using System.Text.Json;
using Microsoft.Extensions.AI;
using AccedeSimple.Domain;
using ModelContextProtocol.Client;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AccedeSimple.Service.Executors;

public class TravelPlanningExecutor(
    ILogger<TravelPlanningExecutor> logger,
    IChatClient chatClient,
    McpClient mcpClient,
    MessageService messageService,
    IOptions<UserSettings> userSettings) : Executor("TravelPlanningExecutor")
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly ILogger<TravelPlanningExecutor> _logger = logger;
    private readonly McpClient _mcpClient = mcpClient;
    private readonly MessageService _messageService = messageService;
    private readonly UserSettings _userSettings = userSettings.Value;

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
    {
        return routeBuilder.AddHandler<TripPlanner, TripPlanner>(HandleAsync);
    }

    public async ValueTask<TripPlanner> HandleAsync(TripPlanner trip, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Check if this is a refinement (has existing options or multiple conversation messages)
        bool isRefinement = trip.TripOptions.Any() || trip.Conversation.Count > 1;
        
        // Notify user about what we're doing
        var statusMessage = isRefinement 
            ? "Refining your trip options based on your feedback..." 
            : "Planning your trip...";
        await _messageService.AddMessageAsync(new TripRequestUpdated(statusMessage), _userSettings.UserId);

        // Generate new trip parameters
        var tripParameterPrompt =
            $"""
            You are a travel assistant. Your task is to generate trip parameters based on the user input.

            The user has provided the following information:

            {string.Join(Environment.NewLine, trip.Conversation.Select(msg => msg.ToString()))}

            Today's date is: {DateTime.Now.ToString()}

            Take any policy comments into consideration:

            {string.Join(Environment.NewLine, trip.PolicyComments)}
            """;

        // If this is a refinement, include existing trip options for context
        if (isRefinement && trip.TripOptions.Any())
        {
            tripParameterPrompt += $"""


            CONTEXT: The user is refining their search. Here are the current trip options:

            {JsonSerializer.Serialize(trip.TripOptions, new JsonSerializerOptions { WriteIndented = true })}

            Based on the user's latest message, extract the updated trip parameters.
            Keep parameters that aren't being changed and update only what the user is asking to modify.
            """;
        }
        else
        {
            tripParameterPrompt += """


            Generate trip parameters
            """;
        }

        var res = await _chatClient.GetResponseAsync<TripParameters>(tripParameterPrompt, cancellationToken: cancellationToken);

        res.TryGetResult(out var tripParameters);

        // Build the prompt for generating trip options
        var tripOptionsPrompt = $"""
            You are a travel planning assistant. Generate trip options based on the provided parameters.

            {JsonSerializer.Serialize(tripParameters)}

            Consider factors like cost, convenience, and preferences. Each option should include:
            - Flight details (departure/arrival times, airline, price)
            - Hotel options (location, check-in/out dates, price)
            - Car rental options if requested

            Ensure that there is a variety of options to choose from, including different airlines, hotels, and car rental companies.

            Generate at least 3 different trip options with a detailed breakdown of each option.

            Ensure that dates are formatted correctly.
            """;

        // If this is a refinement, include the existing options for context
        if (isRefinement && trip.TripOptions.Any())
        {
            tripOptionsPrompt += $"""


            IMPORTANT: This is a refinement request. Here are the existing trip options that were previously generated:

            {JsonSerializer.Serialize(trip.TripOptions, new JsonSerializerOptions { WriteIndented = true })}

            Based on the user's latest feedback in the conversation, update or modify these existing options.
            You can adjust flights, hotels, dates, prices, or completely replace options if the feedback warrants it.
            Keep what works and change what doesn't based on the user's refinement request.
            """;
        }

        List<ChatMessage> messages = [
            new ChatMessage(ChatRole.User, tripOptionsPrompt)
        ];

        var tools = await _mcpClient.ListToolsAsync();

        var response = await _chatClient.GetResponseAsync<List<TripOption>>(
            messages,
            new ChatOptions
            {
                Temperature = 0.7f,
                Tools = [.. tools]
            },
            cancellationToken: cancellationToken);

        response.TryGetResult(out var result);

        var options = result ?? [];

        // Clear existing options and add the new/refined ones
        trip.TripOptions.Clear();
        trip.TripOptions.AddRange(options);

        return trip;
    }
}
