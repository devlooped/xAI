using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static xAI.Protocol.Chat;

namespace xAI;

class GrokChatClient : IChatClient
{
    readonly ChatClientMetadata metadata;
    readonly ChatClient client;
    readonly string defaultModelId;
    readonly GrokClientOptions clientOptions;

    internal GrokChatClient(GrpcChannel channel, GrokClientOptions clientOptions, string defaultModelId)
        : this(new ChatClient(channel), clientOptions, defaultModelId)
    { }

    /// <summary>
    /// Test constructor.
    /// </summary>
    internal GrokChatClient(ChatClient client, string defaultModelId)
        : this(client, new(), defaultModelId)
    { }

    GrokChatClient(ChatClient client, GrokClientOptions clientOptions, string defaultModelId)
    {
        this.client = client;
        this.clientOptions = clientOptions;
        this.defaultModelId = defaultModelId;
        metadata = new ChatClientMetadata("xai", clientOptions.Endpoint, defaultModelId);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = MapToRequest(messages, options);
        var response = await client.GetCompletionAsync(request, cancellationToken: cancellationToken);
        var lastOutput = response.Outputs.OrderByDescending(x => x.Index).FirstOrDefault();

        if (lastOutput == null)
        {
            return new ChatResponse()
            {
                ResponseId = response.Id,
                ModelId = response.Model,
                CreatedAt = response.Created.ToDateTimeOffset(),
                Usage = MapToUsage(response.Usage),
            };
        }

        var message = new ChatMessage(MapRole(lastOutput.Message.Role), default(string));
        var citations = response.Citations?.Distinct().Select(MapCitation).ToList<AIAnnotation>();

        foreach (var output in response.Outputs.OrderBy(x => x.Index))
        {
            if (output.Message.Content is { Length: > 0 } text)
            {
                // Special-case output from tools
                if (output.Message.Role == MessageRole.RoleTool &&
                    output.Message.ToolCalls.Count == 1 &&
                        output.Message.ToolCalls[0] is { } toolCall)
                {
                    if (toolCall.Type == ToolCallType.McpTool)
                    {
                        message.Contents.Add(new McpServerToolCallContent(toolCall.Id, toolCall.Function.Name, null)
                        {
                            RawRepresentation = toolCall
                        });
                        message.Contents.Add(new McpServerToolResultContent(toolCall.Id)
                        {
                            RawRepresentation = toolCall,
                            Output = [new TextContent(text)]
                        });
                        continue;
                    }
                    else if (toolCall.Type == ToolCallType.CodeExecutionTool)
                    {
                        message.Contents.Add(new CodeInterpreterToolCallContent()
                        {
                            CallId = toolCall.Id,
                            RawRepresentation = toolCall
                        });
                        message.Contents.Add(new CodeInterpreterToolResultContent()
                        {
                            CallId = toolCall.Id,
                            RawRepresentation = toolCall,
                            Outputs = [new TextContent(text)]
                        });
                        continue;
                    }
                }

                var content = new TextContent(text) { Annotations = citations };

                foreach (var citation in output.Message.Citations)
                    (content.Annotations ??= []).Add(MapInlineCitation(citation));

                message.Contents.Add(content);
            }

            foreach (var toolCall in output.Message.ToolCalls)
                message.Contents.Add(MapToolCall(toolCall));
        }

        return new ChatResponse(message)
        {
            ResponseId = response.Id,
            ModelId = response.Model,
            CreatedAt = response.Created?.ToDateTimeOffset(),
            FinishReason = lastOutput != null ? MapFinishReason(lastOutput.FinishReason) : null,
            Usage = MapToUsage(response.Usage),
        };
    }

    AIContent MapToolCall(ToolCall toolCall) => toolCall.Type switch
    {
        ToolCallType.ClientSideTool => new FunctionCallContent(
            toolCall.Id,
            toolCall.Function.Name,
            !string.IsNullOrEmpty(toolCall.Function.Arguments)
                ? JsonSerializer.Deserialize<IDictionary<string, object?>>(toolCall.Function.Arguments)
                : null)
        {
            RawRepresentation = toolCall
        },
        ToolCallType.McpTool => new McpServerToolCallContent(toolCall.Id, toolCall.Function.Name, null)
        {
            RawRepresentation = toolCall
        },
        ToolCallType.CodeExecutionTool => new CodeInterpreterToolCallContent()
        {
            CallId = toolCall.Id,
            RawRepresentation = toolCall
        },
        _ => new HostedToolCallContent()
        {
            CallId = toolCall.Id,
            RawRepresentation = toolCall
        }
    };

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return CompleteChatStreamingCore(messages, options, cancellationToken);

