using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AccedeSimple.Service.Executors;

internal class PolicyExecutor(
    [FromKeyedServices("Policy")] AIAgent policyAgent,
    StateStore stateStore) : Executor<List<ChatMessage>, TripPlanner>("ttt")
{
    public override async ValueTask<TripPlanner> HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var resp = await policyAgent.RunAsync(messages, cancellationToken: cancellationToken);

        var trip = new TripPlanner();
        trip.PolicyComments.Add(resp.Text);
        trip.Conversation.AddRange(messages);

        return trip;
    }

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
    {
        return routeBuilder.AddHandler<ChatMessage, TripPlanner>(HandleAsync);
    }

    public async ValueTask<TripPlanner> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Check if there's an existing trip planning session
        var activeTripEntries = stateStore.Search(entry => entry.Key.StartsWith("trip-options:"));
        
        TripPlanner trip;
        
        if (activeTripEntries.Any())
        {
            // Refinement - load existing trip from StateStore
            var tripEntry = activeTripEntries.OrderByDescending(e => e.Timestamp).First();
            var tripId = tripEntry.Key.Substring("trip-options:".Length);
            var existingOptions = tripEntry.Value as List<TripOption>;
            
            // Load the full trip planner state if available
            var existingTrip = stateStore.GetAs<TripPlanner>($"trip-planner:{tripId}");
            
            if (existingTrip != null)
            {
                trip = existingTrip;
            }
            else
            {
                // Fallback: create new with existing options
                trip = new TripPlanner();
                if (existingOptions != null)
                {
                    trip.TripOptions.AddRange(existingOptions);
                }
            }
            
            // Add the new message to the conversation
            trip.Conversation.Add(message);
            
            // Keep existing trip options - TravelPlanningExecutor will see them
            // and can update/refine them based on the user's feedback
            
            // Clean up old stored data (we'll store fresh data after refinement)
            stateStore.Delete($"trip-options:{tripId}");
            stateStore.Delete($"trip-planner:{tripId}");
        }
        else
        {
            // New trip - run through policy agent
            var resp = await policyAgent.RunAsync(message, cancellationToken: cancellationToken);
            
            trip = new TripPlanner();
            trip.PolicyComments.Add(resp.Text);
            trip.Conversation.Add(message);
        }

        return trip;
    }
}