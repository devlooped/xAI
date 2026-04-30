using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Grok-specific text to speech options that extend the base <see cref="TextToSpeechOptions"/>.</summary>
/// <remarks>
/// These options map to xAI's <c>/v1/tts</c> REST and WebSocket parameters.
/// If not specified, the API defaults to MP3 at 24 kHz / 128 kbps.
/// </remarks>
public class GrokTextToSpeechOptions : TextToSpeechOptions
{
    /// <summary>Initializes a new instance of the <see cref="GrokTextToSpeechOptions"/> class.</summary>
    public GrokTextToSpeechOptions()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GrokTextToSpeechOptions"/> class by cloning another instance.</summary>
    protected GrokTextToSpeechOptions(GrokTextToSpeechOptions? other)
        : base(other)
    {
        if (other is null)
            return;

        SampleRate = other.SampleRate;
        BitRate = other.BitRate;
        OptimizeStreamingLatency = other.OptimizeStreamingLatency;
        TextNormalization = other.TextNormalization;
    }

    /// <summary>Gets or sets the output sample rate in Hz.</summary>
    public int? SampleRate { get; set; }

    /// <summary>Gets or sets the MP3 bit rate in bits per second.</summary>
    public int? BitRate { get; set; }

    /// <summary>Gets or sets the xAI streaming latency optimization level.</summary>
    public int? OptimizeStreamingLatency { get; set; }

    /// <summary>Gets or sets a value indicating whether xAI should normalize written-form text before synthesis.</summary>
    public bool? TextNormalization { get; set; }

    /// <inheritdoc />
    public override TextToSpeechOptions Clone() => new GrokTextToSpeechOptions(this);
}
