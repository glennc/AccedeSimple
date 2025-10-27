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
                await RunTravelWorkflowAsync(userMessage);
                break;

            case UserIntent.StartTripApproval when userInput is ItinerarySelectedChatItem itinerarySelected:
                // Resume the workflow from checkpoint with user's selection
                await ResumeTravelWorkflowAsync(itinerarySelected.MessageId, itinerarySelected);
                break;

            case UserIntent.ProcessReceipts:
                // Start the expense workflow
                await RunKeyedWorkflowAsync("ExpenseWorkflow", userInput);
                break;

            case UserIntent.GenerateExpenseReport:
                // Continue expense workflow - for now run directly
                // TODO: Implement proper workflow continuation
                await RunExecutorDirectlyAsync<ExpenseReportExecutor>(new object());
                break;

            default:
                await _messageService.AddMessageAsync(new AssistantResponse("Unknown intent. Please clarify your request."), _userSettings.UserId);
                break;
        }
    }

    private async Task RunKeyedWorkflowAsync(string workflowKey, object? input = null)
    {
        var workflow = _serviceProvider.GetRequiredKeyedService<Microsoft.Agents.AI.Workflows.Workflow>(workflowKey);
        await using var run = await InProcessExecution.RunAsync(workflow, input);

        // Wait for completion
        foreach (var evt in run.NewEvents)
        {
            // Events are processed, outputs are handled by executors via MessageService
        }
    }

    // Temporary method to run an executor directly without a workflow
    private async Task RunExecutorDirectlyAsync<TExecutor>(object input) where TExecutor : Executor
    {
        var executor = _serviceProvider.GetRequiredService<TExecutor>();
        var workflow = new WorkflowBuilder(executor).Build();
        await using var run = await InProcessExecution.RunAsync(workflow, input);

        foreach (var evt in run.NewEvents)
        {
            // Events are processed, outputs are handled by executors via MessageService
        }
    }

    /// <summary>
    /// Start travel workflow and run until it pauses at a RequestPort
    /// </summary>
    private async Task RunTravelWorkflowAsync(UserMessage userMessage)
    {
        // Generate a unique workflow run ID for this trip planning session
        var workflowRunId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting travel workflow with RunId {RunId}", workflowRunId);

        var workflow = _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>();
        var checkpointManager = CheckpointManager.Default;

        // Start streaming execution with checkpoint support and specific runId
        await using var checkpointedRun = await InProcessExecution.StreamAsync(
            workflow, userMessage, checkpointManager, workflowRunId);

        CheckpointInfo? lastCheckpointInfo = null;

        // Process events until we hit a RequestPort or workflow completes
        await foreach (var evt in checkpointedRun.Run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfoEvt:
                    // Workflow is requesting information - store CheckpointInfo and pause
                    await HandleRequestInfoAndPauseAsync(requestInfoEvt, lastCheckpointInfo);
                    return; // HTTP request completes here

                case SuperStepCompletedEvent superStepEvt:
                    // Save CheckpointInfo metadata for potential pause
                    lastCheckpointInfo = superStepEvt.CompletionInfo?.Checkpoint;
                    break;

                case ExecutorCompletedEvent executorEvt:
                    _logger.LogDebug("Executor {ExecutorId} completed", executorEvt.ExecutorId);
                    break;

                case WorkflowOutputEvent outputEvt:
                    _logger.LogInformation("Workflow completed with output");
                    // Workflow completed without pausing
                    return;

                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    _logger.LogError(exception, "Workflow error");
                    await _messageService.AddMessageAsync(
                        new AssistantResponse($"An error occurred: {exception?.Message ?? "Unknown error"}"),
                        _userSettings.UserId);
                    return;
            }
        }
    }

    /// <summary>
    /// Resume travel workflow with admin approval decision
    /// </summary>
    public async Task ResumeWorkflowWithApprovalAsync(string tripId, TripRequestResult approvalResult)
    {
        await ResumeTravelWorkflowAsync(tripId, approvalResult);
    }

    /// <summary>
    /// Resume travel workflow from CheckpointInfo metadata
    /// </summary>
    /// <param name="correlationId">User-facing ID (messageId or tripId) used to look up checkpoint</param>
    /// <param name="response">The response data to send to the paused RequestPort</param>
    private async Task ResumeTravelWorkflowAsync(string correlationId, object response)
    {
        _logger.LogInformation("Resuming travel workflow for correlation {CorrelationId}", correlationId);

        // Get the stored CheckpointInfo metadata (contains RunId and CheckpointId)
        var checkpointInfo = _stateStore.GetAs<CheckpointInfo>($"checkpoint-info:{correlationId}");
        if (checkpointInfo == null)
        {
            _logger.LogError("No CheckpointInfo found for correlation {CorrelationId}", correlationId);
            await _messageService.AddMessageAsync(
                new AssistantResponse("Sorry, I couldn't find your trip planning session. Please start over."),
                _userSettings.UserId);
            return;
        }

        // Get the stored ExternalRequest
        var storedRequest = _stateStore.GetAs<ExternalRequest>($"pending-request:{correlationId}");
        if (storedRequest == null)
        {
            _logger.LogError("No pending request found for correlation {CorrelationId}", correlationId);
            await _messageService.AddMessageAsync(
                new AssistantResponse("Sorry, I couldn't find your trip planning session. Please start over."),
                _userSettings.UserId);
            return;
        }

        var workflow = _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>();
        var checkpointManager = CheckpointManager.Default;

        // Resume from CheckpointInfo (framework will load the actual checkpoint state using RunId + CheckpointId)
        await using var checkpointedRun = await InProcessExecution.ResumeStreamAsync(
            workflow, checkpointInfo, checkpointManager, checkpointInfo.RunId);

        CheckpointInfo? lastCheckpointInfo = null;
        bool responseSent = false;

        // Continue processing events
        await foreach (var evt in checkpointedRun.Run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfoEvt when !responseSent:
                    // This is the RequestPort we paused at - send our response to it
                    var externalResponse = requestInfoEvt.Request.CreateResponse(response);
                    await checkpointedRun.Run.SendResponseAsync(externalResponse);
                    responseSent = true;
                    break;

                case RequestInfoEvent requestInfoEvt:
                    // Hit another RequestPort - save CheckpointInfo and pause again
                    await HandleRequestInfoAndPauseAsync(requestInfoEvt, lastCheckpointInfo, correlationId);
                    return; // HTTP request completes here

                case SuperStepCompletedEvent superStepEvt:
                    // Save CheckpointInfo metadata for potential pause
                    lastCheckpointInfo = superStepEvt.CompletionInfo?.Checkpoint;
                    break;

                case ExecutorCompletedEvent executorEvt:
                    _logger.LogDebug("Executor {ExecutorId} completed", executorEvt.ExecutorId);
                    break;

                case WorkflowOutputEvent outputEvt:
                    _logger.LogInformation("Workflow completed with output");
                    // Workflow completed - cleanup
                    await CleanupWorkflowStateAsync(correlationId);
                    return;

                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    _logger.LogError(exception, "Workflow error");
                    await _messageService.AddMessageAsync(
                        new AssistantResponse($"An error occurred: {exception?.Message ?? "Unknown error"}"),
                        _userSettings.UserId);
                    await CleanupWorkflowStateAsync(correlationId);
                    return;
            }
        }

        _logger.LogWarning("Resume workflow event stream ended without WorkflowOutputEvent or RequestInfoEvent");
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
        else
        {
            _logger.LogWarning("Unknown request port: {PortId}", request.PortInfo.PortId);
        }
    }

    /// <summary>
    /// Cleanup workflow state after completion
    /// </summary>
    /// <param name="correlationId">The user-facing correlation ID (messageId or tripId)</param>
    private async Task CleanupWorkflowStateAsync(string? correlationId)
    {
        if (correlationId == null)
        {
            return;
        }

        // Remove CheckpointInfo metadata
        _stateStore.Delete($"checkpoint-info:{correlationId}");

        // Remove pending ExternalRequest
        _stateStore.Delete($"pending-request:{correlationId}");

        // Remove trip options if they exist
        _stateStore.Delete($"trip-options:{correlationId}");

        // Remove trip request from the trip-requests list if it exists
        var tripRequests = _stateStore.GetAs<List<TripRequest>>("trip-requests");
        if (tripRequests != null)
        {
            // Remove by TripId (correlationId is the tripId at this point)
            tripRequests.RemoveAll(tr => tr.TripId == correlationId);
            _stateStore.Set("trip-requests", tripRequests);
        }

        await Task.CompletedTask;
    }
}
#pragma warning restore