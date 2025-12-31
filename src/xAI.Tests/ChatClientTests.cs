using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Devlooped.Extensions.AI;
using Microsoft.Extensions.AI;
using Moq;
using Tests.Client.Helpers;
using xAI;
using xAI.Protocol;
using static ConfigurationExtensions;
using Chat = Devlooped.Extensions.AI.Chat;
using OpenAIClientOptions = OpenAI.OpenAIClientOptions;

namespace xAI.Tests;

public class ChatClientTests(ITestOutputHelper output)
{
    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesTools()
    {
        var messages = new Chat()
        {
            { "system", "You are a bot that invokes the tool get_date when asked for the date." },
            { "user", "What day is today?" },
        };

        var chat = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4")
            .AsBuilder()
            .UseLogging(output.AsLoggerFactory())
            .Build();

        var options = new GrokChatOptions
        {
            ModelId = "grok-4-fast-non-reasoning",
            Tools = [AIFunctionFactory.Create(() => DateTimeOffset.Now.ToString("O"), "get_date")],
            AdditionalProperties = new()
            {
                { "foo", "bar" }
            }
        };

        var response = await chat.GetResponseAsync(messages, options);
        var getdate = response.Messages
            .SelectMany(x => x.Contents.OfType<FunctionCallContent>())
            .Any(x => x.Name == "get_date");

        Assert.True(getdate);
        // NOTE: the chat client was requested as grok-3 but the chat options wanted a 
        // different model and the grok client honors that choice.
        Assert.Equal(options.ModelId, response.ModelId);
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesToolAndSearch()
    {
        var messages = new Chat()
        {
            { "system", "You use Nasdaq for stocks news and prices." },
            { "user", "What's Tesla stock worth today?" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4")
            .AsBuilder()
            .UseFunctionInvocation()
            .UseLogging(output.AsLoggerFactory())
            .Build();

        var getDateCalls = 0;
        var options = new GrokChatOptions
        {
            ModelId = "grok-4-1-fast-non-reasoning",
            Search = GrokSearch.Web,
            Tools = [AIFunctionFactory.Create(() =>
            {
                getDateCalls++;
                return DateTimeOffset.Now.ToString("O");
            }, "get_date", "Gets the current date")],
        };

        var response = await grok.GetResponseAsync(messages, options);

        // The get_date result shows up as a tool role
        Assert.Contains(response.Messages, x => x.Role == ChatRole.Tool);

        // Citations include nasdaq.com at least as a web search source
        var urls = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .Select(x => x.Url!)
            .ToList();

        Assert.Equal(1, getDateCalls);
        Assert.Contains(urls, x => x.Host.EndsWith("nasdaq.com"));
        Assert.Contains(urls, x => x.PathAndQuery.Contains("/TSLA"));
        Assert.Equal(options.ModelId, response.ModelId);

        var calls = response.Messages
            .SelectMany(x => x.Contents.OfType<HostedToolCallContent>())
            .Select(x => x.RawRepresentation as xAI.Protocol.ToolCall)
            .Where(x => x is not null)
            .ToList();

        Assert.NotEmpty(calls);
        Assert.Contains(calls, x => x?.Type == xAI.Protocol.ToolCallType.WebSearchTool);
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesSpecificSearchUrl()
    {
        var messages = new Chat()
        {
            { "system", "Sos un asistente del Cerro Catedral, usas la funcionalidad de Live Search en el sitio oficial." },
            { "system", $"Hoy es {DateTime.Now.ToString("o")}" },
            { "user", "Que calidad de nieve hay hoy?" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-1-fast-non-reasoning");

        var options = new ChatOptions
        {
            Tools = [new GrokSearchTool()
            {
                AllowedDomains = [ "catedralaltapatagonia.com" ]
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);
        var text = response.Text;

        var citations = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations ?? [])
            .OfType<CitationAnnotation>()
            .Where(x => x.Url != null)
            .Select(x => x.Url!.AbsoluteUri)
            .ToList();

        Assert.Contains("https://partediario.catedralaltapatagonia.com/partediario/", citations);
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesHostedSearchTool()
    {
        var messages = new Chat()
        {
            { "system", "You are an AI assistant that knows how to search the web." },
            { "user", "What's Tesla stock worth today? Search X, Yahoo and the news for latest info." },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new GrokChatOptions
        {
            Include = { IncludeOption.WebSearchCallOutput },
            Tools = [new HostedWebSearchTool()]
        };

        var response = await grok.GetResponseAsync(messages, options);
        var text = response.Text;

        Assert.Contains("TSLA", text);
        Assert.NotNull(response.ModelId);

        var urls = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .Select(x => x.Url!)
            .ToList();

        Assert.Contains(urls, x => x.Host == "finance.yahoo.com");
        Assert.Contains(urls, x => x.PathAndQuery.Contains("/TSLA"));
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesGrokSearchToolIncludesDomain()
    {
        var messages = new Chat()
        {
            { "system", "You are an AI assistant that knows how to search the web." },
            { "user", "What is the latest news about Microsoft?" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new ChatOptions
        {
            Tools = [new GrokSearchTool
            {
                AllowedDomains = ["microsoft.com", "news.microsoft.com"],
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);

        Assert.NotNull(response.Text);
        Assert.Contains("Microsoft", response.Text);

        var urls = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .Select(x => x.Url!)
            .ToList();

        foreach (var url in urls)
        {
            output.WriteLine(url.ToString());
        }

        Assert.All(urls, x => x.Host.EndsWith(".microsoft.com"));
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesGrokSearchToolExcludesDomain()
    {
        var messages = new Chat()
        {
            { "system", "You are an AI assistant that knows how to search the web." },
            { "user", "What is the latest news about Microsoft?" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new ChatOptions
        {
            Tools = [new GrokSearchTool
            {
                ExcludedDomains = ["blogs.microsoft.com"]
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);

        Assert.NotNull(response.Text);
        Assert.Contains("Microsoft", response.Text);

        var urls = response.Messages
            .SelectMany(x => x.Contents)
            .SelectMany(x => x.Annotations?.OfType<CitationAnnotation>() ?? [])
            .Where(x => x.Url is not null)
            .Select(x => x.Url!)
            .ToList();

        foreach (var url in urls)
        {
            output.WriteLine(url.ToString());
        }

        Assert.DoesNotContain(urls, x => x.Host == "blogs.microsoft.com");
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesHostedCodeExecution()
    {
        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var response = await grok.GetResponseAsync(
            "Calculate the compound interest for $10,000 at 5% annually for 10 years",
            new ChatOptions
            {
                Tools = [new HostedCodeInterpreterTool()]
            });

        var text = response.Text;

        Assert.Contains("$6,288.95", text);
        Assert.NotEmpty(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<CodeInterpreterToolCallContent>());

        // result content is not available by default
        Assert.Empty(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<CodeInterpreterToolResultContent>());
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesHostedCodeExecutionWithOutput()
    {
        var messages = new Chat()
        {
            { "user", "Calculate the compound interest for $10,000 at 5% annually for 10 years" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new GrokChatOptions
        {
            Include = { IncludeOption.CodeExecutionCallOutput },
            Tools = [new HostedCodeInterpreterTool()]
        };

        var response = await grok.GetResponseAsync(messages, options);

        Assert.Contains("$6,288.95", response.Text);
        Assert.NotEmpty(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<CodeInterpreterToolCallContent>());

        // result content opted-in is found
        Assert.NotEmpty(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<CodeInterpreterToolResultContent>());
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GrokInvokesHostedCollectionSearch()
    {
        var messages = new Chat()
        {
            { "user", "¿Cuál es el monto exacto del rango de la multa por inasistencia injustificada a la audiencia señalada por el juez en el proceso sucesorio, según lo establecido en el Artículo 691 del Código Procesal Civil y Comercial de la Nación (Ley 17.454)?" },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new GrokChatOptions
        {
            Include = { IncludeOption.CollectionsSearchCallOutput },
            Tools = [new HostedFileSearchTool {
                Inputs = [new HostedVectorStoreContent("collection_91559d9b-a55d-42fe-b2ad-ecf8904d9049")]
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);
        var text = response.Text;

        Assert.Contains("11,74", text);
        Assert.Contains(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<HostedToolCallContent>()
                .Select(x => x.RawRepresentation as xAI.Protocol.ToolCall),
            x => x?.Type == xAI.Protocol.ToolCallType.CollectionsSearchTool);
    }

    [SecretsFact("XAI_API_KEY", "GITHUB_TOKEN")]
    public async Task GrokInvokesHostedMcp()
    {
        var messages = new Chat()
        {
            { "user", "When was GrokClient v1.0.0 released on the devlooped/GrokClient repo? Respond with just the date, in YYYY-MM-DD format." },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new ChatOptions
        {
            Tools = [new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/") {
                AuthorizationToken = Configuration["GITHUB_TOKEN"]!,
                AllowedTools = ["list_releases"],
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);
        var text = response.Text;

        Assert.Equal("2025-11-29", text);
        var call = Assert.Single(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<McpServerToolCallContent>());

        Assert.Equal("GitHub.list_releases", call.ToolName);
    }

    [SecretsFact("XAI_API_KEY", "GITHUB_TOKEN")]
    public async Task GrokInvokesHostedMcpWithOutput()
    {
        var messages = new Chat()
        {
            { "user", "When was GrokClient v1.0.0 released on the devlooped/GrokClient repo? Respond with just the date, in YYYY-MM-DD format." },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");

        var options = new GrokChatOptions
        {
            Include = { IncludeOption.McpCallOutput },
            Tools = [new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/") {
                AuthorizationToken = Configuration["GITHUB_TOKEN"]!,
                AllowedTools = ["list_releases"],
            }]
        };

        var response = await grok.GetResponseAsync(messages, options);

        // Can include result of MCP tool
        var output = Assert.Single(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<McpServerToolResultContent>());

        Assert.NotNull(output.Output);
        Assert.Single(output.Output);
        var json = Assert.Single(output.Output!.OfType<TextContent>()).Text;
        var tags = JsonSerializer.Deserialize<List<Release>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(tags);
        Assert.Contains(tags, x => x.TagName == "v1.0.0");
    }

    record Release(string TagName, DateTimeOffset CreatedAt);

    [SecretsFact("XAI_API_KEY", "GITHUB_TOKEN")]
    public async Task GrokStreamsUpdatesFromAllTools()
    {
        var messages = new Chat()
        {
            { "user",
                """
                What's the oldest stable version released on the devlooped/GrokClient repo on GitHub?, 
                what is the current price of Tesla stock, 
                and what is the current date? Respond with the following JSON: 
                {
                  "today": "[get_date result]",
                  "release": "[first stable release of devlooped/GrokClient, using GitHub MCP tool]",
                  "price": [$TSLA price using web search tool]
                }
                """
            },
        };

        var grok = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIChatClient("grok-4-fast")
            .AsBuilder()
            .UseFunctionInvocation()
            .UseLogging(output.AsLoggerFactory())
            .Build();

        var getDateCalls = 0;
        var options = new GrokChatOptions
        {
            Include = { IncludeOption.McpCallOutput },
            Tools =
            [
                new HostedWebSearchTool(),
                new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/") {
                    AuthorizationToken = Configuration["GITHUB_TOKEN"]!,
                    AllowedTools = ["list_releases", "get_release_by_tag"],
                },
                AIFunctionFactory.Create(() => {
                    getDateCalls++;
                    return DateTimeOffset.Now.ToString("O");
                }, "get_date", "Gets the current date")
            ]
        };

        var updates = await grok.GetStreamingResponseAsync(messages, options).ToListAsync();
        var response = updates.ToChatResponse();
        var typed = JsonSerializer.Deserialize<Response>(response.Messages.Last().Text, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(typed);

        Assert.NotEmpty(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<McpServerToolCallContent>());

        Assert.Contains(response.Messages
                .SelectMany(x => x.Contents)
                .OfType<HostedToolCallContent>()
                .Select(x => x.RawRepresentation as xAI.Protocol.ToolCall),
            x => x?.Type == xAI.Protocol.ToolCallType.WebSearchTool);

        Assert.Equal(1, getDateCalls);

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), typed.Today);
        Assert.EndsWith("1.0.0", typed.Release);
        Assert.True(typed.Price > 100);
    }

    [Fact]
    public async Task GrokCustomFactoryInvokedFromOptions()
    {
        var invoked = false;
        var client = new Mock<xAI.Protocol.Chat.ChatClient>(MockBehavior.Strict);
        client.Setup(x => x.GetCompletionAsync(It.IsAny<GetCompletionsRequest>(), null, null, CancellationToken.None))
            .Returns(CallHelpers.CreateAsyncUnaryCall(new GetChatCompletionResponse
            {
                Outputs =
                {
                    new CompletionOutput
                    {
                        Message = new CompletionMessage
                        {
                            Content = "Hey Cazzulino!"
                        }
                    }
                }
            }));

        var grok = new GrokChatClient(client.Object, "grok-4-1-fast");
        var response = await grok.GetResponseAsync("Hi, my internet alias is kzu. Lookup my real full name online.",
            new GrokChatOptions
            {
                RawRepresentationFactory = (client) =>
                {
                    invoked = true;
                    return new GetCompletionsRequest();
                }
            });

        Assert.True(invoked);
        Assert.Equal("Hey Cazzulino!", response.Text);
    }

    record Response(DateOnly Today, string Release, decimal Price);
}
