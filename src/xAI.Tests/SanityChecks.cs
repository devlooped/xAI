using System.Text.Json;
using Devlooped.Extensions.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using xAI.Protocol;
using Xunit.Abstractions;
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

    /// <summary>
    /// Comprehensive integration test (non-streaming) that exercises all major features:
    /// - Client-side tool invocation (AIFunctionFactory)
    /// - Hosted web search tool
    /// - Hosted code interpreter tool
    /// - Hosted MCP server tool (GitHub)
    /// - Citations and annotations
    /// </summary>
    [SecretsFact("CI_XAI_API_KEY", "GITHUB_TOKEN")]
    public async Task IntegrationTest()
    {
        var (grok, options, getDateCalls) = SetupIntegrationTest();

        var response = await grok.GetResponseAsync(CreateIntegrationChat(), options);

        AssertIntegrationTest(response, getDateCalls);
    }

    [SecretsFact("CI_XAI_API_KEY", "GITHUB_TOKEN")]
    public async Task IntegrationTestStreaming()
    {
        var (grok, options, getDateCalls) = SetupIntegrationTest();

        var updates = await grok.GetStreamingResponseAsync(CreateIntegrationChat(), options).ToListAsync();
        var response = updates.ToChatResponse();

        AssertIntegrationTest(response, getDateCalls);
    }

    static ChatConversation CreateIntegrationChat() => new()
    {
        { "system", "You are a helpful assistant that uses all available tools to answer questions accurately." },
        { "user",
            $$"""
            Current timestamp is {{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}.

            Please answer the following questions using the appropriate tools:
            1. What is today's date? (use get_date tool)
            2. What is the current price of Tesla (TSLA) stock? (use Yahoo news web search)
            3. Calculate the earnings that would be produced by compound interest to $5k at 4% annually for 5 years (use code interpreter)
            4. What is the latest release version of the devlooped/GrokClient repository? (use GitHub MCP tool)
            
            Respond with a JSON object in this exact format:
            {
              "today": "[date from get_date in YYYY-MM-DD format]",
              "tesla_price": [numeric price from web search],
              "compound_interest": [numeric result from code interpreter],
              "latest_release": "[version string from GitHub]"
            }
            """
        }
    };

    static (IChatClient grok, GrokChatOptions options, Func<int> getDateCalls) SetupIntegrationTest()
    {
        var getDateCalls = 0;
        var grok = new GrokClient(Environment.GetEnvironmentVariable("CI_XAI_API_KEY")!)
            .AsIChatClient("grok-4-1-fast-reasoning")
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        var options = new GrokChatOptions
        {
            Include =
            [
                IncludeOption.InlineCitations,
                IncludeOption.WebSearchCallOutput,
                IncludeOption.CodeExecutionCallOutput,
                IncludeOption.McpCallOutput
            ],
            Tools =
            [
                // Client-side tool
                AIFunctionFactory.Create(() =>
                {
                    getDateCalls++;
                    return DateTime.Now.ToString("yyyy-MM-dd");
                }, "get_date", "Gets the current date in YYYY-MM-DD format"),

                // Hosted web search tool
                new HostedWebSearchTool(),

                // Hosted code interpreter tool
                new HostedCodeInterpreterTool(),

                // Hosted MCP server tool (GitHub)
                new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/")
                {
                    AuthorizationToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")!,
                    AllowedTools = ["list_releases", "get_release_by_tag"],
                }
            ]
        };

        return (grok, options, () => getDateCalls);
    }

    void AssertIntegrationTest(ChatResponse response, Func<int> getDateCalls)
    {
        // Verify response basics
        Assert.NotNull(response);
        Assert.NotNull(response.ModelId);
        Assert.NotEmpty(response.Messages);

        // Verify client-side tool was invoked
        Assert.True(getDateCalls() >= 1);

        // Verify web search tool was used
        var webSearchCalls = response.Messages
            .SelectMany(x => x.Contents.Select(c => c.RawRepresentation as xAI.Protocol.ToolCall))
            .Where(x => x?.Type == xAI.Protocol.ToolCallType.WebSearchTool)
            .ToList();
        Assert.NotEmpty(webSearchCalls);

        // Verify code interpreter tool was used
        var codeInterpreterCalls = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<CodeInterpreterToolCallContent>()
            .ToList();
        Assert.NotEmpty(codeInterpreterCalls);

        // Verify code interpreter output was included
        var codeInterpreterResults = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<CodeInterpreterToolResultContent>()
            .ToList();
        Assert.NotEmpty(codeInterpreterResults);

        // Verify MCP tool was used
        var mcpCalls = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<McpServerToolCallContent>()
            .ToList();
        Assert.NotEmpty(mcpCalls);

        // Verify MCP output was included
        var mcpResults = response.Messages
            .SelectMany(x => x.Contents)
            .OfType<McpServerToolResultContent>()
            .ToList();
        Assert.NotEmpty(mcpResults);

        // Verify citations from web search
        Assert.NotEmpty(response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .Select(x => x.Url!));

        // Parse and validate the JSON response
        var responseText = response.Messages.Last().Text;
        Assert.NotNull(responseText);

        output.WriteLine("Response text:");
        output.WriteLine(responseText);

        // Extract JSON from response (may be wrapped in markdown code blocks)
        var jsonStart = responseText.IndexOf('{');
        var jsonEnd = responseText.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var result = JsonSerializer.Deserialize<IntegrationTestResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            Assert.NotNull(result);

            // Verify date is today
            Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), result.Today);

            // Verify Tesla price is reasonable (greater than $100)
            Assert.True(result.TeslaPrice > 100, $"Tesla price {result.TeslaPrice} should be > 100");

            // Verify compound interest calculation is approximately correct
            // Formula: P(1 + r)^t - P = 5000 * (1.04)^5 - 5000 ≈ $1,083.26
            Assert.True(result.CompoundInterest > 1000 && result.CompoundInterest < 1200,
                $"Compound interest {result.CompoundInterest} should be between 1000 and 1200");

            // Verify latest release contains version pattern
            Assert.NotNull(result.LatestRelease);
            Assert.Contains(".", result.LatestRelease);

            output.WriteLine($"Parsed response: Today={result.Today}, TeslaPrice={result.TeslaPrice}, CompoundInterest={result.CompoundInterest}, LatestRelease={result.LatestRelease}");
        }

        output.WriteLine($"Code interpreter calls: {codeInterpreterCalls.Count}");
        output.WriteLine($"MCP calls: {mcpCalls.Count}");
    }

    record IntegrationTestResponse(
        string Today,
        decimal TeslaPrice,
        decimal CompoundInterest,
        string LatestRelease);
}
