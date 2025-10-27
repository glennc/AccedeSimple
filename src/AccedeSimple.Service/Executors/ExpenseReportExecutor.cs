#pragma warning disable
using System.Text.Json;
using Microsoft.Extensions.AI;
using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;

namespace AccedeSimple.Service.Executors;

public class ExpenseReportExecutor(
    IChatClient chatClient,
    MessageService messageService,
    IOptions<UserSettings> userSettings) : Executor<object>("ExpenseReportExecutor")
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly MessageService _messageService = messageService;
    private readonly UserSettings _userSettings = userSettings.Value;

    public override async ValueTask HandleAsync(
        object input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // Read receipts from workflow state
        var receipts = await context.ReadStateAsync<List<ReceiptData>>("receipts", cancellationToken);

        // Convert receipts to expense items
        var expenseItems = receipts?.Select(r => new ExpenseItem(
            Description: r.Description,
            Amount: r.Amount,
            Category: r.Category,
            Date: r.Date ?? DateTime.Now,
            ReceiptReference: r.Id,
            Notes: null
        )).ToList() ?? [];

        // Calculate total expenses
        var totalExpenses = expenseItems.Sum(e => e.Amount);

        // Expense report
        var report = new ExpenseReport(
            ReportId: Guid.NewGuid().ToString(),
            TripId: null,
            UserId: $"{_userSettings.UserId}",
            TotalExpenses: totalExpenses,
            Items: expenseItems,
            Status: ExpenseReportStatus.Draft
        );

        // Generate a summary of the expense report
        var summaryPrompt =
            $"""
            You are an expense report assistant. Your task is to generate a summary of the expense report.

            Make sure to include the following information:
            - Total Expenses
            - Items (Description, Amount, Category, Date, Receipt Reference)

            The user has provided the following information:

            {JsonSerializer.Serialize(report)}

            Today's date is: {DateTime.Now.ToString()}

            Generate a summary of the expense report:
            """;

        var summaryResponse = await _chatClient.GetResponseAsync(summaryPrompt, cancellationToken: cancellationToken);

        await _messageService.AddMessageAsync(new AssistantResponse(summaryResponse.Text), _userSettings.UserId);
    }
}
#pragma warning restore
