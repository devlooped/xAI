using System.Text.Json;
using Google.Protobuf;
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

    internal GrokChatClient(ChannelBase channel, GrokClientOptions clientOptions, string defaultModelId)
        : this(new ChatClient(channel), clientOptions, defaultModelId)
    { }

    /// <summary>
    /// Test constructor.
    /// </summary>
    internal GrokChatClient(ChatClient client, string defaultModelId)
        : this(client, client.Options as GrokClientOptions ?? new(), defaultModelId)
    { }

    internal GrokChatClient(ChatClient client, GrokClientOptions clientOptions, string defaultModelId)
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

        var result = new ChatResponse()
        {
            ResponseId = response.Id,
            ModelId = response.Model,
            CreatedAt = response.Created?.ToDateTimeOffset(),
            FinishReason = lastOutput != null ? lastOutput.FinishReason.Convert() : null,
            Usage = response.Usage.Convert(),
        };

        var citations = response.Citations?.Distinct().Select(x => x.FromCitationUrl()).ToList<AIAnnotation>();

        ((List<ChatMessage>)result.Messages).AddRange(response.Outputs.AsChatMessages(citations));

        return result;
    }

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
                var update = new ChatResponseUpdate
                {
                    Role = output.Delta.Role.Convert(),
                    ResponseId = chunk.Id,
                    ModelId = chunk.Model,
                    CreatedAt = chunk.Created?.ToDateTimeOffset(),
                    RawRepresentation = chunk,
                    FinishReason = output.FinishReason != FinishReason.ReasonInvalid ? output.FinishReason.Convert() : null,
                };

                var citations = chunk.Citations?.Distinct().Select(MapCitation).ToList<AIAnnotation>();
                if (citations?.Count > 0)
                {
                    var content = update.Contents.LastOrDefault();
                    if (content == null)
                    {
                        content = new AIContent();
                        update.Contents.Add(content);
                    }
                    ((List<AIAnnotation>)(content.Annotations ??= [])).AddRange(citations);
                }

                ((List<AIContent>)update.Contents).AddRange(output.Delta.ToolCalls.AsContents(text, citations));

                // Only append text content if it's not already part of other tools' content
                if (!update.Contents.OfType<CodeInterpreterToolResultContent>().Any() &&
                    !update.Contents.OfType<McpServerToolResultContent>().Any() &&
                    text is not null)
                    update.Contents.Add(new TextContent(text));

                if (chunk.Usage.Convert() is { } usage)
                    update.Contents.Add(new UsageContent(usage) { RawRepresentation = chunk.Usage });

                yield return update;
            }
        }
    }

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
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "collection_id", collection }
            },
            FileId = file,
            ToolName = "collections_search",
            Url = new Uri($"collections://{collection}/files/{file}"),
        };
    }

    GetCompletionsRequest MapToRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var request = options?.RawRepresentationFactory?.Invoke(this) as GetCompletionsRequest ?? new GetCompletionsRequest()
        {
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

                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    gmsg.Content.Add(new Content { Text = textContent.Text });
                }
                else if (content is TextReasoningContent reasoning)
                {
                    gmsg.ReasoningContent = reasoning.Text;
                    gmsg.EncryptedContent = reasoning.ProtectedData;
                }
                else if (content is DataContent dataContent && dataContent.HasTopLevelMediaType("image"))
                {
                    gmsg.Content.Add(new Content { ImageUrl = new ImageUrlContent { ImageUrl = $"data:{dataContent.MediaType};base64,{Convert.ToBase64String(dataContent.Data.Span)}" } });
                }
                else if (content is UriContent uriContent && uriContent.HasTopLevelMediaType("image"))
                {
                    gmsg.Content.Add(new Content { ImageUrl = new ImageUrlContent { ImageUrl = uriContent.Uri.ToString() } });
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
