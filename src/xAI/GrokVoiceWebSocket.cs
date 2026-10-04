namespace xAI;

static class GrokVoiceWebSocket
{
    internal static void SetAuthorizationHeader(string? apiKey, Action<string, string> setRequestHeader)
    {
        if (!string.IsNullOrEmpty(apiKey))
            setRequestHeader("Authorization", "Bearer " + apiKey);
    }
}
