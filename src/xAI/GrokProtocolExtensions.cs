using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using xAI.Protocol;

namespace xAI;

/// <summary>Provides extension methods for working with xAI protocol types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrokProtocolExtensions
{
    /// <summary>Creates an xAI protocol <see cref="Tool"/> from an <see cref="AITool"/>.</summary>
    /// <param name="tool">The tool to convert.</param>
    /// <returns>An xAI protocol <see cref="Tool"/> representing <paramref name="tool"/> or <see langword="null"/> if there is no mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public static Tool? AsProtocolTool(this AITool tool, ChatOptions? options = null) => ToProtocolTool(Throw.IfNull(tool), options);

    static Tool? ToProtocolTool(AITool tool, ChatOptions? options = null)
    {
        switch (tool)
        {
            case AIFunction functionTool:
                return new Tool
                {
                    Function = new Function
                    {
                        Name = functionTool.Name,
                        Description = functionTool.Description,
                        Parameters = JsonSerializer.Serialize(functionTool.JsonSchema)
                    }
                };

            case HostedWebSearchTool webSearchTool:
                if (webSearchTool is GrokXSearchTool xSearchTool)
                {
                    var xsearch = new XSearch
                    {
                        EnableImageUnderstanding = xSearchTool.EnableImageUnderstanding,
                        EnableVideoUnderstanding = xSearchTool.EnableVideoUnderstanding,
                    };

                    if (xSearchTool.AllowedHandles is { Count: > 0 } &&
                        xSearchTool.ExcludedHandles is { Count: > 0 })
                        throw new NotSupportedException($"Cannot use {nameof(GrokXSearchTool.AllowedHandles)} and {nameof(GrokXSearchTool.ExcludedHandles)} together in the same request.");

                    if (xSearchTool.AllowedHandles is { } allowed)
                        xsearch.AllowedXHandles.AddRange(allowed);
                    if (xSearchTool.ExcludedHandles is { } excluded)
                        xsearch.ExcludedXHandles.AddRange(excluded);
                    if (xSearchTool.FromDate is { } from)
                        xsearch.FromDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                    if (xSearchTool.ToDate is { } to)
                        xsearch.ToDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

                    return new Tool { XSearch = xsearch };
                }
                else if (webSearchTool is GrokSearchTool grokSearch)
                {
                    var websearch = new WebSearch
                    {
                        EnableImageUnderstanding = grokSearch.EnableImageUnderstanding,
                    };

                    if (grokSearch.AllowedDomains is { Count: > 0 } &&
                        grokSearch.ExcludedDomains is { Count: > 0 })
                        throw new NotSupportedException($"Cannot use {nameof(GrokSearchTool.AllowedDomains)} and {nameof(GrokSearchTool.ExcludedDomains)} together in the same request.");

                    if (grokSearch.AllowedDomains is { } allowed)
                        websearch.AllowedDomains.AddRange(allowed);
                    if (grokSearch.ExcludedDomains is { } excluded)
                        websearch.ExcludedDomains.AddRange(excluded);

                    return new Tool { WebSearch = websearch };
                }
                else
                {
                    return new Tool { WebSearch = new WebSearch() };
                }

            case HostedCodeInterpreterTool:
                return new Tool { CodeExecution = new CodeExecution { } };

            case HostedFileSearchTool fileSearch:
                var collectionTool = new CollectionsSearch();

                if (fileSearch.Inputs?.OfType<HostedVectorStoreContent>() is { } vectorStores)
                    collectionTool.CollectionIds.AddRange(vectorStores.Select(x => x.VectorStoreId).Distinct());

                if (fileSearch.MaximumResultCount is { } maxResults)
                    collectionTool.Limit = maxResults;
                if (fileSearch.GetProperty<string>(nameof(CollectionsSearch.Instructions)) is { } instructions)
                    collectionTool.Instructions = instructions;

                return new Tool { CollectionsSearch = collectionTool };

            case HostedMcpServerTool mcpTool:
                var mcp = new MCP
                {
                    Authorization = mcpTool.AuthorizationToken,
                    ServerLabel = mcpTool.ServerName,
                    ServerUrl = mcpTool.ServerAddress,
                    AllowedToolNames = { mcpTool.AllowedTools ?? Array.Empty<string>() },
                };

                // We can set an entire dictionary with a specific key
                if (mcpTool.GetProperty<IDictionary<string, string>>(nameof(MCP.ExtraHeaders)) is { } headers)
                    mcp.ExtraHeaders.Add(headers);

                // Or also the more intuitive mapping of additional properties directly.
                foreach (var kv in mcpTool.AdditionalProperties)
                    if (kv.Value is string value)
                        mcp.ExtraHeaders.Add(kv.Key, value);

                return new Tool { Mcp = mcp };

            default:
                return null;
        }
    }
}
