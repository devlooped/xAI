using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Grok-specific options for a real-time speech-to-speech session.</summary>
/// <remarks>
/// The inherited <see cref="RealtimeSessionOptions.Voice"/> accepts either a built-in voice
/// or a custom voice ID. Use <see cref="CustomVoiceId"/> as a more explicit alternative.
/// </remarks>
public sealed class GrokRealtimeOptions : RealtimeSessionOptions
{
    /// <summary>Gets or initializes an ephemeral client secret used to authenticate the WebSocket.</summary>
    /// <remarks>
    /// When omitted, the client uses the API key supplied to <see cref="GrokClient"/>.
    /// Obtain a short-lived secret with <see cref="GrokRealtimeClient.CreateEphemeralTokenAsync"/>.
    /// </remarks>
    public string? EphemeralToken { get; init; }

    /// <summary>Gets or initializes an xAI custom voice ID.</summary>
    /// <remarks>This is passed as the native xAI session <c>voice</c> setting.</remarks>
    public string? CustomVoiceId { get; init; }

    /// <summary>Gets or initializes xAI's session resumption setting.</summary>
    public bool? EnableResumption { get; init; }
}
