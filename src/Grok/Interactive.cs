using System.Diagnostics;
using System.Text.Json;
using DotNetConfig;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Json;
using xAI;
using xAI.Protocol;
using static Spectre.Console.AnsiConsole;

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
            apiKey = Ask<string>("Enter Grok API key:");
            Config.Build(ConfigLevel.Global).SetString("grok", "apikey", apiKey);
        }

        client = new GrokClient(apiKey);

        _ = Task.Run(InputListener, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cts.Cancel();
        MarkupLine($":robot: Stopping");
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

        modelId = Prompt(new SelectionPrompt<string>().Title("Select model").AddChoices(choices));
        Config.Build(ConfigLevel.Global).SetString("grok", "modelid", modelId);

        var chat = client!.GetChatClient().AsIChatClient(modelId);
        var options = new GrokChatOptions
        {
            Instructions = "Reply in the language, style and tone used by the user.",
            Include =
            [
                IncludeOption.CodeExecutionCallOutput,
                IncludeOption.XSearchCallOutput,
                IncludeOption.WebSearchCallOutput,
            ],
            Tools = [new GrokXSearchTool(), new HostedWebSearchTool(), new HostedCodeInterpreterTool()]
        };
        var conversation = new List<ChatMessage>();

        MarkupLine($":robot: Ready");
        Markup($":person_beard: ");
        var green = new Style(Color.Lime);
        var red = new Style(Color.Red);
        var yellow = new Style(Color.Yellow);

        while (!cts.IsCancellationRequested)
        {
            var input = ReadInput(cts.Token);
            if (!string.IsNullOrWhiteSpace(input))
            {
                try
                {
                    if (input is "cls" or "clear")
                    {
                        System.Console.Clear();
                        conversation.Clear();
                    }
                    else
                    {
                        conversation.Add(new ChatMessage(ChatRole.User, input));
                        var response = new ChatMessage(ChatRole.Assistant, default(string?));
                        await foreach (var update in chat.GetStreamingResponseAsync(conversation, options, cts.Token))
                        {
                            foreach (var content in update.Contents)
                            {
                                var grid = new Grid()
                                    .AddColumn(new GridColumn().Width(2).Padding(0, 0))
                                    .AddColumn(new GridColumn().Padding(1, 0))
                                    .AddColumn(new GridColumn().Padding(1, 0));

                                if (content.RawRepresentation is not ToolCall tool)
                                    continue;

                                if (content is CodeInterpreterToolResultContent codeResult)
                                {
                                    grid.AddRow(new Markup(":desktop_computer:"), new Markup($" {tool.Function.Name} :check_mark:"));
                                    if (codeResult.Outputs?.ConcatText() is { } output)
                                    {
                                        if (output.StartsWith('{') && output.EndsWith('}') &&
                                            JsonElement.Parse(output) is var json &&
                                            json.TryGetProperty("stdout", out var stdOut) &&
                                            json.TryGetProperty("stderr", out var stdErr))
                                        {
                                            if (stdOut.GetString()?.Trim() is { Length: > 0 } outText)
                                                grid.AddRow(new Text("  "), new Panel(new Paragraph(outText, green))
                                                    .Border(BoxBorder.Square));
                                            if (stdErr.GetString()?.Trim() is { Length: > 0 } errText)
                                                grid.AddRow(new Text("  "), new Panel(new Paragraph(errText, red))
                                                    .Border(BoxBorder.Square));
                                        }
                                        else
                                        {
                                            grid.AddRow(new Text("  "), new Panel(new Paragraph(output, green))
                                                .Border(BoxBorder.Square));
                                        }
                                    }
                                    Write(grid);
                                    continue;
                                }

                                if (tool.Function.Arguments.StartsWith('{') && tool.Function.Arguments.EndsWith('}'))
                                {
                                    var json = JsonElement.Parse(tool.Function.Arguments);
                                    if (tool.Type == ToolCallType.WebSearchTool &&
                                        json.TryGetProperty("query", out var query))
                                    {
                                        if (tool.Status != ToolCallStatus.Completed)
                                        {
                                            grid.AddRow(new Markup(":magnifying_glass_tilted_right:"), new Text(tool.Function.Name),
                                                new Text(query.GetString() ?? " ", yellow));
                                        }
                                    }
                                    else if (content is CodeInterpreterToolCallContent &&
                                        json.TryGetProperty("code", out var code))
                                    {
                                        // We don't want this tool content case to fall back below unless it's pending.
                                        if (tool.Status != ToolCallStatus.Completed)
                                        {
                                            grid.AddRow(new Markup(":desktop_computer:"), new Markup($" {tool.Function.Name} :hourglass_not_done:"));
                                            grid.AddRow(new Text("  "), new Panel(new Paragraph(code.GetString()?.Trim() ?? "", green))
                                                .Border(BoxBorder.Square));
                                        }
                                    }
                                    else if (tool.Function.Name == "browse_page" &&
                                        json.TryGetProperty("url", out var url))
                                    {
                                        if (tool.Status != ToolCallStatus.Completed)
                                        {
                                            var link = url.GetString() ?? "";
                                            grid.AddRow(new Markup(":globe_with_meridians:"), new Text(tool.Function.Name),
                                                new Text(link, new Style(Color.Blue, link: link)));
                                        }
                                    }
                                    else
                                    {
                                        grid.AddRow(new Markup(":hammer_and_pick:"), new Text(tool.Function.Name));
                                        grid.AddRow(new Text(""), new JsonText(tool.Function.Arguments));
                                    }
                                }

                                Write(grid);
                            }

                            foreach (var thinking in update.Contents.OfType<TextReasoningContent>())
                                MarkupLineInterpolated($":brain: {thinking.Text}");
                            foreach (var content in update.Contents.OfType<TextContent>())
                                System.Console.Write(content.Text);

                            foreach (var content in update.Contents)
                                response.Contents.Add(content);
                        }
                        WriteLine();
                        conversation.Add(response);
                    }
                }
                catch (Exception e)
                {
                    WriteException(e);
                }
                finally
                {
                    Markup($":person_beard: ");
                }
            }
            else
            {
                Markup($":person_beard: ");
            }
        }
    }

    static string ReadInput(CancellationToken cancellation)
    {
        var sb = new System.Text.StringBuilder();
        while (!cancellation.IsCancellationRequested)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                {
                    sb.Append(Environment.NewLine);
                    System.Console.WriteLine();
                }
                else
                {
                    System.Console.WriteLine();
                    break;
                }
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                    System.Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                System.Console.Write(key.KeyChar);
            }
        }

        return sb.ToString().Trim();
    }
}
