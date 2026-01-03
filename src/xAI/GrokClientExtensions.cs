using System.ComponentModel;
using Microsoft.Extensions.AI;
using xAI.Protocol;

namespace xAI;

/// <summary>Provides extension methods for <see cref="GrokClient"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrokClientExtensions
{
    /// <summary>Creates a new <see cref="IChatClient"/> from the specified <see cref="GrokClient"/> using the given model as the default.</summary>
    public static IChatClient AsIChatClient(this GrokClient client, string defaultModelId)
        => new GrokChatClient(Throw.IfNull(client).Channel, client.Options, defaultModelId);

    /// <summary>Creates a new <see cref="IChatClient"/> from the specified <see cref="Chat.ChatClient"/> using the given model as the default.</summary>
    public static IChatClient AsIChatClient(this Chat.ChatClient client, string defaultModelId)
        => new GrokChatClient(Throw.IfNull(client), defaultModelId);

    /// <summary>Creates a new <see cref="IEmbeddingGenerator{String, Embedding}"/> from the specified <see cref="GrokClient"/> using the given model as the default.</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> AsIEmbeddingGenerator(this GrokClient client, string defaultModelId)
        => new GrokEmbeddingGenerator(Throw.IfNull(client).Channel, client.Options, defaultModelId);

    /// <summary>Creates a new <see cref="IEmbeddingGenerator{String, Embedding}"/> from the specified <see cref="Embedder.EmbedderClient"/> using the given model as the default.</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> AsIEmbeddingGenerator(this Embedder.EmbedderClient client, string defaultModelId)
        => new GrokEmbeddingGenerator(Throw.IfNull(client), defaultModelId);
}