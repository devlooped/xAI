using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Moq;
using OpenAI.Responses;
using xAI.Protocol;

namespace xAI.Tests;

public class GrokConversionTests
{
    [Fact]
    public void AsTool_WithWebSearch()
    {
        var webSearch = new HostedWebSearchTool();

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
    }

    [Fact]
    public void AsTool_WithWebSearch_ThrowsIfAllowedAndExcluded()
    {
        var webSearch = new GrokSearchTool
        {
            AllowedDomains = ["Foo"],
            ExcludedDomains = ["Bar"]
        };

        Assert.Throws<NotSupportedException>(() => webSearch.AsProtocolTool());
    }

    [Fact]
    public void AsTool_WithWebSearch_AllowedDomains()
    {
        var webSearch = new GrokSearchTool
        {
            AllowedDomains = ["foo.com", "bar.com"],
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.Equal(["foo.com", "bar.com"], tool.WebSearch.AllowedDomains);
    }

    [Fact]
    public void AsTool_WithWebSearch_ExcludedDomains()
    {
        var webSearch = new GrokSearchTool
        {
            ExcludedDomains = ["foo.com", "bar.com"],
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.Equal(["foo.com", "bar.com"], tool.WebSearch.ExcludedDomains);
    }

    [Fact]
    public void AsTool_WithWebSearch_ImageUnderstanding()
    {
        var webSearch = new GrokSearchTool
        {
            EnableImageUnderstanding = true
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.True(tool.WebSearch.EnableImageUnderstanding);
    }

    [Fact]
    public void AsTool_WithWebSearch_UserLocation()
    {
        var webSearch = new GrokSearchTool
        {
            Country = "US",
            Region = "California",
            City = "San Francisco",
            Timezone = "America/Los_Angeles"
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.NotNull(tool.WebSearch.UserLocation);
        Assert.Equal("US", tool.WebSearch.UserLocation.Country);
        Assert.Equal("California", tool.WebSearch.UserLocation.Region);
        Assert.Equal("San Francisco", tool.WebSearch.UserLocation.City);
        Assert.Equal("America/Los_Angeles", tool.WebSearch.UserLocation.Timezone);
    }

    [Fact]
    public void AsTool_WithWebSearch_UserLocation_PartialFields()
    {
        var webSearch = new GrokSearchTool
        {
            Country = "DE",
            City = "Berlin"
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.NotNull(tool.WebSearch.UserLocation);
        Assert.Equal("DE", tool.WebSearch.UserLocation.Country);
        Assert.Equal("Berlin", tool.WebSearch.UserLocation.City);
        Assert.Empty(tool.WebSearch.UserLocation.Region);
        Assert.Empty(tool.WebSearch.UserLocation.Timezone);
    }

    [Fact]
    public void AsTool_WithWebSearch_NoUserLocation()
    {
        var webSearch = new GrokSearchTool();

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.Null(tool.WebSearch.UserLocation);
    }

    [Fact]
    public void AsTool_WithXSearch_ThrowsIfAllowedAndExcluded()
    {
        var webSearch = new GrokXSearchTool
        {
            AllowedHandles = ["Foo"],
            ExcludedHandles = ["Bar"]
        };

        Assert.Throws<NotSupportedException>(() => webSearch.AsProtocolTool());
    }

    [Fact]
    public void AsTool_WithXSearch_AllowedHandles()
    {
        var webSearch = new GrokXSearchTool
        {
            AllowedHandles = ["foo", "bar"],
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.XSearch);
        Assert.Equal(["foo", "bar"], tool.XSearch.AllowedXHandles);
    }

    [Fact]
    public void AsTool_WithXSearch_ExcludedDomains()
    {
        var webSearch = new GrokXSearchTool
        {
            ExcludedHandles = ["foo", "bar"],
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.XSearch);
        Assert.Equal(["foo", "bar"], tool.XSearch.ExcludedXHandles);
    }

    [Fact]
    public void AsTool_WithXSearch_ImageUnderstanding()
    {
        var webSearch = new GrokXSearchTool
        {
            EnableImageUnderstanding = true
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.XSearch);
        Assert.True(tool.XSearch.EnableImageUnderstanding);
    }

    [Fact]
    public void AsTool_WithXSearch_VideoUnderstanding()
    {
        var webSearch = new GrokXSearchTool
        {
            EnableVideoUnderstanding = true
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.XSearch);
        Assert.True(tool.XSearch.EnableVideoUnderstanding);
    }

    [Fact]
    public void AsTool_WithXSearch_FromTo()
    {
        var webSearch = new GrokXSearchTool
        {
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var tool = webSearch.AsProtocolTool();

        Assert.NotNull(tool?.XSearch);
        Assert.Equal(tool.XSearch.FromDate, Timestamp.FromDateTime(webSearch.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        Assert.Equal(tool.XSearch.ToDate, Timestamp.FromDateTime(webSearch.ToDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
    }

    [Fact]
    public void AsTool_WithFunctionTool()
    {
        var functionTool = AIFunctionFactory.Create(() => "", "Name", "Description");

        var tool = functionTool.AsProtocolTool();

        Assert.NotNull(tool?.Function);
        Assert.Equal("Name", tool.Function.Name);
        Assert.Equal("Description", tool.Function.Description);
    }

    [Fact]
    public void AsTool_WithCodeExecution()
    {
        var codeTool = new HostedCodeInterpreterTool();

        var tool = codeTool.AsProtocolTool();

        Assert.NotNull(tool?.CodeExecution);
    }

    [Fact]
    public void AsTool_WithHostedFileSearchTool()
    {
        var collectionId = Guid.NewGuid().ToString();
        var instructions = "Return N/A if no results found";
        var fileSearch = new HostedFileSearchTool()
        {
            MaximumResultCount = 50,
            Inputs = [new HostedVectorStoreContent(collectionId)]
        }.WithInstructions(instructions);

        var tool = fileSearch.AsProtocolTool();

        Assert.NotNull(tool?.CollectionsSearch);
        Assert.Contains(collectionId, tool.CollectionsSearch.CollectionIds);
        Assert.Equal(50, tool.CollectionsSearch.Limit);
        Assert.Equal(instructions, tool.CollectionsSearch.Instructions);
    }

    [Fact]
    public void AsTool_WithHostedMcpTool()
    {
        var accessToken = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, string>
        {
            ["foo"] = "baz"
        };
        var mcpTool = new HostedMcpServerTool("foo", "foo.com", new Dictionary<string, object?>
        {
            ["x-extra"] = "bar",
            [nameof(MCP.ExtraHeaders)] = headers
        })
        {
            AllowedTools = ["list"],
            Headers = new Dictionary<string, string> { ["Authorization"] = accessToken },
        };

        var tool = mcpTool.AsProtocolTool();

        Assert.NotNull(tool?.Mcp);
        Assert.Equal("foo", tool.Mcp.ServerLabel);
        Assert.Equal("foo.com", tool.Mcp.ServerUrl);
        Assert.Contains("list", tool.Mcp.AllowedToolNames);
        Assert.Equal(accessToken, tool.Mcp.Authorization);
        Assert.Contains(KeyValuePair.Create("x-extra", "bar"), tool.Mcp.ExtraHeaders);
        Assert.Contains(KeyValuePair.Create("foo", "baz"), tool.Mcp.ExtraHeaders);
    }

    static IGrokChatClient CreateClient()
    {
        var mock = new Mock<IGrokChatClient>();
        mock.SetupGet(x => x.DefaultModelId).Returns("grok-4");
        mock.SetupGet(x => x.EndUserId).Returns((string?)null);
        return mock.Object;
    }

    static AITool DummyTool() => AIFunctionFactory.Create(() => "", "dummy", "A dummy tool");

    [Fact]
    public void AsCompletionsRequest_NoTools_DoesNotSetToolChoice()
    {
        // xAI rejects ToolChoice when no tools are present
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = null });

        Assert.Null(request.ToolChoice);
    }

    [Fact]
    public void AsCompletionsRequest_NullToolMode_SetsAutoToolChoice()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = null, Tools = [DummyTool()] });

        Assert.NotNull(request.ToolChoice);
        Assert.True(request.ToolChoice.HasMode);
        Assert.Equal(Protocol.ToolMode.Auto, request.ToolChoice.Mode);
    }

    [Fact]
    public void AsCompletionsRequest_AutoToolMode_SetsAutoToolChoice()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = ChatToolMode.Auto, Tools = [DummyTool()] });

        Assert.NotNull(request.ToolChoice);
        Assert.True(request.ToolChoice.HasMode);
        Assert.Equal(Protocol.ToolMode.Auto, request.ToolChoice.Mode);
    }

    [Fact]
    public void AsCompletionsRequest_NoneToolMode_SetsNoneToolChoice()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = ChatToolMode.None, Tools = [DummyTool()] });

