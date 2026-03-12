using System.Text.Json;
using Devlooped.Extensions.AI;
using DotNetEnv;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using xAI.Protocol;
using Xunit.Abstractions;
using Xunit.Sdk;
using ChatConversation = Devlooped.Extensions.AI.Chat;

namespace xAI.Tests;

public class SanityChecks(ITestOutputHelper output)
{
    [SecretsFact("CI_XAI_API_KEY")]
    public async Task NoEmbeddingModels()
    {
        var services = new ServiceCollection()
            .AddxAIProtocol(Environment.GetEnvironmentVariable("CI_XAI_API_KEY")!)
            .BuildServiceProvider();

        var client = services.GetRequiredService<Models.ModelsClient>();

        var embeddings = await client.ListEmbeddingModelsAsync();

        Assert.NotNull(embeddings);
        Assert.Empty(embeddings);
    }

    [SecretsFact("CI_XAI_API_KEY")]
    public async Task ListModelsAsync()
    {
        var services = new ServiceCollection()
            .AddxAIProtocol(Environment.GetEnvironmentVariable("CI_XAI_API_KEY")!)
            .BuildServiceProvider();

        var client = services.GetRequiredService<Models.ModelsClient>();

        var models = await client.ListLanguageModelsAsync();

        Assert.NotNull(models);

        foreach (var model in models)
            output.WriteLine(model.Name);
    }

