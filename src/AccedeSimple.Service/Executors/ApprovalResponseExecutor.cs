#pragma warning disable
using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;

namespace AccedeSimple.Service.Executors;
public class ApprovalResponseExecutor(
    MessageService messageService,
    IOptions<UserSettings> userSettings,
    ILogger<ApprovalResponseExecutor> logger) : Executor<TripRequestResult, TripRequestResult>("ApprovalResponseExecutor")
{
    private readonly UserSettings _userSettings = userSettings.Value;
    private readonly MessageService _messageService = messageService;
    private readonly ILogger<ApprovalResponseExecutor> _logger = logger;

    public override async ValueTask<TripRequestResult> HandleAsync(
        TripRequestResult result,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // Read trip requests from workflow state
        var requests = await context.ReadStateAsync<List<TripRequest>>("trip-requests", cancellationToken);

        var request = requests?.FirstOrDefault(r => r.TripId == result.TripId);

        if (request != null && requests != null)
        {
            var updatedRequests = requests.Where(r => r.TripId != result.TripId).ToList();
            await context.QueueStateUpdateAsync("trip-requests", updatedRequests, cancellationToken);
        }

        // Send the approval/rejection message to the user
        var message = new TripRequestDecisionChatItem(result);
        await _messageService.AddMessageAsync(message, _userSettings.UserId);

        // Return the result to complete the workflow
        return result;
    }
}
#pragma warning restore
