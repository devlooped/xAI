using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class ConfigurationExtensions
{
    public static IConfiguration Configuration { get; } = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets(Assembly.GetExecutingAssembly())
        .Build();

    public static TOptions GetOptions<TOptions>(this IConfiguration configuration, string name)
        where TOptions : class, new()
        => new ServiceCollection()
            .Configure<TOptions>(configuration.GetSection(name))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<TOptions>>()
            .Value;

    public static TOptions GetOptions<TOptions>(this IConfiguration configuration)
        where TOptions : class, new()
    {
        var name = typeof(TOptions).Name;
        if (name.EndsWith("Options"))
            return configuration.GetOptions<TOptions>(name[..^7]);

        return configuration.GetOptions<TOptions>(name);
    }
}
