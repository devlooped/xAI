using System;
using System.Collections.Generic;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using xAI.Protocol;

namespace xAI;

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
            AuthorizationToken = accessToken,
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
}
