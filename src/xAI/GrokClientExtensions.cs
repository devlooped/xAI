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
        => new GrokChatClient(client.ChannelHandler.Channel, client.Options, defaultModelId);

    /// <summary>Creates a new <see cref="IChatClient"/> from the specified <see cref="Chat.ChatClient"/> using the given model as the default.</summary>
    public static IChatClient AsIChatClient(this Chat.ChatClient client, string defaultModelId)
        => new GrokChatClient(client, defaultModelId);

    /// <summary>Creates a new <see cref="IImageGenerator"/> from the specified <see cref="GrokClient"/> using the given model as the default.</summary>
    public static IImageGenerator AsIImageGenerator(this GrokClient client, string defaultModelId)
        => new GrokImageGenerator(client.ChannelHandler.Channel, client.Options, defaultModelId);

    /// <summary>Creates a new <see cref="IImageGenerator"/> from the specified <see cref="Image.ImageClient"/> using the given model as the default.</summary>
    public static IImageGenerator AsIImageGenerator(this Image.ImageClient client, string defaultModelId)
        => new GrokImageGenerator(client, defaultModelId);

    /// <summary>Creates a new <see cref="ITextToSpeechClient"/> from the specified <see cref="GrokClient"/>.</summary>
    public static ITextToSpeechClient AsITextToSpeechClient(this GrokClient client)
        => new GrokTextToSpeechClient(client.ChannelHandler.Handler, client.Options, client.ApiKey);
}
