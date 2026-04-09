using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using xAI.Protocol;

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

                    if (grokSearch.Country is not null ||
                        grokSearch.Region is not null ||
                        grokSearch.City is not null ||
                        grokSearch.Timezone is not null)
                    {
                        websearch.UserLocation = new();
                        if (grokSearch.Country is not null)
                            websearch.UserLocation.Country = grokSearch.Country;
                        if (grokSearch.Region is not null)
                            websearch.UserLocation.Region = grokSearch.Region;
                        if (grokSearch.City is not null)
                            websearch.UserLocation.City = grokSearch.City;
                        if (grokSearch.Timezone is not null)
                            websearch.UserLocation.Timezone = grokSearch.Timezone;
                    }

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
                    return new Tool { WebSearch = new() };
                }

            case HostedCodeInterpreterTool:
                return new Tool { CodeExecution = new() };

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
                    ServerLabel = mcpTool.ServerName,
                    ServerUrl = mcpTool.ServerAddress,
                    AllowedToolNames = { mcpTool.AllowedTools ?? Array.Empty<string>() },
                };

                if (mcpTool.Headers?.TryGetValue("Authorization", out var authHeader) == true && !string.IsNullOrEmpty(authHeader))
                {
                    mcp.Authorization = authHeader;
                    foreach (var item in mcpTool.Headers.Where(x => x.Key != "Authorization"))
                        mcp.ExtraHeaders.Add(item.Key, item.Value);
                }
                else if (mcpTool.Headers != null)
                {
                    mcp.ExtraHeaders.Add(mcpTool.Headers);
                }

                // We can set an entire dictionary with a specific key
                // We keep this for backs compat, but makes little sense now with McpTool.Headers property available.
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
        foreach (var completion in messages)
        {
            ChatMessage message = new(ChatRole.Assistant, (string?)null)
            {
                RawRepresentation = completion
            };

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
            //else if (completion.Role == MessageRole.RoleTool && message.Contents.Count == 0 && content?.Length > 0)
            //{
            //    // For tool messages with no content, we can still create a message to hold annotations and completion
            //    message.Contents.Add(new TextContent(content)
            //    {
            //        Annotations = annotations,
            //        RawRepresentation = completion
            //    });
            //}

            yield return message;
        }
    }

    /// <summary>Converts messages and optional options to an xAI protocol completions request.</summary>
    internal static GetCompletionsRequest AsCompletionsRequest(this IGrokChatClient client, IEnumerable<ChatMessage> messages, ChatOptions? options = null)
    {
        var request = options?.RawRepresentationFactory?.Invoke(client) as GetCompletionsRequest ?? new GetCompletionsRequest()
        {
            Model = options?.ModelId ?? client.DefaultModelId,
        };

        if (string.IsNullOrEmpty(request.Model))
            request.Model = options?.ModelId ?? client.DefaultModelId;

        if ((options?.EndUserId ?? client.EndUserId) is { } user) request.User = user;
        if (options?.MaxOutputTokens is { } maxTokens) request.MaxTokens = maxTokens;
        if (options?.Temperature is { } temperature) request.Temperature = temperature;
        if (options?.TopP is { } topP) request.TopP = topP;
        if (options?.FrequencyPenalty is { } frequencyPenalty) request.FrequencyPenalty = frequencyPenalty;
        if (options?.PresencePenalty is { } presencePenalty) request.PresencePenalty = presencePenalty;
        if (options?.Instructions is { Length: > 0 } instructions)
        {
            request.Messages.Insert(0, new Message
            {
                Role = MessageRole.RoleSystem,
                Content = { new Content { Text = instructions } }
            });
        }

        request.ToolChoice = options?.ToolMode switch
        {
            null or AutoChatToolMode => new ToolChoice { Mode = ToolMode.Auto },
            NoneChatToolMode => new ToolChoice { Mode = ToolMode.None },
            RequiredChatToolMode { RequiredFunctionName: { } name } => new ToolChoice { FunctionName = name },
            RequiredChatToolMode => new ToolChoice { Mode = ToolMode.Required },
            _ => null
        };

        foreach (var message in messages)
        {
            if (message.RawRepresentation is Message input)
            {
                request.Messages.Add(input);
                continue;
            }
            else if (message.RawRepresentation is CompletionMessage completion)
            {
                request.Messages.Add(completion.AsMessage());
                continue;
            }

            var gmsg = new Message { Role = message.Role.Convert() };

            foreach (var content in message.Contents)
            {
                if (content.RawRepresentation is CompletionMessage completion)
                {
                    request.Messages.Add(completion.AsMessage());
                    continue;
                }
                if (content.RawRepresentation is Content protoContent)
                {
                    gmsg.Content.Add(protoContent);
                    continue;
                }

                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    gmsg.Content.Add(new Content { Text = textContent.Text });
                }
                else if (content is TextReasoningContent reasoning)
                {
                    gmsg.ReasoningContent = reasoning.Text;
                    gmsg.EncryptedContent = reasoning.ProtectedData;
                }
                else if (content is DataContent dataContent)
                {
                    gmsg.Content.Add(new Content
                    {
                        File = new FileContent
                        {
                            Data = Google.Protobuf.ByteString.CopyFrom(dataContent.Data.Span),
                            MimeType = dataContent.MediaType,
                            Filename = dataContent.Name ?? "",
                        }
                    });
                    //gmsg.Content.Add(new Content { ImageUrl = new ImageUrlContent { ImageUrl = $"data:{dataContent.MediaType};base64,{System.Convert.ToBase64String(dataContent.Data.Span)}" } });
                }
                else if (content is UriContent uriContent)
                {
                    if (uriContent.HasTopLevelMediaType("image"))
                    {
                        gmsg.Content.Add(new Content
                        {
                            ImageUrl = new ImageUrlContent { ImageUrl = uriContent.Uri.ToString() },
                        });
                    }
                    else
                    {
                        gmsg.Content.Add(new Content
                        {
                            File = new FileContent
                            {
                                Url = uriContent.Uri.ToString(),
                                MimeType = uriContent.MediaType
                            }
                        });
                    }
                }
                else if (content.RawRepresentation is ToolCall toolCall)
                {
                    gmsg.ToolCalls.Add(toolCall);
                }
                else if (content is FunctionCallContent functionCall)
                {
                    gmsg.ToolCalls.Add(new ToolCall
                    {
                        Id = functionCall.CallId,
                        Type = ToolCallType.ClientSideTool,
                        Function = new FunctionCall
                        {
                            Name = functionCall.Name,
                            Arguments = JsonSerializer.Serialize(functionCall.Arguments)
                        }
                    });
                }
                else if (content is FunctionResultContent resultContent)
                {
                    var msg = new Message
                    {
                        Role = MessageRole.RoleTool,
                        Content = { new Content { Text = JsonSerializer.Serialize(resultContent.Result) ?? "null" } }
                    };

                    if (resultContent.CallId is { Length: > 0 } callId)
                        msg.ToolCallId = callId;

                    request.Messages.Add(msg);
                }
                else if (content is McpServerToolResultContent mcpResult &&
                    mcpResult.RawRepresentation is ToolCall mcpToolCall &&
                    // TODO: what if there are multiple outputs?
                    mcpResult.Outputs is { Count: 1 } &&
                    mcpResult.Outputs[0] is TextContent mcpText)
                {
                    request.Messages.Add(new Message
                    {
                        Role = MessageRole.RoleTool,
                        ToolCalls = { mcpToolCall },
                        Content = { new Content { Text = mcpText.Text } }
                    });
                }
                else if (content is CodeInterpreterToolResultContent codeResult &&
                    codeResult.RawRepresentation is ToolCall codeToolCall &&
                    // TODO: what if there are multiple outputs?
                    codeResult.Outputs is { Count: 1 } &&
                    codeResult.Outputs[0] is TextContent codeText)
                {
                    request.Messages.Add(new Message
                    {
                        Role = MessageRole.RoleTool,
                        ToolCalls = { codeToolCall },
                        Content = { new Content { Text = codeText.Text } }
                    });
                }
            }

            if (gmsg.Content.Count == 0 && gmsg.ToolCalls.Count == 0)
                continue;

            request.Messages.Add(gmsg);
        }

        if (options is GrokChatOptions grokOptions)
        {
            request.Include.AddRange(grokOptions.Include);

            if (grokOptions.Search.HasFlag(GrokSearch.X))
            {
                (options.Tools ??= []).Insert(0, new GrokXSearchTool());
            }
            else if (grokOptions.Search.HasFlag(GrokSearch.Web))
            {
                (options.Tools ??= []).Insert(0, new GrokSearchTool());
            }

            request.UseEncryptedContent = grokOptions.UseEncryptedContent;
        }

        if (options?.Tools is not null)
        {
            foreach (var tool in options.Tools.Select(x => x.AsProtocolTool(options)))
                if (tool is not null) request.Tools.Add(tool);
        }

        if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
        {
            request.ResponseFormat = new ResponseFormat { FormatType = FormatType.JsonObject };
            if (jsonFormat.Schema != null)
            {
                request.ResponseFormat.FormatType = FormatType.JsonSchema;
                request.ResponseFormat.Schema = jsonFormat.Schema?.ToString();
            }
        }

        return request;
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
                            Outputs = [new TextContent(content)]
                        };
                    break;

                case ToolCallType.CodeExecutionTool:
                    yield return new CodeInterpreterToolCallContent(toolCall.Id)
                    {
                        Annotations = annotations,
                        RawRepresentation = toolCall,
                    };
                    if (content is not null)
                        yield return new CodeInterpreterToolResultContent(toolCall.Id)
                        {
                            Annotations = annotations,
                            RawRepresentation = toolCall,
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
                        else
                        {
                            // If we can't parse the references, still return as single raw content 
                            // for further inspection by consumers.
                            yield return new CollectionSearchToolResultContent
                            {
                                Annotations = annotations,
                                RawRepresentation = toolCall,
                                CallId = toolCall.Id,
                                Outputs = [new TextContent(content)]
                            };
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

    internal static Message AsMessage(this CompletionMessage completion)
    {
        var message = new Message
        {
            Role = completion.Role,
            EncryptedContent = completion.EncryptedContent,
            ReasoningContent = completion.ReasoningContent,
        };

        if (!string.IsNullOrEmpty(completion.Content))
            message.Content.Add(new Content { Text = completion.Content });

        message.ToolCalls.AddRange(completion.ToolCalls);

        return message;
    }

    internal static MessageRole Convert(this ChatRole role) => role switch
    {
        _ when role == ChatRole.System => MessageRole.RoleSystem,
        _ when role == ChatRole.User => MessageRole.RoleUser,
        _ when role == ChatRole.Assistant => MessageRole.RoleAssistant,
        _ when role == ChatRole.Tool => MessageRole.RoleTool,
        _ => MessageRole.RoleUser
    };

    internal static ChatRole Convert(this MessageRole role) => role switch
    {
        MessageRole.RoleSystem => ChatRole.System,
        MessageRole.RoleUser => ChatRole.User,
        MessageRole.RoleAssistant => ChatRole.Assistant,
        MessageRole.RoleTool => ChatRole.Tool,
        _ => ChatRole.Assistant
    };

    internal static ChatFinishReason? Convert(this FinishReason finishReason) => finishReason switch
    {
        FinishReason.ReasonStop => ChatFinishReason.Stop,
        FinishReason.ReasonMaxLen => ChatFinishReason.Length,
        FinishReason.ReasonToolCalls => ChatFinishReason.ToolCalls,
        FinishReason.ReasonMaxContext => ChatFinishReason.Length,
        FinishReason.ReasonTimeLimit => ChatFinishReason.Length,
        _ => null
    };

    internal static UsageDetails? Convert(this SamplingUsage usage) => usage == null ? null : new()
    {
        InputTokenCount = usage.PromptTokens,
        OutputTokenCount = usage.CompletionTokens,
        TotalTokenCount = usage.TotalTokens
    };

    internal static CitationAnnotation FromCitationUrl(this string citationUrl)
    {
        var url = new Uri(citationUrl);
        if (url.Scheme != "collections")
            return new CitationAnnotation { Url = url };

        // Special-case collection citations so we get better metadata
        var collection = url.Host;
        var file = url.AbsolutePath[7..];

        return new CitationAnnotation
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "collection_id", collection }
            },
            FileId = file,
            ToolName = "collections_search",
            Url = new Uri($"collections://{collection}/files/{file}"),
        };
    }

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
