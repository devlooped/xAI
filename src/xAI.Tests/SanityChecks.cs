using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using xAI.Protocol;
using Xunit.Abstractions;

namespace xAI.Tests;

public class SanityChecks(ITestOutputHelper output)
{
    [SecretsFact("XAI_API_KEY")]
    public async Task ListModelsAsync()
    {
        var services = new ServiceCollection()
            .AddGrokClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!)
            .BuildServiceProvider();

        var client = services.GetRequiredService<Models.ModelsClient>();

        var models = await client.ListLanguageModelsAsync();

        Assert.NotNull(models);

        foreach (var model in models)
            output.WriteLine(model.Name);
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task ExecuteLocalFunctionWithWebSearch()
    {
        var services = new ServiceCollection()
            .AddGrokClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!)
            .BuildServiceProvider();

        var client = services.GetRequiredService<Chat.ChatClient>();

        // Define a local function to get the current date
        var getDateFunction = new Function
        {
            Name = "get_date",
            Description = "Get the current date in YYYY-MM-DD format",
            Parameters = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            })
        };

        // Create the request with both a local function and web_search tool
        var request = new GetCompletionsRequest
        {
            Model = "grok-4-1-fast-non-reasoning",
            Messages =
            {
                new Message
                {
                    Role = MessageRole.RoleSystem,
                    Content = { new Content { Text = "You use the get_date function to get the current date, and Nasdaq and Yahoo finance for stocks news." } }
                },
                new Message
                {
                    Role = MessageRole.RoleUser,
                    Content = { new Content { Text = "What's Tesla stock price today?" } }
                }
            },
            Tools =
            {
                new Tool { Function = getDateFunction },
                new Tool { WebSearch = new WebSearch() }
            },
            Include = { IncludeOption.InlineCitations }
        };

        var response = await client.GetCompletionAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response.Id);
        Assert.NotEmpty(response.Id);
        Assert.Equal(request.Model, response.Model);
        Assert.NotEmpty(response.Outputs);

        var firstOutput = response.Outputs[0];
        Assert.NotEmpty(firstOutput.Message.ToolCalls);

        var getDateToolCall = firstOutput.Message.ToolCalls
            .FirstOrDefault(tc => tc.Function?.Name == "get_date" && tc.Type == ToolCallType.ClientSideTool);

        Assert.NotNull(getDateToolCall);
        Assert.NotNull(getDateToolCall.Id);
        Assert.NotEmpty(getDateToolCall.Id);
        Assert.Equal("get_date", getDateToolCall.Function.Name);

        output.WriteLine($"Found client-side tool call: {getDateToolCall.Function.Name} (ID: {getDateToolCall.Id})");

        var currentDate = DateTime.Now.ToString("yyyy-MM-dd");

        var call = new Message
        {
            Role = MessageRole.RoleAssistant,
        };
        call.ToolCalls.AddRange(firstOutput.Message.ToolCalls);

        var followUpRequest = new GetCompletionsRequest
        {
            Model = request.Model,
            Messages =
            {
                request.Messages[0],
                call,
                new Message
                {
                    Role = MessageRole.RoleTool,
                    Content = { new Content { Text = currentDate } }
                }
            },
            Tools = { request.Tools },
            Include = { IncludeOption.InlineCitations, IncludeOption.WebSearchCallOutput }
        };

        var followUpResponse = await client.GetCompletionAsync(followUpRequest);

        Assert.NotNull(followUpResponse);
        Assert.NotEmpty(followUpResponse.Outputs);

        var remainingClientToolCalls = followUpResponse.Outputs
            .SelectMany(x => x.Message.ToolCalls)
            .Where(tc => tc.Type == ToolCallType.ClientSideTool);

        Assert.Empty(remainingClientToolCalls);

        var finalOutput = followUpResponse.Outputs.Last();
        Assert.NotNull(finalOutput.Message.Content);
        Assert.NotEmpty(finalOutput.Message.Content);
    }
}