        Assert.NotNull(request.ToolChoice);
        Assert.True(request.ToolChoice.HasMode);
        Assert.Equal(Protocol.ToolMode.None, request.ToolChoice.Mode);
    }

    [Fact]
    public void AsCompletionsRequest_RequireAnyToolMode_SetsRequiredToolChoice()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = ChatToolMode.RequireAny, Tools = [DummyTool()] });

        Assert.NotNull(request.ToolChoice);
        Assert.True(request.ToolChoice.HasMode);
        Assert.Equal(Protocol.ToolMode.Required, request.ToolChoice.Mode);
    }

    [Fact]
    public void AsCompletionsRequest_RequireSpecificToolMode_SetsFunctionNameToolChoice()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions { ToolMode = ChatToolMode.RequireSpecific("get_weather"), Tools = [DummyTool()] });

        Assert.NotNull(request.ToolChoice);
        Assert.True(request.ToolChoice.HasFunctionName);
        Assert.Equal("get_weather", request.ToolChoice.FunctionName);
    }

    [Fact]
    public void AsCompletionsRequest_MapsSeedStopSequencesParallelToolsReasoningAndConversationId()
    {
        var request = CreateClient().AsCompletionsRequest([], new ChatOptions
        {
            Seed = 42,
            StopSequences = ["STOP", "END"],
            AllowMultipleToolCalls = false,
            ConversationId = "resp_123",
            Reasoning = new ReasoningOptions
            {
                Effort = Microsoft.Extensions.AI.ReasoningEffort.High,
            },
        });

        Assert.True(request.HasSeed);
        Assert.Equal(42, request.Seed);
        Assert.Equal(["STOP", "END"], request.Stop);
        Assert.True(request.HasParallelToolCalls);
        Assert.False(request.ParallelToolCalls);
        Assert.Equal("resp_123", request.PreviousResponseId);
        Assert.True(request.HasReasoningEffort);
        Assert.Equal(Protocol.ReasoningEffort.EffortHigh, request.ReasoningEffort);
    }

    [Fact]
    public void AsCompletionsRequest_StoreMessages_SetsStoreMessages()
    {
        var request = CreateClient().AsCompletionsRequest([], new GrokChatOptions
        {
            StoreMessages = true,
        });

        Assert.True(request.StoreMessages);
    }

    [Fact]
    public void AsTool_WithWebSearch_EnableImageSearch()
    {
        var tool = new GrokSearchTool { EnableImageSearch = true }.AsProtocolTool();

        Assert.NotNull(tool?.WebSearch);
        Assert.True(tool.WebSearch.EnableImageSearch);
    }

    [Fact]
    public void AsContents_WebSearchTool_MapsCallAndResult()
    {
        var toolCall = new ToolCall
        {
            Id = "ws_1",
            Type = ToolCallType.WebSearchTool,
            Function = new FunctionCall
            {
                Name = "web_search",
                Arguments = """{"query":"tesla stock"}""",
            },
        };

        var annotations = new List<AIAnnotation>
        {
            new CitationAnnotation
            {
                Title = "Tesla",
                Url = new Uri("https://finance.yahoo.com/quote/TSLA"),
            },
        };

        var contents = new[] { toolCall }.AsContents("search output", annotations).ToList();

        var call = Assert.IsType<WebSearchToolCallContent>(Assert.Single(contents.OfType<WebSearchToolCallContent>()));
        Assert.Equal("ws_1", call.CallId);
        Assert.Equal(["tesla stock"], call.Queries);
        Assert.Null(call.RawRepresentation);

        var result = Assert.IsType<WebSearchToolResultContent>(Assert.Single(contents.OfType<WebSearchToolResultContent>()));
        Assert.Equal("ws_1", result.CallId);
        Assert.Same(toolCall, result.RawRepresentation);
        Assert.NotNull(result.Outputs);
        Assert.Contains(result.Outputs!, x => x is UriContent uri && uri.Uri.Host == "finance.yahoo.com");
        Assert.Contains(result.Outputs!, x => x is TextContent text && text.Text == "search output");
    }

    [Fact]
    public void Convert_SamplingUsage_MapsExtendedTokenCounts()
    {
        var usage = new SamplingUsage
        {
            PromptTokens = 11,
            CompletionTokens = 7,
            TotalTokens = 18,
            ReasoningTokens = 5,
            CachedPromptTextTokens = 3,
            PromptTextTokens = 8,
            PromptImageTokens = 2,
            NumSourcesUsed = 1,
            CostInUsdTicks = 123,
        };

        var details = usage.Convert();

        Assert.NotNull(details);
        Assert.Equal(11, details.InputTokenCount);
        Assert.Equal(7, details.OutputTokenCount);
        Assert.Equal(18, details.TotalTokenCount);
        Assert.Equal(5, details.ReasoningTokenCount);
        Assert.Equal(3, details.CachedInputTokenCount);
        Assert.NotNull(details.AdditionalCounts);
        Assert.Equal(8, details.AdditionalCounts![nameof(SamplingUsage.PromptTextTokens)]);
        Assert.Equal(2, details.AdditionalCounts[nameof(SamplingUsage.PromptImageTokens)]);
        Assert.Equal(1, details.AdditionalCounts[nameof(SamplingUsage.NumSourcesUsed)]);
        Assert.Equal(123, details.AdditionalCounts[nameof(SamplingUsage.CostInUsdTicks)]);
    }

    [Fact]
    public void Convert_ReasoningEffort_MapsKnownValues()
    {
        Assert.Equal(Protocol.ReasoningEffort.EffortNone, Microsoft.Extensions.AI.ReasoningEffort.None.Convert());
        Assert.Equal(Protocol.ReasoningEffort.EffortLow, Microsoft.Extensions.AI.ReasoningEffort.Low.Convert());
        Assert.Equal(Protocol.ReasoningEffort.EffortMedium, Microsoft.Extensions.AI.ReasoningEffort.Medium.Convert());
        Assert.Equal(Protocol.ReasoningEffort.EffortHigh, Microsoft.Extensions.AI.ReasoningEffort.High.Convert());
        Assert.Equal(Protocol.ReasoningEffort.EffortHigh, Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh.Convert());
    }
}