        async IAsyncEnumerable<ChatResponseUpdate> CompleteChatStreamingCore(IEnumerable<ChatMessage> messages, ChatOptions? options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var request = MapToRequest(messages, options);
            var call = client.GetCompletionChunk(request, cancellationToken: cancellationToken);

            await foreach (var chunk in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                var output = chunk.Outputs[0];
                var text = output.Delta.Content is { Length: > 0 } delta ? delta : null;

                // Use positional arguments for ChatResponseUpdate
                var update = new ChatResponseUpdate(MapRole(output.Delta.Role), text)
                {
                    ResponseId = chunk.Id,
                    ModelId = chunk.Model,
                    CreatedAt = chunk.Created?.ToDateTimeOffset(),
                    FinishReason = output.FinishReason != FinishReason.ReasonInvalid ? MapFinishReason(output.FinishReason) : null,
                };

                if (chunk.Citations is { Count: > 0 } citations)
                {
                    var textContent = update.Contents.OfType<TextContent>().FirstOrDefault();
                    if (textContent == null)
                    {
                        textContent = new TextContent(string.Empty);
                        update.Contents.Add(textContent);
                    }

                    foreach (var citation in citations.Distinct())
                        (textContent.Annotations ??= []).Add(MapCitation(citation));
                }

                foreach (var toolCall in output.Delta.ToolCalls)
                    update.Contents.Add(MapToolCall(toolCall));

                if (update.Contents.Any())
                    yield return update;
            }
        }
    }

    static CitationAnnotation MapInlineCitation(InlineCitation citation) => citation.CitationCase switch
    {
        InlineCitation.CitationOneofCase.WebCitation => new CitationAnnotation { Url = new(citation.WebCitation.Url) },
        InlineCitation.CitationOneofCase.XCitation => new CitationAnnotation { Url = new(citation.XCitation.Url) },
        InlineCitation.CitationOneofCase.CollectionsCitation => new CitationAnnotation
        {
            FileId = citation.CollectionsCitation.FileId,
            Snippet = citation.CollectionsCitation.ChunkContent,
            ToolName = "file_search",
        },
        _ => new CitationAnnotation()
    };

    static CitationAnnotation MapCitation(string citation)
    {
        var url = new Uri(citation);
        if (url.Scheme != "collections")
            return new CitationAnnotation { Url = url };

        // Special-case collection citations so we get better metadata
        var collection = url.Host;
        var file = url.AbsolutePath[7..];
        return new CitationAnnotation
        {
            ToolName = "collections_search",
            FileId = file,
            AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    { "collection_id", collection }
                }
        };
    }

    GetCompletionsRequest MapToRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var request = options?.RawRepresentationFactory?.Invoke(this) as GetCompletionsRequest ?? new GetCompletionsRequest()
        {
            // By default always include citations in the final output if available
            Include = { IncludeOption.InlineCitations },
            Model = options?.ModelId ?? defaultModelId,
        };

        if (string.IsNullOrEmpty(request.Model))
            request.Model = options?.ModelId ?? defaultModelId;

        if ((options?.EndUserId ?? clientOptions.EndUserId) is { } user) request.User = user;
        if (options?.MaxOutputTokens is { } maxTokens) request.MaxTokens = maxTokens;
        if (options?.Temperature is { } temperature) request.Temperature = temperature;
        if (options?.TopP is { } topP) request.TopP = topP;
        if (options?.FrequencyPenalty is { } frequencyPenalty) request.FrequencyPenalty = frequencyPenalty;
        if (options?.PresencePenalty is { } presencePenalty) request.PresencePenalty = presencePenalty;

        foreach (var message in messages)
        {
            var gmsg = new Message { Role = MapRole(message.Role) };

            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    gmsg.Content.Add(new Content { Text = textContent.Text });
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
                    request.Messages.Add(new Message
                    {
                        Role = MessageRole.RoleTool,
                        Content = { new Content { Text = JsonSerializer.Serialize(resultContent.Result) ?? "null" } }
                    });
                }
                else if (content is McpServerToolResultContent mcpResult &&
                    mcpResult.RawRepresentation is ToolCall mcpToolCall &&
                    mcpResult.Output is { Count: 1 } &&
                    mcpResult.Output[0] is TextContent mcpText)
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

            // If we have only tool calls and no content, the gRPC enpoint fails, so add an empty one.
            if (gmsg.Content.Count == 0)
                gmsg.Content.Add(new Content());

            request.Messages.Add(gmsg);
        }

        IList<IncludeOption> includes = [IncludeOption.InlineCitations];
        if (options is GrokChatOptions grokOptions)
        {
            // NOTE: overrides our default include for inline citations, potentially.
            request.Include.Clear();
            request.Include.AddRange(grokOptions.Include);

            if (grokOptions.Search.HasFlag(GrokSearch.X))
            {
                (options.Tools ??= []).Insert(0, new GrokXSearchTool());
            }
            else if (grokOptions.Search.HasFlag(GrokSearch.Web))
            {
                (options.Tools ??= []).Insert(0, new GrokSearchTool());
            }
        }

        if (options?.Tools is not null)
        {
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction functionTool)
                {
                    var function = new Function
                    {
                        Name = functionTool.Name,
                        Description = functionTool.Description,
                        Parameters = JsonSerializer.Serialize(functionTool.JsonSchema)
                    };
                    request.Tools.Add(new Tool { Function = function });
                }
                else if (tool is HostedWebSearchTool webSearchTool)
                {
                    if (webSearchTool is GrokXSearchTool xSearch)
                    {
                        var toolProto = new XSearch
                        {
                            EnableImageUnderstanding = xSearch.EnableImageUnderstanding,
                            EnableVideoUnderstanding = xSearch.EnableVideoUnderstanding,
                        };

                        if (xSearch.AllowedHandles is { } allowed) toolProto.AllowedXHandles.AddRange(allowed);
                        if (xSearch.ExcludedHandles is { } excluded) toolProto.ExcludedXHandles.AddRange(excluded);
                        if (xSearch.FromDate is { } from) toolProto.FromDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                        if (xSearch.ToDate is { } to) toolProto.ToDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

                        request.Tools.Add(new Tool { XSearch = toolProto });
                    }
                    else if (webSearchTool is GrokSearchTool grokSearch)
                    {
                        var toolProto = new WebSearch
                        {
                            EnableImageUnderstanding = grokSearch.EnableImageUnderstanding,
                        };

                        if (grokSearch.AllowedDomains is { } allowed) toolProto.AllowedDomains.AddRange(allowed);
                        if (grokSearch.ExcludedDomains is { } excluded) toolProto.ExcludedDomains.AddRange(excluded);

                        request.Tools.Add(new Tool { WebSearch = toolProto });
                    }
                    else
                    {
                        request.Tools.Add(new Tool { WebSearch = new WebSearch() });
                    }
                }
                else if (tool is HostedCodeInterpreterTool)
                {
                    request.Tools.Add(new Tool { CodeExecution = new CodeExecution { } });
                }
                else if (tool is HostedFileSearchTool fileSearch)
                {
                    var toolProto = new CollectionsSearch();

                    if (fileSearch.Inputs?.OfType<HostedVectorStoreContent>() is { } vectorStores)
                        toolProto.CollectionIds.AddRange(vectorStores.Select(x => x.VectorStoreId).Distinct());

                    if (fileSearch.MaximumResultCount is { } maxResults)
                        toolProto.Limit = maxResults;

                    request.Tools.Add(new Tool { CollectionsSearch = toolProto });
                }
                else if (tool is HostedMcpServerTool mcpTool)
                {
                    request.Tools.Add(new Tool
                    {
                        Mcp = new MCP
                        {
                            Authorization = mcpTool.AuthorizationToken,
                            ServerLabel = mcpTool.ServerName,
                            ServerUrl = mcpTool.ServerAddress,
                            AllowedToolNames = { mcpTool.AllowedTools ?? Array.Empty<string>() }
                        }
                    });
                }
            }
        }

        if (options?.ResponseFormat is ChatResponseFormatJson)
        {
            request.ResponseFormat = new ResponseFormat
            {
                FormatType = FormatType.JsonObject
            };
        }

        return request;
    }

    static MessageRole MapRole(ChatRole role) => role switch
    {
        _ when role == ChatRole.System => MessageRole.RoleSystem,
        _ when role == ChatRole.User => MessageRole.RoleUser,
        _ when role == ChatRole.Assistant => MessageRole.RoleAssistant,
        _ when role == ChatRole.Tool => MessageRole.RoleTool,
        _ => MessageRole.RoleUser
    };

    static ChatRole MapRole(MessageRole role) => role switch
    {
        MessageRole.RoleSystem => ChatRole.System,
        MessageRole.RoleUser => ChatRole.User,
        MessageRole.RoleAssistant => ChatRole.Assistant,
        MessageRole.RoleTool => ChatRole.Tool,
        _ => ChatRole.Assistant
    };

    static ChatFinishReason? MapFinishReason(FinishReason finishReason) => finishReason switch
    {
        FinishReason.ReasonStop => ChatFinishReason.Stop,
        FinishReason.ReasonMaxLen => ChatFinishReason.Length,
        FinishReason.ReasonToolCalls => ChatFinishReason.ToolCalls,
        FinishReason.ReasonMaxContext => ChatFinishReason.Length,
        FinishReason.ReasonTimeLimit => ChatFinishReason.Length,
        _ => null
    };

    static UsageDetails? MapToUsage(SamplingUsage usage) => usage == null ? null : new()
    {
        InputTokenCount = usage.PromptTokens,
        OutputTokenCount = usage.CompletionTokens,
        TotalTokenCount = usage.TotalTokens
    };

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType switch
    {
        Type t when t == typeof(ChatClientMetadata) => metadata,
        Type t when t == typeof(GrokChatClient) => this,
        _ => null
    };

    /// <inheritdoc />
    public void Dispose() { }
}
