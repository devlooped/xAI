using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Grok-specific speech to text options that extend the base <see cref="SpeechToTextOptions"/>.</summary>
/// <remarks>
/// These options map to xAI's <c>/v1/stt</c> REST and WebSocket parameters.
/// </remarks>
public class GrokSpeechToTextOptions : SpeechToTextOptions
{
    /// <summary>Initializes a new instance of the <see cref="GrokSpeechToTextOptions"/> class.</summary>
    public GrokSpeechToTextOptions()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GrokSpeechToTextOptions"/> class by cloning another instance.</summary>
    protected GrokSpeechToTextOptions(GrokSpeechToTextOptions? other)
        : base(other)
    {
        if (other is null)
            return;

        Format = other.Format;
        AudioFormat = other.AudioFormat;
        Multichannel = other.Multichannel;
        Channels = other.Channels;
        Diarize = other.Diarize;
        InterimResults = other.InterimResults;
        Endpointing = other.Endpointing;
    }

    /// <summary>Gets or sets a value indicating whether xAI should apply inverse text normalization to the transcript.</summary>
    public bool? Format { get; set; }

    /// <summary>Gets or sets the raw input audio format hint or streaming encoding, such as <c>pcm</c>, <c>mulaw</c>, or <c>alaw</c>.</summary>
    public string? AudioFormat { get; set; }

    /// <summary>Gets or sets a value indicating whether xAI should transcribe each channel independently.</summary>
    public bool? Multichannel { get; set; }

    /// <summary>Gets or sets the number of audio channels.</summary>
    public int? Channels { get; set; }

    /// <summary>Gets or sets a value indicating whether xAI should include speaker diarization data.</summary>
    public bool? Diarize { get; set; }

    /// <summary>Gets or sets a value indicating whether xAI streaming should emit interim partial transcripts.</summary>
    public bool? InterimResults { get; set; }

    /// <summary>Gets or sets the silence duration in milliseconds before xAI emits an utterance-final event.</summary>
    public int? Endpointing { get; set; }

    /// <inheritdoc />
    public override SpeechToTextOptions Clone() => new GrokSpeechToTextOptions(this);
}
