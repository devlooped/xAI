using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using static ConfigurationExtensions;

namespace xAI.Tests;

public class ToolCallingFollowing(ITestOutputHelper output)
{
    [SecretsTheory("XAI_API_KEY", Skip = "Comprehensive tests for downstream consumer")]
    [MemberData(nameof(AllDistressMessages))]
    public async Task InvokesDistress(string model, string message)
    {
        var chat = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient(model)
            .AsBuilder()
            .UseFunctionInvocation(configure: client => client.MaximumIterationsPerRequest = 3)
            .UseLogging(output.AsLoggerFactory())
            .Build();

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(SendAlertAsync)]
        };

        var response = await chat.GetResponseAsync(message, options);

        var calledTools = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(fc => fc.Name)
            .ToList();

        Assert.True(
            calledTools.Contains("emergency_alert", StringComparer.OrdinalIgnoreCase),
            $"[{model}] LLM did not call emergency_alert for: \"{message}\". " +
            $"Tools called: [{string.Join(", ", calledTools)}]");
    }

    [SecretsTheory("XAI_API_KEY", Skip = "Comprehensive tests for downstream consumer")]
    [MemberData(nameof(AllRoutineMessages))]
    public async Task DoesNotInvokeDistress(string model, string message)
    {
        var chat = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient(model)
            .AsBuilder()
            .UseFunctionInvocation(configure: client => client.MaximumIterationsPerRequest = 3)
            .UseLogging(output.AsLoggerFactory())
            .Build();

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(SendAlertAsync)],
            ToolMode = ChatToolMode.Auto
        };

        var response = await chat.GetResponseAsync(message, options);

        var calledTools = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(fc => fc.Name)
            .ToList();

        var jopt = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        Assert.False(
            calledTools.Contains("emergency_alert", StringComparer.OrdinalIgnoreCase),
            $"""
            [{model}] LLM did call emergency_alert for: "{message}". 
            Tool calls:
            {string.Join(Environment.NewLine, response.Messages
                .SelectMany(x => x.Contents.OfType<FunctionCallContent>()
                .Select(x => JsonSerializer.Serialize(x, jopt))))}
            """);
    }

    public static IEnumerable<object[]> AllDistressMessages =>
        from model in Models
        from message in DistressPhrases
        select new object[] { model, message };

    public static IEnumerable<object[]> AllRoutineMessages =>
        from model in Models
        from message in RoutinePhrases
        select new object[] { model, message };

    static readonly string[] Models = ["grok-4-1-fast-non-reasoning", "grok-4.20-non-reasoning"];

    static readonly string[] RoutinePhrases =
    [
        "¿qué hora es?",
        "¿qué tiempo hace hoy?",
        "poneme música",
        "cuéntame un chiste",
        "buenas tardes",
        "thanks",
        "gracias",
        "what time is it?",
        "tell me a joke",
        "play some music",
        "good morning",
        "what's the weather like?",
        "remind me to take my pills at 8pm",
    ];

    static readonly string[] DistressPhrases =
    [
        "necesito ayuda, me siento muy mal",
        "socorro",
        "me caí",
        "me caí de la cama",
        "me duele el pecho",
        "no puedo levantarme",
        "llamá a mi hijo, me caí",
        "please help me, I am feeling really bad",
        "I fell down",
        "I'm having chest pains",
        "I can't get up",
        "I think I'm having a heart attack",
    ];

    [DisplayName("emergency_alert")]
    [Description(
        """
        Use ONLY when the elder is expressing their own physical distress, inability to move, or calling for help 
        right now — examples: a fall ('I fell', 'I can't get up', 'no puedo levantarme'), acute pain, difficulty 
        breathing, chest pain, feeling very unwell, or an explicit plea for help ('help me', 'I feel terrible', 
        'please help me', 'I am feeling really bad'). 
        """)]
    public Task<string> SendAlertAsync(
        [Description("The elder's exact words (verbatim transcription). Must be the original user message, not a summary or paraphrase.")]
        string elder_message,
        [Description("Explanation of why the tool is being invoked")]
        string reason,
        [Description("Severity level: 'high' for general distress, 'critical' for falls, chest pain, breathing difficulty")]
        string severity = "high",
        CancellationToken ct = default) => Task.FromResult("done");
}
