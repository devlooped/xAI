using Grpc.Core;
using Microsoft.Extensions.AI;
using xAI.Protocol;
using static xAI.Protocol.Chat;

namespace xAI;

interface IGrokChatClient : IChatClient
{
    string DefaultModelId { get; }
    string? EndUserId { get; }
}

class GrokChatClient : IGrokChatClient
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

    public string DefaultModelId => defaultModelId;
    public string? EndUserId => clientOptions.EndUserId;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = this.AsCompletionsRequest(messages, options);
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
            var request = this.AsCompletionsRequest(messages, options);
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
