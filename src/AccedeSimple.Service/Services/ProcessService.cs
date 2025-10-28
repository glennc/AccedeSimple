#pragma warning disable
using System.Text.Json;
using AccedeSimple.Domain;
using AccedeSimple.Service.Executors;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AccedeSimple.Service.Services;

public class ProcessService
{
    private readonly MessageService _messageService;
    private readonly IChatClient _chatClient;
    private readonly UserSettings _userSettings;
    private readonly HttpClient _httpClient;
    private readonly AIAgent _policyAgent;
    private readonly IServiceProvider _serviceProvider;
    private readonly StateStore _stateStore;
    private readonly ILogger<ProcessService> _logger;

    public ProcessService(
        MessageService messageService,
        IChatClient chatClient,
        IOptions<UserSettings> userSettings,
        IHttpClientFactory httpClientFactory,
        [FromKeyedServices("Policy")] AIAgent policyAgent,
        IServiceProvider serviceProvider,
        StateStore stateStore,
        ILogger<ProcessService> logger)
    {
        _messageService = messageService;
        _chatClient = chatClient;
        _userSettings = userSettings.Value;
        _httpClient = httpClientFactory.CreateClient("LocalGuide");
        _policyAgent = policyAgent;
        _serviceProvider = serviceProvider;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task ActAsync(UserIntent userIntent, ChatItem userInput)
    {
        switch (userIntent)
        {
            case UserIntent.General:
                // Handle general inquiries
                var response = await _chatClient.GetResponseAsync(userInput.ToChatMessage());
                await _messageService.AddMessageAsync(new AssistantResponse(response.Text), _userSettings.UserId);
                break;

            case UserIntent.AskLocalGuide:
                // Handle local guide inquiries
                var builder = new UriBuilder(_httpClient.BaseAddress)
                {
                    Path = "attractions",
                    Query = $"query={Uri.EscapeDataString(userInput.Text)}"
                };
                var localGuideRequest = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, builder.Uri));
                var body = await localGuideRequest.Content.ReadAsStringAsync();
                await _messageService.AddMessageAsync(new AssistantResponse(body), _userSettings.UserId);
                break;

            case UserIntent.AskPolicyQuestions:
                // Use the policy agent for policy inquiries
                var policyResponse = await _policyAgent.RunAsync(userInput.ToChatMessage());
                await _messageService.AddMessageAsync(new AssistantResponse(policyResponse.Text), _userSettings.UserId);
                break;

            case UserIntent.StartTravelPlanning when userInput is UserMessage userMessage:
                // Start the travel workflow - await until it pauses at RequestPort
                await RunOrResumeWorkflowAsync(
                    workflowKey: null,
                    workflowName: "travel workflow",
                    input: userMessage);
                break;

            case UserIntent.StartTripApproval when userInput is ItinerarySelectedChatItem itinerarySelected:
                // Resume the workflow from checkpoint with user's selection
                await RunOrResumeWorkflowAsync<UserMessage>(
                    workflowKey: null,
                    workflowName: "travel workflow",
                    correlationId: itinerarySelected.MessageId,
                    response: itinerarySelected,
                    sessionNotFoundMessage: "Sorry, I couldn't find your trip planning session. Please start over.");
                break;

            case UserIntent.ProcessReceipts when userInput is UserMessage receiptMessage:
                // Start the expense workflow - await until it pauses at GenerateReportConfirmation RequestPort
                await RunOrResumeWorkflowAsync(
                    workflowKey: "ExpenseWorkflow",
                    workflowName: "expense workflow",
                    input: receiptMessage);
                break;

            case UserIntent.GenerateExpenseReport:
                // Resume the expense workflow from checkpoint
                var receiptSessionId = _stateStore.GetAs<string>($"receipt-session:{_userSettings.UserId}:latest");
                if (receiptSessionId != null)
                {
                    await RunOrResumeWorkflowAsync<UserMessage>(
                        workflowKey: "ExpenseWorkflow",
                        workflowName: "expense workflow",
                        correlationId: receiptSessionId,
                        response: new object(),
                        sessionNotFoundMessage: "Sorry, I couldn't find your receipt processing session. Please start over.");
                }
                else
                {
                    await _messageService.AddMessageAsync(
                        new AssistantResponse("No receipts have been processed yet. Please upload receipts first."),
                        _userSettings.UserId);
                }
                break;

            default:
                await _messageService.AddMessageAsync(new AssistantResponse("Unknown intent. Please clarify your request."), _userSettings.UserId);
                break;
        }
    }

    /// <summary>
    /// Run or resume a workflow. If correlationId is provided, resumes from checkpoint; otherwise starts new.
    /// </summary>
    /// <typeparam name="TInput">The input type for the workflow (e.g., UserMessage)</typeparam>
    /// <param name="workflowKey">Optional key for keyed workflow service. If null, uses default workflow.</param>
    /// <param name="input">Input for starting a new workflow (required when correlationId is null)</param>
    /// <param name="correlationId">Correlation ID for resuming an existing workflow</param>
    /// <param name="response">Response to send when resuming (required when correlationId is not null)</param>
    private async Task RunOrResumeWorkflowAsync<TInput>(
        string? workflowKey,
        string workflowName,
        TInput? input = default,
        string? correlationId = null,
        object? response = null,
        string? sessionNotFoundMessage = null) where TInput : notnull
    {
        var checkpointManager = CheckpointManager.Default;

        // Resolve workflow - MUST be done inside this method to get a fresh instance each time
        var workflow = workflowKey == null
            ? _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>()
            : _serviceProvider.GetRequiredKeyedService<Microsoft.Agents.AI.Workflows.Workflow>(workflowKey);

        // Determine if we're resuming or starting new
        if (correlationId != null)
        {
            // Resuming from checkpoint
            _logger.LogInformation("Resuming {WorkflowName} for correlation {CorrelationId}", workflowName, correlationId);

            // Get the stored CheckpointInfo metadata (contains RunId and CheckpointId)
            var checkpointInfo = _stateStore.GetAs<CheckpointInfo>($"checkpoint-info:{correlationId}");
            if (checkpointInfo == null)
            {
                _logger.LogError("No CheckpointInfo found for correlation {CorrelationId}", correlationId);
                await _messageService.AddMessageAsync(new AssistantResponse(sessionNotFoundMessage ?? "Session not found."), _userSettings.UserId);
                return;
            }

            // Get the stored ExternalRequest
            var storedRequest = _stateStore.GetAs<ExternalRequest>($"pending-request:{correlationId}");
            if (storedRequest == null)
            {
                _logger.LogError("No pending request found for correlation {CorrelationId}", correlationId);
                await _messageService.AddMessageAsync(new AssistantResponse(sessionNotFoundMessage ?? "Session not found."), _userSettings.UserId);
                return;
            }

            await using var checkpointedRun = await InProcessExecution.ResumeStreamAsync(workflow, checkpointInfo, checkpointManager, checkpointInfo.RunId);
            await ProcessWorkflowEventsAsync(checkpointedRun, correlationId, response);
        }
        else
        {
            // Starting new workflow
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input), "Input is required when starting a new workflow");
            }

            var workflowRunId = Guid.NewGuid().ToString();
            _logger.LogInformation("Starting {WorkflowName} with RunId {RunId}", workflowName, workflowRunId);

            // Generic type parameter TInput is properly inferred from the caller, ensuring correct type routing
            await using var checkpointedRun = await InProcessExecution.StreamAsync(workflow, input, checkpointManager, workflowRunId);
            await ProcessWorkflowEventsAsync(checkpointedRun, null, null);
        }
    }

    private async Task ProcessWorkflowEventsAsync(
        Checkpointed<StreamingRun> checkpointedRun,
        string? correlationId,
        object? response)
    {
        CheckpointInfo? lastCheckpointInfo = null;
        bool responseSent = false;

        // Process events
        await foreach (var evt in checkpointedRun.Run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfoEvt when correlationId != null && !responseSent:
                    // Resuming: send response to the paused RequestPort
                    var externalResponse = requestInfoEvt.Request.CreateResponse(response!);
                    await checkpointedRun.Run.SendResponseAsync(externalResponse);
                    responseSent = true;
                    break;

                case RequestInfoEvent requestInfoEvt:
                    // Hit a RequestPort - save CheckpointInfo and pause
                    _logger.LogInformation("Workflow paused at RequestPort {PortId}", requestInfoEvt.Request.PortInfo.PortId);

                    await HandleRequestInfoAndPauseAsync(requestInfoEvt, lastCheckpointInfo, correlationId);
                    return;

                case SuperStepCompletedEvent superStepEvt:
                    _logger.LogInformation("SuperStep completed");
                    lastCheckpointInfo = superStepEvt.CompletionInfo?.Checkpoint;
                    break;

                case ExecutorCompletedEvent executorEvt:
                    _logger.LogInformation("Executor {ExecutorId} completed", executorEvt.ExecutorId);
                    break;

                case WorkflowOutputEvent outputEvt:
                    _logger.LogInformation("Workflow completed with output");
                    if (correlationId != null)
                    {
                        await CleanupWorkflowStateAsync(correlationId);
                    }
                    return;

                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    _logger.LogError(exception, "Workflow error");
                    await _messageService.AddMessageAsync(
                        new AssistantResponse($"An error occurred: {exception?.Message ?? "Unknown error"}"),
                        _userSettings.UserId);
                    if (correlationId != null)
                    {
                        await CleanupWorkflowStateAsync(correlationId);
                    }
                    return;
            }
        }

        if (correlationId != null)
        {
            _logger.LogWarning("Resume workflow event stream ended without WorkflowOutputEvent or RequestInfoEvent");
        }
    }

    /// <summary>
    /// Resume travel workflow with admin approval decision
    /// </summary>
    public async Task ResumeWorkflowWithApprovalAsync(string tripId, TripRequestResult approvalResult)
    {
        await RunOrResumeWorkflowAsync<UserMessage>(
            workflowKey: null,
            workflowName: "travel workflow",
            correlationId: tripId,
            response: approvalResult,
            sessionNotFoundMessage: "Sorry, I couldn't find your trip planning session. Please start over.");
    }

    /// <summary>
    /// Handle a RequestInfoEvent by storing CheckpointInfo metadata and sending message to user
    /// </summary>
    /// <param name="requestInfoEvt">The RequestInfoEvent from the workflow</param>
    /// <param name="checkpointInfo">CheckpointInfo metadata (contains RunId and CheckpointId)</param>
    /// <param name="correlationId">The correlation ID from the previous pause (for subsequent pauses in same workflow)</param>
    private async Task HandleRequestInfoAndPauseAsync(RequestInfoEvent requestInfoEvt, CheckpointInfo? checkpointInfo, string? correlationId = null)
    {
        var request = requestInfoEvt.Request;

        if (checkpointInfo == null)
        {
            _logger.LogError("No CheckpointInfo available when pausing at RequestPort {PortId}", request.PortInfo.PortId);
            return;
        }

        // If this is a subsequent pause in the same workflow, clean up previous checkpoint data
        if (correlationId != null)
        {
            _logger.LogDebug("Cleaning up previous checkpoint data for correlation {CorrelationId}", correlationId);
            _stateStore.Delete($"checkpoint-info:{correlationId}");
            _stateStore.Delete($"pending-request:{correlationId}");
            _stateStore.Delete($"trip-options:{correlationId}");
        }

        if (request.PortInfo.PortId == "UserSelection")
        {
            // User needs to select a trip option
            var tripOptions = request.DataAs<List<TripOption>>();
            if (tripOptions != null)
            {
                // Create the message with its own Id - this becomes our user-facing correlation ID
                var candidateMessage = new CandidateItineraryChatItem("Here are trips matching your requirements.", tripOptions);

                // Store CheckpointInfo metadata by messageId (user will resume with this ID)
                _stateStore.Set($"checkpoint-info:{candidateMessage.Id}", checkpointInfo);

                // Store the ExternalRequest so we can respond when user resumes
                _stateStore.Set($"pending-request:{candidateMessage.Id}", request);

                // Store trip options for TripRequestCreationExecutor to access
                _stateStore.Set($"trip-options:{candidateMessage.Id}", tripOptions);

                // Send trip options to user
                await _messageService.AddMessageAsync(candidateMessage, _userSettings.UserId);
            }
        }
        else if (request.PortInfo.PortId == "AdminApproval")
        {
            // Admin needs to approve the trip request
            var tripRequest = request.DataAs<TripRequest>();
            if (tripRequest != null)
            {
                // Store CheckpointInfo metadata by tripId (admin will resume with this ID)
                _stateStore.Set($"checkpoint-info:{tripRequest.TripId}", checkpointInfo);

                // Store the ExternalRequest so we can respond when admin approves/rejects
                _stateStore.Set($"pending-request:{tripRequest.TripId}", request);

                // Store trip request in StateStore so admin page can access it
                var existingRequests = _stateStore.GetAs<List<TripRequest>>("trip-requests") ?? new List<TripRequest>();
                existingRequests.Add(tripRequest);
                _stateStore.Set("trip-requests", existingRequests);

                // Send approval request message
                await _messageService.AddMessageAsync(
                    new AssistantResponse($"Trip request created. Awaiting admin approval for trip {tripRequest.TripId}."),
                    _userSettings.UserId);
            }
        }
        else if (request.PortInfo.PortId == "GenerateReportConfirmation")
        {
            // User needs to confirm expense report generation
            var receipts = request.DataAs<List<ReceiptData>>();
            if (receipts != null)
            {
                // Generate a unique session ID for this receipt processing session
                var receiptSessionId = Guid.NewGuid().ToString();

                // Store CheckpointInfo metadata by receiptSessionId (user will resume with this ID)
                _stateStore.Set($"checkpoint-info:{receiptSessionId}", checkpointInfo);

                // Store the ExternalRequest so we can respond when user confirms
                _stateStore.Set($"pending-request:{receiptSessionId}", request);

                // Store receipt session ID for later retrieval
                _stateStore.Set($"receipt-session:{_userSettings.UserId}:latest", receiptSessionId);

                // Send confirmation message
                await _messageService.AddMessageAsync(
                    new AssistantResponse($"Receipts processed. Ready to generate expense report. Say 'generate expense report' when ready."),
                    _userSettings.UserId);
            }
        }
        else
        {
            _logger.LogWarning("Unknown request port: {PortId}", request.PortInfo.PortId);
        }
    }

    /// <summary>
    /// Cleanup common workflow checkpoint state after completion
    /// </summary>
    /// <param name="correlationId">The user-facing correlation ID</param>
    private async Task CleanupWorkflowStateAsync(string? correlationId)
    {
        if (correlationId == null)
        {
            return;
        }

        // Remove common checkpoint data
        _stateStore.Delete($"checkpoint-info:{correlationId}");
        _stateStore.Delete($"pending-request:{correlationId}");

        // Cleanup workflow-specific data based on what exists in state
        // Travel workflow: trip-options, trip-requests
        _stateStore.Delete($"trip-options:{correlationId}");
        var tripRequests = _stateStore.GetAs<List<TripRequest>>("trip-requests");
        if (tripRequests != null)
        {
            tripRequests.RemoveAll(tr => tr.TripId == correlationId);
            _stateStore.Set("trip-requests", tripRequests);
        }

        // Expense workflow: receipt-session
        var receiptSessionKey = $"receipt-session:{_userSettings.UserId}:latest";
        var storedReceiptSessionId = _stateStore.GetAs<string>(receiptSessionKey);
        if (storedReceiptSessionId == correlationId)
        {
            _stateStore.Delete(receiptSessionKey);
        }

        await Task.CompletedTask;
    }
}
#pragma warning restore