    [SecretsFact("CI_XAI_API_KEY")]
    public async Task ExecuteLocalFunctionWithWebSearch()
    {
        var services = new ServiceCollection()
            .AddxAIProtocol(Environment.GetEnvironmentVariable("CI_XAI_API_KEY")!)
            .BuildServiceProvider();

        var client = services.GetRequiredService<xAI.Protocol.Chat.ChatClient>();

        // Define a local function to get the current date
        var getDateFunction = new Function
        {
            Name = "get_date",
            Description = "Get the current date in YYYY-MM-DD format",
            Parameters = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            })
        };

        // Create the request with both a local function and web_search tool
        var request = new GetCompletionsRequest
        {
            Model = "grok-4-1-fast-non-reasoning",
            Messages =
            {
                new Message
                {
                    Role = MessageRole.RoleSystem,
                    Content = { new Content { Text = "You use the get_date function to get the current date, and Nasdaq and Yahoo finance for stocks news." } }
                },
                new Message
                {
                    Role = MessageRole.RoleUser,
                    Content = { new Content { Text = "What's Tesla stock price today?" } }
                }
            },
            Tools =
            {
                new Tool { Function = getDateFunction },
                new Tool { WebSearch = new WebSearch() }
            },
            Include = { IncludeOption.InlineCitations }
        };

        var response = await client.GetCompletionAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response.Id);
        Assert.NotEmpty(response.Id);
        Assert.Equal(request.Model, response.Model);
        Assert.NotEmpty(response.Outputs);

        var firstOutput = response.Outputs[0];
        Assert.NotEmpty(firstOutput.Message.ToolCalls);

        var getDateToolCall = firstOutput.Message.ToolCalls
            .FirstOrDefault(tc => tc.Function?.Name == "get_date" && tc.Type == ToolCallType.ClientSideTool);

        Assert.NotNull(getDateToolCall);
        Assert.NotNull(getDateToolCall.Id);
        Assert.NotEmpty(getDateToolCall.Id);
        Assert.Equal("get_date", getDateToolCall.Function.Name);

        output.WriteLine($"Found client-side tool call: {getDateToolCall.Function.Name} (ID: {getDateToolCall.Id})");

        var currentDate = DateTime.Now.ToString("yyyy-MM-dd");

        var call = new Message
        {
            Role = MessageRole.RoleAssistant,
        };
        call.ToolCalls.AddRange(firstOutput.Message.ToolCalls);

        var followUpRequest = new GetCompletionsRequest
        {
            Model = request.Model,
            Messages =
            {
                request.Messages[0],
                call,
                new Message
                {
                    Role = MessageRole.RoleTool,
                    Content = { new Content { Text = currentDate } }
                }
            },
            Tools = { request.Tools },
            Include = { IncludeOption.InlineCitations, IncludeOption.WebSearchCallOutput }
        };

        var followUpResponse = await client.GetCompletionAsync(followUpRequest);

        Assert.NotNull(followUpResponse);
        Assert.NotEmpty(followUpResponse.Outputs);

        var remainingClientToolCalls = followUpResponse.Outputs
            .SelectMany(x => x.Message.ToolCalls)
            .Where(tc => tc.Type == ToolCallType.ClientSideTool);

        Assert.Empty(remainingClientToolCalls);

        var finalOutput = followUpResponse.Outputs.Last();
        Assert.NotNull(finalOutput.Message.Content);
        Assert.NotEmpty(finalOutput.Message.Content);
    }

    [SecretsTheory("CI_XAI_API_KEY")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientSideFunction(bool streaming)
    {
        var getDateCalls = 0;
        var grok = new GrokClient(Env.GetString("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast")
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        var options = new GrokChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Tools =
            [
                AIFunctionFactory.Create(() =>
                {
                    getDateCalls++;
                    return DateTime.Now.ToString("yyyy-MM-dd");
                }, "get_date", "Gets the current date in YYYY-MM-DD format"),
            ]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", """
                What is today's date? Use the get_date tool.
                Respond with a JSON object: { "today": "[date in YYYY-MM-DD format]" }
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);

        Assert.True(getDateCalls >= 1, "get_date function was not called");

        var result = ParseJson<DateResult>(response, output);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), result.Today);
        output.WriteLine($"Today: {result.Today}");
    }

    [SecretsTheory("CI_XAI_API_KEY")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgenticWebSearch(bool streaming)
    {
        var grok = new GrokClient(Env.GetString("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast");

        var options = new GrokChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Include = [IncludeOption.WebSearchCallOutput],
            Tools = [new HostedWebSearchTool()]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", """
                What is the current price of Tesla (TSLA) stock? Use web search (Yahoo Finance or similar).
                Respond with a JSON object: { "tesla_price": [numeric price] }
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);

        // Verify web search tool was used
        var webSearchCalls = response.Messages
            .SelectMany(x => x.Contents.Select(c => c.RawRepresentation as xAI.Protocol.ToolCall))
            .Where(x => x?.Type == xAI.Protocol.ToolCallType.WebSearchTool)
            .ToList();
        Assert.NotEmpty(webSearchCalls);

        // Verify citations were produced
        var citations = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .ToList();
        Assert.NotEmpty(citations);

        var result = ParseJson<TeslaPriceResult>(response, output);
        Assert.True(result.TeslaPrice > 100, $"Tesla price {result.TeslaPrice} should be > 100");
        output.WriteLine($"Tesla price: {result.TeslaPrice}");
    }

    [SecretsTheory("CI_XAI_API_KEY")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgenticXSearch(bool streaming)
    {
        var grok = new GrokClient(Env.GetString("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast");

        var options = new GrokChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Include = [IncludeOption.XSearchCallOutput],
            Tools = [new GrokXSearchTool { AllowedHandles = ["tesla"] }]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", """
                What is the top news from Tesla on X? Use the X search tool.
                Respond with a JSON object: { "tesla_news": "[top news headline or summary]" }
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);

        // Verify X search tool was used
        var xSearchCalls = response.Messages
            .SelectMany(x => x.Contents.Select(c => c.RawRepresentation as xAI.Protocol.ToolCall))
            .Where(x => x?.Type == xAI.Protocol.ToolCallType.XSearchTool)
            .ToList();
        Assert.NotEmpty(xSearchCalls);

        var result = ParseJson<TeslaNewsResult>(response, output);
        Assert.NotNull(result.TeslaNews);
        Assert.NotEmpty(result.TeslaNews);
        output.WriteLine($"Tesla X news: {result.TeslaNews}");
    }

    [SecretsTheory("CI_XAI_API_KEY", "GITHUB_TOKEN")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgenticMcpServer(bool streaming)
    {
        var grok = new GrokClient(Env.GetString("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast");

        var options = new GrokChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Include = [IncludeOption.McpCallOutput],
            Tools =
            [
                new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/")
                {
                    Headers = new Dictionary < string, string > {["Authorization"] = Env.GetString("GITHUB_TOKEN") ! },
                    AllowedTools = ["list_releases", "get_release_by_tag"],
                }
            ]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", $$"""
                What is the latest release version of the {{ThisAssembly.Git.Url}} repository? Use the GitHub MCP tool.
                Respond with a JSON object: { "latest_release": "[version string]" }
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);

        var mcpCalls = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<McpServerToolCallContent>()
            .ToList();
        Assert.NotEmpty(mcpCalls);

        var mcpResults = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<McpServerToolResultContent>()
            .ToList();
        Assert.NotEmpty(mcpResults);

        var result = ParseJson<LatestReleaseResult>(response, output);
        Assert.NotNull(result.LatestRelease);
        Assert.Contains(".", result.LatestRelease);
        output.WriteLine($"Latest release: {result.LatestRelease}");
        output.WriteLine($"MCP calls: {mcpCalls.Count}");
    }

    [SecretsTheory("CI_XAI_API_KEY")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgenticFileSearch(bool streaming)
    {
        var grok = new GrokClient(Env.GetString("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast");

        var options = new GrokChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Include = [IncludeOption.CollectionsSearchCallOutput],
            ToolMode = ChatToolMode.RequireAny,
            Tools =
            [
                new HostedFileSearchTool
                {
                    Inputs = [new HostedVectorStoreContent("collection_91559d9b-a55d-42fe-b2ad-ecf8904d9049")]
                }
            ]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", """
                What is the law number of Código Procesal Civil y Comercial de la Nación?
                Use the collection search tool. 
                Respond with a JSON object: { "law_number": [numeric law number without group separator] }
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);

        Assert.Contains(
            response.Messages.SelectMany(x => x.Contents).OfType<CollectionSearchToolCallContent>()
                .Select(x => x.RawRepresentation as xAI.Protocol.ToolCall),
            x => x?.Type == xAI.Protocol.ToolCallType.CollectionsSearchTool);

        var files = response.Messages
            .SelectMany(x => x.Contents).OfType<CollectionSearchToolResultContent>()
            .SelectMany(x => (x.Outputs ?? []).OfType<HostedFileContent>())
            .ToList();

        if (files.Count == 0)
        {
            var search = response.Messages
                .SelectMany(x => x.Contents).OfType<CollectionSearchToolResultContent>()
                .First();

            Assert.Fail("Expected at least one file in the collection search results: " +
                new ChatMessage(ChatRole.Tool, search.Outputs).Text);
        }

        output.WriteLine(string.Join(", ", files.Select(x => x.Name)));
        Assert.Contains(files, x => x.Name?.Contains("LNS0004592") == true);

        var result = ParseJson<LawNumberResult>(response, output);
        Assert.Equal(17454, result.LawNumber);
        output.WriteLine($"Law number: {result.LawNumber}");
    }

    /// <summary>
    /// Code execution is flaky and can produce:
    /// Grpc.Core.RpcException : Status(StatusCode="Unavailable", Detail="Bad gRPC response. HTTP status code: 504")
    /// </summary>
    [SecretsTheory("CI_XAI_API_KEY")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgenticCodeInterpreter(bool streaming)
    {
        var client = new GrokClient(Env.GetString("CI_XAI_API_KEY")!);

        var grok = client.AsIChatClient("grok-4-1-fast");

        var options = new GrokChatOptions
        {
            Include = [IncludeOption.CodeExecutionCallOutput],
            Tools = [new HostedCodeInterpreterTool()]
        };

        var chat = new ChatConversation
        {
            { "system", "You are a helpful assistant." },
            { "user", """
                Calculate the earnings produced by compound interest on $5,000 at 4% annually for 5 years.
                Use the code interpreter. Return just the earnings number (not the total principal + earnings), 
                no additional text or formatting, and no explanation. The output should be a single numeric value 
                parseable by a decimal parser.
                """ }
        };

        var response = await GetResponseAsync(grok, chat, options, streaming);
        output.WriteLine($"Compound interest: {response.Text}");

        var codeInterpreterCalls = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<CodeInterpreterToolCallContent>()
            .ToList();
        Assert.NotEmpty(codeInterpreterCalls);

        var codeInterpreterResults = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<CodeInterpreterToolResultContent>()
            .ToList();
        Assert.NotEmpty(codeInterpreterResults);

        // Formula: P(1 + r)^t - P = 5000 * (1.04)^5 - 5000 ≈ $1,083.26
        Assert.NotEmpty(response.Text);
        Assert.True(decimal.TryParse(response.Text, out var result), $"Could not parse response {response.Text}");

        Assert.True(result > 1000 && result < 1200,
            $"Compound interest {result} should be between 1000 and 1200");
        output.WriteLine($"Code interpreter calls: {codeInterpreterCalls.Count}");
    }

    static async Task<ChatResponse> GetResponseAsync(IChatClient client, ChatConversation chat, GrokChatOptions options, bool streaming)
    {
        if (!streaming)
            return await client.GetResponseAsync(chat, options);

        var updates = await client.GetStreamingResponseAsync(chat, options).ToListAsync();
        return updates.ToChatResponse();
    }

    static T ParseJson<T>(ChatResponse response, ITestOutputHelper output)
    {
        var responseText = response.Messages.Last().Text;
        Assert.NotNull(responseText);
        output.WriteLine("Response text:");
        output.WriteLine(responseText);

        var jsonStart = responseText.IndexOf('{');
        var jsonEnd = responseText.LastIndexOf('}');
        Assert.True(jsonStart >= 0 && jsonEnd > jsonStart, "Response did not contain a JSON object");

        var json = responseText[jsonStart..(jsonEnd + 1)];
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        Assert.NotNull(result);
        return result;
    }

    record DateResult(string Today);
    record TeslaPriceResult(decimal TeslaPrice);
    record TeslaNewsResult(string TeslaNews);
    record LatestReleaseResult(string LatestRelease);
    record LawNumberResult(int LawNumber);
}
