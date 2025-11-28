![Icon](assets/icon.png) Grok Client (gRPC)
============

[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/oss.svg?color=blue)](license.txt) 
[![Version](https://img.shields.io/nuget/vpre/Devlooped.GrokClient.svg?color=royalblue)](https://www.nuget.org/packages/Devlooped.GrokClient)
[![Downloads](https://img.shields.io/nuget/dt/Devlooped.GrokClient.svg?color=green)](https://www.nuget.org/packages/Devlooped.GrokClient)

A full XAI/Grok client based on the official full gRPC API.

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->

<!-- #content -->
## Usage

```csharp
var builder = Host.CreateApplicationBuilder(args); // or WebApplication.CreateBuilder(args);

builder.Services.AddGrokClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!);

var app = builder.Build();
```

This package leverages the [gRPC client factory](https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory) 
integration for seamless dependency injection:

```csharp
class MyService(Chat.ChatClient chat, Documents.DocumentsClient docs, Embedder.EmbedderClient embed)
{
    // use clients
}
```

## Auto-updating

This project contains an automated mechanism to always fetch the latest version 
of the official .proto files from XAI, ensuring it remains up-to-date with any changes 
or additions made to the API as soon as they are published.

See for example the [introduction of tool output and citations](https://github.com/devlooped/GrokClient/pull/3).

<!-- #content -->

<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->