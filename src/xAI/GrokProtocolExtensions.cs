using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static Google.Protobuf.Reflection.GeneratedCodeInfo.Types;

namespace xAI;

/// <summary>Provides extension methods for working with xAI protocol types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class GrokProtocolExtensions
{
    /// <summary>Creates an xAI protocol <see cref="Tool"/> from an <see cref="AITool"/>.</summary>
    /// <param name="tool">The tool to convert.</param>
    /// <returns>An xAI protocol <see cref="Tool"/> representing <paramref name="tool"/> or <see langword="null"/> if there is no mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public static Tool? AsProtocolTool(this AITool tool, ChatOptions? options = null) => ToProtocolTool(Throw.IfNull(tool), options);

    /// <summary>Creates a sequence of <see cref="ChatMessage"/> instances from the specified protocol outputs.</summary>
    /// <param name="outputs">The output messages to convert.</param>
    /// <returns>A sequence of <see cref="ChatMessage"/> instances.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is <see langword="null"/>.</exception>
    public static IEnumerable<ChatMessage> AsChatMessages(this IEnumerable<CompletionOutput> outputs, List<AIAnnotation>? citations = default) => ToChatMessages(Throw.IfNull(outputs).Select(x => x.Message), citations);

    /// <summary>Creates a sequence of <see cref="ChatMessage"/> instances from the specified protocol messages.</summary>
    /// <param name="messages">The messages to convert.</param>
    /// <returns>A sequence of <see cref="ChatMessage"/> instances.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    public static IEnumerable<ChatMessage> AsChatMessages(this IEnumerable<CompletionMessage> messages, List<AIAnnotation>? citations = default) => ToChatMessages(Throw.IfNull(messages), citations);

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

    static IEnumerable<ChatMessage> ToChatMessages(IEnumerable<CompletionMessage> messages, List<AIAnnotation>? citations = default)
    {
        ChatMessage? message = null;

        foreach (var completion in messages)
        {
            message ??= new(ChatRole.Assistant, (string?)null);
            var annotations = citations;
            if (completion.Citations.Count > 0)
            {
                annotations ??= [];
                foreach (var citation in completion.Citations)
                    annotations.AddRange(AsCitations(citation));
            }

            var content = string.IsNullOrEmpty(completion.Content) ? null : completion.Content;

            ((List<AIContent>)message.Contents).AddRange(AsContents(completion.ToolCalls, content, annotations));

            if (!string.IsNullOrEmpty(completion.ReasoningContent))
            {
                message.Contents.Add(new TextReasoningContent(completion.ReasoningContent)
                {
                    Annotations = annotations,
                    RawRepresentation = completion,
                    ProtectedData = completion.EncryptedContent,
                });
            }
            else if (!string.IsNullOrEmpty(completion.EncryptedContent))
            {
                message.Contents.Add(new TextReasoningContent(null)
                {
                    Annotations = annotations,
                    ProtectedData = completion.EncryptedContent,
                    RawRepresentation = completion
                });
            }

            if (completion.Role != MessageRole.RoleTool && completion.Content is { Length: > 0 } text)
            {
                message.Contents.Add(new TextContent(text)
                {
                    Annotations = annotations,
                    RawRepresentation = completion
                });
            }
        }

        if (message is not null)
            yield return message;
    }

    internal static IEnumerable<AIContent> AsContents(this IEnumerable<ToolCall> toolCalls, string? content = default, List<AIAnnotation>? annotations = default)
    {
        foreach (var toolCall in toolCalls)
        {
            switch (toolCall.Type)
            {
                case ToolCallType.ClientSideTool:
                    yield return new FunctionCallContent(
                        toolCall.Id,
                        toolCall.Function.Name,
                        !string.IsNullOrEmpty(toolCall.Function.Arguments)
                            ? JsonSerializer.Deserialize<IDictionary<string, object?>>(toolCall.Function.Arguments)
                            : null)
                    {
                        Annotations = annotations,
                        RawRepresentation = toolCall,
                    };
                    break;

                case ToolCallType.McpTool:
                    yield return new McpServerToolCallContent(toolCall.Id, toolCall.Function.Name, null)
                    {
                        Annotations = annotations,
                        RawRepresentation = toolCall
                    };
                    if (content is not null)
                        yield return new McpServerToolResultContent(toolCall.Id)
                        {
                            Annotations = annotations,
                            RawRepresentation = toolCall,
                            Output = [new TextContent(content)]
                        };
                    break;

                case ToolCallType.CodeExecutionTool:
                    yield return new CodeInterpreterToolCallContent()
                    {
                        Annotations = annotations,
                        RawRepresentation = toolCall,
                        CallId = toolCall.Id,
                    };
                    if (content is not null)
                        yield return new CodeInterpreterToolResultContent()
                        {
                            Annotations = annotations,
                            RawRepresentation = toolCall,
                            CallId = toolCall.Id,
                            Outputs = [new TextContent(content)]
                        };
                    break;

                case ToolCallType.CollectionsSearchTool:
                    if (content is not null)
                    {
                        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(content));
                        if (JsonDocument.TryParseValue(ref reader, out var doc) &&
                            doc.RootElement.TryGetProperty("search_matches", out var matchesElement) &&
                            matchesElement.ValueKind == JsonValueKind.Array &&
                            JsonSerializer.Deserialize<CollectionSearchItem[]>(matchesElement, CollectionSearchJsonContext.Default.Options) is { Length: > 0 } matches)
                        {
                            var result = new CollectionSearchToolResultContent
                            {
                                Annotations = annotations,
                                RawRepresentation = toolCall,
                                CallId = toolCall.Id,
                            };
                            var outputs = new List<AIContent>();
                            foreach (var file in matches.GroupBy(x => x.FileId))
                            {
                                var fileCitations = file.SelectMany(AsCitations).ToArray();
                                outputs.Add(new HostedFileContent(file.Key)
                                {
                                    Annotations = fileCitations,
                                    RawRepresentation = toolCall,
                                    Name = fileCitations.Select(x => x.Title).Where(x => x != null).FirstOrDefault(),
                                });
                            }
                            result.Outputs = outputs;
                            yield return result;
                        }
                    }
                    else
                    {
                        yield return new CollectionSearchToolCallContent
                        {
                            Annotations = annotations,
                            RawRepresentation = toolCall,
                            CallId = toolCall.Id,
                        };
                    }
                    break;

                default:
                    yield return new() { Annotations = annotations, RawRepresentation = toolCall };
                    break;
            }
        }
    }

    static IEnumerable<CitationAnnotation> AsCitations(CollectionSearchItem item)
    {
        var newline = item.ChunkContent.IndexOf('\n');
        var title = newline >= 0 ? item.ChunkContent[..newline] : null;
        foreach (var collectionId in item.CollectionIds)
        {
            yield return new CitationAnnotation
            {
                Title = title,
                FileId = item.FileId,
                Snippet = newline >= 0 ? item.ChunkContent[++newline..] : item.ChunkContent,
                ToolName = "collections_search",
                Url = new Uri($"collections://{collectionId}/files/{item.FileId}"),
                RawRepresentation = item,
            };
        }
    }

    static IEnumerable<CitationAnnotation> AsCitations(InlineCitation citation) => citation.CitationCase switch
    {
        InlineCitation.CitationOneofCase.WebCitation => [new CitationAnnotation { Url = new(citation.WebCitation.Url), RawRepresentation = citation }],
        InlineCitation.CitationOneofCase.XCitation => [new CitationAnnotation { Url = new(citation.XCitation.Url), RawRepresentation = citation }],
        InlineCitation.CitationOneofCase.CollectionsCitation => AsCitations(new CollectionSearchItem(
            citation.CollectionsCitation.FileId, citation.CollectionsCitation.ChunkId, citation.CollectionsCitation.ChunkContent, citation.CollectionsCitation.Score, [.. citation.CollectionsCitation.CollectionIds])),
        _ => [new CitationAnnotation { RawRepresentation = citation }]
    };

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
        UseStringEnumConverter = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
        WriteIndented = true
        )]
    [JsonSerializable(typeof(CollectionSearchItem[]))]
    [JsonSerializable(typeof(CollectionSearchItem))]
    partial class CollectionSearchJsonContext : JsonSerializerContext { }

    record CollectionSearchItem(string FileId, string ChunkId, string ChunkContent, float Score, string[] CollectionIds);
}
