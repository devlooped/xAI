using System.Text.Json;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Devlooped.Grok;

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
            Model = "grok-4-1-fast",
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
            }
        };

        var response = await client.GetCompletionAsync(request);

        Assert.NotNull(response);
        output.WriteLine($"Response ID: {response.Id}");
        output.WriteLine($"Model: {response.Model}");

        // Check if we have outputs
        Assert.NotEmpty(response.Outputs);
        var firstOutput = response.Outputs[0];
        var invokedGetDate = false;

        output.WriteLine($"Finish Reason: {firstOutput.FinishReason}");

        // The model should call tools
        if (firstOutput.Message.ToolCalls.Count > 0)
        {
            output.WriteLine($"\nTool Calls ({firstOutput.Message.ToolCalls.Count}):");

            foreach (var toolCall in firstOutput.Message.ToolCalls)
            {
                output.WriteLine($"  - ID: {toolCall.Id}");
                output.WriteLine($"    Type: {toolCall.Type}");
                output.WriteLine($"    Status: {toolCall.Status}");

                if (toolCall.Function != null)
                {
                    output.WriteLine($"    Function: {toolCall.Function.Name}");
                    output.WriteLine($"    Arguments: {toolCall.Function.Arguments}");

                    // If it's our local get_date function, we need to execute it
                    if (toolCall.Function.Name == "get_date" && toolCall.Type == ToolCallType.ClientSideTool)
                    {
                        output.WriteLine($"    -> Local function call detected, would execute on client side");

                        // Execute the function
                        var currentDate = DateTime.Now.ToString("yyyy-MM-dd");
                        output.WriteLine($"    -> Result: {currentDate}");

                        var call = new Message
                        {
                            Role = MessageRole.RoleAssistant,
                        };
                        call.ToolCalls.AddRange(firstOutput.Message.ToolCalls);

                        // Continue the conversation with the function result
                        var followUpRequest = new GetCompletionsRequest
                        {
                            Model = request.Model,
                            Messages =
                            {
                                request.Messages[0], // Original user message
                                call,
                                //new Message
                                //{
                                //    Role = MessageRole.RoleAssistant,
                                //    ToolCalls = { toolCall }
                                //},
                                new Message
                                {
                                    Role = MessageRole.RoleTool,
                                    ToolCallId = toolCall.Id,
                                    Content = { new Content { Text = currentDate } }
                                }
                            },
                            Tools = { request.Tools[0], request.Tools[1] }
                        };

                        var followUpResponse = await client.GetCompletionAsync(followUpRequest);
                        invokedGetDate = true;

                        // There should be no more tool calls after we return the client-side one.
                        Assert.Empty(followUpResponse.Outputs.SelectMany(x => x.Message.ToolCalls));
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(firstOutput.Message.Content))
        {
            output.WriteLine($"\nContent: {firstOutput.Message.Content}");
        }

        // Check for citations
        if (response.Citations.Count > 0)
        {
            output.WriteLine($"\nCitations ({response.Citations.Count}):");
            foreach (var citation in response.Citations)
            {
                output.WriteLine($"  - {citation}");
            }
        }

        Assert.True(invokedGetDate);
    }
}
