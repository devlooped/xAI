using System.Diagnostics;
using System.Text;
using DotNetConfig;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using xAI;
using xAI.Protocol;

namespace Grok;

partial class Interactive(IConfiguration configuration) : IHostedService
{
    readonly CancellationTokenSource cts = new();
    string? apiKey = configuration["grok:apikey"];
    GrokClient? client;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = AnsiConsole.Ask<string>("Enter Grok API key:");
            Config.Build(ConfigLevel.Global).SetString("grok", "apikey", apiKey);
        }

        client = new GrokClient(apiKey);

        _ = Task.Run(InputListener, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cts.Cancel();
        AnsiConsole.MarkupLine($":robot: Stopping");
        return Task.CompletedTask;
    }

    async Task InputListener()
    {
        Debug.Assert(client != null);

        var models = await client.GetModelsClient().ListLanguageModelsAsync();
        var modelId = configuration["grok:modelid"];
        var choices = models.Select(x => x.Aliases.OrderBy(a => a.Length).FirstOrDefault() ?? x.Name).ToList();
        if (modelId != null && choices.IndexOf(modelId) is var index && index > 0)
        {
            choices.RemoveAt(index);
            choices.Insert(0, modelId);
        }

        modelId = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Select model").AddChoices(choices));
        Config.Build(ConfigLevel.Global).SetString("grok", "modelid", modelId);

        var chat = client!.GetChatClient().AsIChatClient(modelId);
        var options = new ChatOptions
        {
            Tools = [new GrokXSearchTool(), new HostedWebSearchTool(), new HostedCodeInterpreterTool()]
        };
        var conversation = new List<ChatMessage>();

        AnsiConsole.MarkupLine($":robot: Ready");
        AnsiConsole.Markup($":person_beard: ");

        while (!cts.IsCancellationRequested)
        {
            var input = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(input))
            {
                try
                {
                    if (input is "cls" or "clear")
                    {
                        Console.Clear();
                        conversation.Clear();
                    }
                    else
                    {
                        conversation.Add(new ChatMessage(ChatRole.User, input));
                        var contents = await AnsiConsole.Status().StartAsync("Sending...", async ctx =>
                        {
                            var contents = new List<TextContent>();
                            await foreach (var update in chat.GetStreamingResponseAsync(conversation, options, cts.Token))
                            {
                                foreach (var tool in update.Contents.Select(x => x.RawRepresentation as ToolCall).Where(x => x != null))
                                    ctx.Status($"Calling: {tool!.Function.Name.EscapeMarkup()}");
                                foreach (var thinking in update.Contents.OfType<TextReasoningContent>())
                                    ctx.Status($"Thinking: {thinking.Text.EscapeMarkup()}");

                                contents.AddRange(update.Contents.OfType<TextContent>());
                            }
                            return contents;
                        });

                        foreach (var content in contents)
                            Console.Write(content);

                        Console.WriteLine();
                    }
                }
                catch (Exception e)
                {
                    AnsiConsole.WriteException(e);
                }
                finally
                {
                    AnsiConsole.Markup($":person_beard: ");
                }
            }
            else
            {
                AnsiConsole.Markup($":person_beard: ");
            }
        }
    }
}
