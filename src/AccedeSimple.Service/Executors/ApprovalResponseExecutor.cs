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
        // Send the approval/rejection message to the user
        // The result parameter contains all the information we need (TripId, status, etc.)
        var message = new TripRequestDecisionChatItem(result);
        await _messageService.AddMessageAsync(message, _userSettings.UserId);

        // Return the result to complete the workflow
        return result;
    }
}
#pragma warning restore
