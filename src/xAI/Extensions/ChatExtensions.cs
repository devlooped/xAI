using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using xAI.Protocol;

namespace xAI;

/// <summary>Extensions for <see cref="ChatOptions"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class ChatOptionsExtensions
{
    extension(ChatOptions options)
    {
        /// <summary>Gets or sets the end user ID for the chat session.</summary>
        public string? EndUserId
        {
            get => (options.AdditionalProperties ??= []).TryGetValue("EndUserId", out var value) ? value as string : null;
            set => (options.AdditionalProperties ??= [])["EndUserId"] = value;
        }
    }
}

/// <summary>Grok-specific extensions for <see cref="HostedFileSearchTool"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class HostedFileSearchToolExtensions
{
    extension(HostedFileSearchTool tool)
    {
        /// <summary>
        /// User-defined instructions to be included in the search query. Defaults to generic search
        /// instructions used by the collections search backend if unset.
        /// </summary>
        public HostedFileSearchTool WithInstructions(string instructions) => new(new Dictionary<string, object?>
        {
            [nameof(CollectionsSearch.Instructions)] = Throw.IfNullOrEmpty(instructions)
        })
        {
            Inputs = tool.Inputs,
            MaximumResultCount = tool.MaximumResultCount,
        };
    }
}

static partial class AIToolExtensions
{
    extension(AITool tool)
    {
        public T? GetProperty<T>(string name) =>
            tool.AdditionalProperties?.TryGetValue(name, out var value) is true && value is T typed ? typed : default;
    }
}