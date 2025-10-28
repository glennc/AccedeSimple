#pragma warning disable
using System.Text.Json;
using Microsoft.Extensions.AI;
using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;

namespace AccedeSimple.Service.Executors;

public class TripRequestCreationExecutor(
    IChatClient chatClient,
    IOptions<UserSettings> userSettings,
    StateStore stateStore,
    ILogger<TripRequestCreationExecutor> logger) : Executor<ItinerarySelectedChatItem, TripRequest>("TripRequestCreationExecutor")
{
    private readonly UserSettings _userSettings = userSettings.Value;
    private readonly IChatClient _chatClient = chatClient;
    private readonly StateStore _stateStore = stateStore;
    private readonly ILogger<TripRequestCreationExecutor> _logger = logger;

    public override async ValueTask<TripRequest> HandleAsync(
        ItinerarySelectedChatItem userInput,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var stateKey = $"trip-options:{userInput.TripId}";
        var options = _stateStore.GetAs<List<TripOption>>(stateKey);

        if (options == null)
        {
            _logger.LogError("Trip options not found in StateStore for key {StateKey}", stateKey);
            throw new InvalidOperationException($"Trip options not found for trip {userInput.TripId}");
        }

        // Find the selected option
        var selectedOption = options.FirstOrDefault(o => o.OptionId == userInput.OptionId);

        if (selectedOption == null)
        {
            _logger.LogError("Selected option {OptionId} not found in trip options", userInput.OptionId);
            throw new InvalidOperationException($"Selected trip option {userInput.OptionId} not found");
        }

        var tripId = userInput.TripId;

        // Create trip request directly with the selected option
        var tripRequest = new TripRequest(
            TripId: tripId,
            TripOption: selectedOption,
            AdditionalNotes: null
        );

        // Clean up trip options from StateStore
        _stateStore.Delete(stateKey);

        return tripRequest;
    }
}
#pragma warning restore
