# xAI SDK implementation notes

- `GrokClient` is primarily backed by generated gRPC protocol clients, but voice features use xAI's documented REST/WebSocket endpoints because there are no generated voice protocol types in `src\xAI.Protocol`.
- Voice REST calls use `GrokClient.HttpHandler` (backed by `httpHandlers` cache) — a plain `SocketsHttpHandler`+Polly pipeline separate from the gRPC channel. `ChannelHandler` returns `ChannelBase` only; there is no `.Handler` property on it.
- `AsITextToSpeechClient` returns an `ITextToSpeechClient` implementation that uses `POST /v1/tts` for unary audio and `wss://.../v1/tts` for streaming audio.
- `AsISpeechToTextClient` returns an `ISpeechToTextClient` implementation that uses `POST /v1/stt` for file transcription and `wss://.../v1/stt` for raw-audio streaming transcription.
- TTS defaults follow xAI docs: voice `eve`, language `en` when omitted by `TextToSpeechOptions`, and MP3 output when no codec is specified.
- STT streaming defaults follow xAI docs: encoding `pcm` and sample rate `16000` when omitted; WebSocket input must be raw encoded audio, not MP3/WAV container bytes.
- Chat streaming `GetChatCompletionChunk.Usage` values are cumulative within a sampling segment and may reset across tool-driven segments; emit deltas (or restart deltas after a reset) so `ToChatResponse()` totals match non-streaming usage.
