using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Devlooped.Grok;

public class ModelsTests(ITestOutputHelper output)
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
}
