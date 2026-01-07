using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Grok;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Some users reported not getting emoji on Windows from F5 in VS so we force UTF-8 encoding. 
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

var host = Host.CreateApplicationBuilder(args);
host.Logging.ClearProviders();

host.Configuration.AddDotNetConfig();
host.Configuration.AddUserSecrets<Program>();

host.Services.AddHttpClient();
host.Services.ConfigureHttpClientDefaults(x =>
{
    if (Debugger.IsAttached)
        x.ConfigureHttpClient(h => h.Timeout = TimeSpan.MaxValue);
    else
        x.AddStandardResilienceHandler();
});

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => cts.Cancel();
host.Services.AddSingleton(cts);
host.Services.AddSingleton<IHostedService, Interactive>();

var app = host.Build();

await app.RunAsync(cts.Token);
