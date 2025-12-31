using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Provides extension methods for <see cref="GrokClient"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrokClientExtensions
{
    /// <summary>Creates a new <see cref="IChatClient"/> from the specified <see cref="GrokClient"/> using the given model as the default.</summary>
    public static IChatClient AsIChatClient(this GrokClient client, string defaultModelId)
        => new GrokChatClient(client.Channel, client.Options, defaultModelId);
}
