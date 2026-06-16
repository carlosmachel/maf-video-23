#pragma warning disable OPENAI001 // Suppress experimental API warnings for Responses API usage.
#pragma warning disable MAAI001   // Suppress experimental API warnings for Agents AI experiments.

using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using dotenv.net;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

DotEnv.Load();

var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");

var model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-5.4";

const int maxContextWindowTokens = 1_050_000;
const int maxOutputTokens = 128_000;

var projectClient = new AIProjectClient(
    new Uri(endpoint),
    // WARNING: DefaultAzureCredential is convenient for development but requires careful
    // consideration in production. Consider ManagedIdentityCredential instead.
    new DefaultAzureCredential(),
    new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) });

// --- Background agent: Web Search Agent ---
// Uses the HarnessAgent's built-in HostedWebSearchTool.
// Features not needed by this sub-agent are disabled to keep it focused.
AIAgent webSearchAgent = projectClient
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(model)
    .AsHarnessAgent(maxContextWindowTokens, maxOutputTokens, new HarnessAgentOptions
    {
        Name = "WebSearchAgent",
        Description = "An agent that can search the web to find information.",
        DisableTodoProvider = true,
        DisableAgentModeProvider = true,
        DisableFileMemory = true,
        DisableFileAccess = true,
        DisableToolApproval = true,
        ChatOptions = new ChatOptions
        {
            Instructions = "You are a web search assistant. When asked to find information, use the web search tool to look it up and return a concise, factual answer.",
        },
    });

var getCurrentDateTimeTool = AIFunctionFactory.Create(
    () => DateTime.Now.ToString("MMMM d, yyyy"),
    new AIFunctionFactoryOptions
    {
        Name = "GetCurrentDateTime",
        Description = "Returns today's current date."
    });

// --- Parent agent: Stock Price Researcher ---
// Orchestrates the web search agent to look up stock prices concurrently.
var parentInstructions =
    """
    You are a stock price research assistant. You have access to a web search background agent that can look up information on the web.
    When given a list of stock tickers, your job is to find the most recent closing price for each ticker.

    ## Workflow

    1. Call GetCurrentDateTime to get today's date. Use that date when searching for the most recent closing prices.
    2. For each ticker, start a background task on the WebSearchAgent asking it to find the closing price for that date (or the most recent trading day before it).
       - Start all background tasks before waiting for any of them to complete, so they run concurrently.
    3. Wait for all background tasks to complete.
    4. Retrieve the results from each background task.
    5. Present a summary table with the ticker symbol and closing price for each stock.
    6. Clear all completed tasks to free memory.

    ## Important

    - Always call GetCurrentDateTime first to know today's date. Never assume or hardcode a date.
    - Always delegate web searches to the WebSearchAgent background agent. Do not try to answer from memory.
    - If a background task fails or returns unclear results, retry with a more specific query.
    - Present results in a clean markdown table format.
    """;

AIAgent parentAgent = projectClient
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(model)
    .AsHarnessAgent(maxContextWindowTokens, maxOutputTokens, new HarnessAgentOptions
    {
        Name = "StockPriceResearcher",
        Description = "An agent that researches stock prices using background agents.",
        DisableTodoProvider = true,
        DisableAgentModeProvider = true,
        DisableFileMemory = true,
        DisableFileAccess = true,
        DisableToolApproval = true,
        DisableWebSearch = true,
        BackgroundAgents = [webSearchAgent],
        ChatOptions = new ChatOptions
        {
            Instructions = parentInstructions,
            MaxOutputTokens = 16_000,
            Tools = [getCurrentDateTimeTool],
        },
    });

Console.OutputEncoding = Encoding.UTF8;
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("=== Stock Price Research Agent ===");
Console.WriteLine("Enter stock tickers (e.g., BAC, MSFT, BA)");
Console.WriteLine("Commands: /exit /clear");
Console.ResetColor();
Console.WriteLine();

AgentSession session = await parentAgent.CreateSessionAsync();

while (true)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("> ");
    Console.ResetColor();

    string? input = Console.ReadLine();

    if (input is null || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
        break;

    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        session = await parentAgent.CreateSessionAsync();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[Session cleared]");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    if (string.IsNullOrWhiteSpace(input))
        continue;

    Console.WriteLine();

    var messages = new[] { new ChatMessage(ChatRole.User, input) };
    bool wroteText = false;

    await foreach (var update in parentAgent.RunStreamingAsync(messages, session))
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent { Text: { Length: > 0 } text }:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(text);
                    wroteText = true;
                    break;

                case FunctionCallContent call:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[Calling: {call.Name}]");
                    break;

                case ErrorContent error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nError: {error.Message}");
                    break;
            }
        }
    }

    if (wroteText) Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine();
}

Console.ResetColor();
Console.WriteLine("Goodbye!");
