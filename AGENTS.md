# xAI SDK implementation notes

- `GrokClient` is primarily backed by generated gRPC protocol clients, but text to speech uses xAI's documented REST/WebSocket voice endpoints because there are no generated TTS protocol types in `src\xAI.Protocol`.
- `AsITextToSpeechClient` returns an `ITextToSpeechClient` implementation that uses `POST /v1/tts` for unary audio and `wss://.../v1/tts` for streaming audio.
- TTS defaults follow xAI docs: voice `eve`, language `en` when omitted by `TextToSpeechOptions`, and MP3 output when no codec is specified.
