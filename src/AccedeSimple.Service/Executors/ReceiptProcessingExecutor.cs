#pragma warning disable
using System.Diagnostics;
using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.AI.Evaluation.Safety;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;

namespace AccedeSimple.Service.Executors;

public class ReceiptProcessingExecutor(
    IChatClient chatClient,
    MessageService messageService,
    IOptions<UserSettings> userSettings) : Executor<UserMessage, List<ReceiptData>>("ReceiptProcessingExecutor")
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly MessageService _messageService = messageService;
    private readonly UserSettings _userSettings = userSettings.Value;

    private static readonly ChatConfiguration s_SafetyChatConfiguration =
        new ContentSafetyServiceConfiguration(
            credential: new ChainedTokenCredential(new AzureCliCredential(), new DefaultAzureCredential()),
            subscriptionId: Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID"),
            resourceGroupName: Environment.GetEnvironmentVariable("AZURE_RESOURCE_GROUP"),
            projectName: Environment.GetEnvironmentVariable("AZURE_AI_FOUNDRY_PROJECT"))
                .ToChatConfiguration();

    private static readonly ReportingConfiguration s_reportingConfiguation =
        DiskBasedReportingConfiguration.Create(
            storageRootPath: Path.Combine(Path.GetTempPath(), "AccedeSimple"),
            evaluators: [new ContentHarmEvaluator()],
            chatConfiguration: s_SafetyChatConfiguration);

    public override async ValueTask<List<ReceiptData>> HandleAsync(
        UserMessage userInput,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var (success, elapsed) = await CheckImageSafetyAsync(userInput);
        if (success)
        {
            var receiptResponse = await _chatClient.GetResponseAsync<List<ReceiptData>>(userInput.ToChatMessage(), cancellationToken: cancellationToken);

            if (receiptResponse.TryGetResult(out var receipts))
            {
                // Store processed receipts in workflow state
                await context.QueueStateUpdateAsync("receipts", receipts, cancellationToken);

                await _messageService.AddMessageAsync(
                    new AssistantResponse(
                        $"Receipts processed successfully in {elapsed} s. No unsafe content detected."),
                    _userSettings.UserId);

                return receipts;
            }
            else
            {
                await _messageService.AddMessageAsync(
                    new AssistantResponse(
                        $"Receipts processing failed after {elapsed} s. Failed to parse receipt content."),
                    _userSettings.UserId);
            }
        }

        return [];
    }

    async Task<(bool success, string elapsed)> CheckImageSafetyAsync(UserMessage userInput)
    {
        var userRequest = userInput.ToChatMessage();

        // Azure AI Foundry Evaluation service requires an assistant message to be included as part of the evaluation.
        var modelResponse = new ChatMessage(ChatRole.Assistant, "Processing...");

        // Generate a scenario name that uniquely identifies the request content being evaluated.
        var scenarioName = $"Image Safety Check {AIJsonUtilities.HashDataToString([userRequest])}";
        await using var scenarioRun = await s_reportingConfiguation.CreateScenarioRunAsync(scenarioName);

        var s = Stopwatch.StartNew();
        var result = await scenarioRun.EvaluateAsync(userRequest, modelResponse);
        s.Stop();

        result.TryGet<NumericMetric>(HateAndUnfairnessEvaluator.HateAndUnfairnessMetricName, out var hateAndUnfairness);
        result.TryGet<NumericMetric>(SelfHarmEvaluator.SelfHarmMetricName, out var selfHarm);
        result.TryGet<NumericMetric>(SexualEvaluator.SexualMetricName, out var sexual);
        result.TryGet<NumericMetric>(ViolenceEvaluator.ViolenceMetricName, out var violence);

        var elapsed = s.Elapsed.TotalSeconds.ToString("F2");

        var success =
            await ValidateAsync(hateAndUnfairness) &&
            await ValidateAsync(selfHarm) &&
            await ValidateAsync(sexual) &&
            await ValidateAsync(violence);

        return (success, elapsed);

        async Task<bool> ValidateAsync(EvaluationMetric? metric)
        {
            if (metric is null)
            {
                // Ignore metrics that were not found.
                return true;
            }

            if (metric.Interpretation!.Rating is not EvaluationRating.Exceptional)
            {
                // Fail if the metric interpretation is anything other than exceptional.
                await _messageService.AddMessageAsync(
                    message: new AssistantResponse(
                        $"Receipts processing failed after {elapsed} s. Potentially unsafe content detected: {metric.Reason}"),
                    _userSettings.UserId);

                return false;
            }

            return true;
        }
    }
}
#pragma warning restore
