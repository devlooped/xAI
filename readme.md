![Icon](assets/icon.png) .NET SDK
============

[![Version](https://img.shields.io/nuget/vpre/xAI.svg?color=royalblue)](https://www.nuget.org/packages/xAI)
[![Downloads](https://img.shields.io/nuget/dt/xAI.svg?color=darkmagenta)](https://www.nuget.org/packages/xAI)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](osmfeula.txt)
[![OSS](https://img.shields.io/github/license/devlooped/oss.svg?color=blue)](license.txt) 

xAI .NET SDK based on the official gRPC API reference from xAI with integration for 
Microsoft.Extensions.AI and Microsoft.Agents.AI.

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->

<!-- #xai -->
xAI/Grok integration for Microsoft.Extensions.AI `IChatClient` with full support for all 
[agentic tools](https://docs.x.ai/docs/guides/tools/overview):

```csharp
var grok = new GrokClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!)
    .AsIChatClient("grok-4.1-fast");
```
## Web Search

```csharp
var messages = new Chat()
{
    { "system", "You are an AI assistant that knows how to search the web." },
    { "user", "What's Tesla stock worth today? Search X and the news for latest info." },
};

var grok = new GrokClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!).AsIChatClient("grok-4.1-fast");

var options = new ChatOptions
{
    Tools = [new HostedWebSearchTool()] // 👈 compatible with OpenAI
};

var response = await grok.GetResponseAsync(messages, options);
```

In addition to basic web search as shown above, Grok supports more 
[advanced search](https://docs.x.ai/docs/guides/tools/search-tools) scenarios, 
which can be opted-in by using Grok-specific types:

```csharp
var grok = new GrokChatClient(Environment.GetEnvironmentVariable("XAI_API_KEY")!)
    .AsIChatClient("grok-4.1-fast");
var response = await grok.GetResponseAsync(
    "What are the latest product news by Tesla?", 
    new ChatOptions
    {
        Tools = [new GrokSearchTool()
        {
            AllowedDomains = [ "ir.tesla.com" ]
        }]
    });
```

You can alternatively set `ExcludedDomains` instead, and enable image 
understanding with `EnableImageUndestanding`. Learn more about these filters 
at [web search parameters](https://docs.x.ai/docs/guides/tools/search-tools#web-search-parameters).

## X Search

In addition to web search, Grok also supports searching on X (formerly Twitter):

```csharp
var response = await grok.GetResponseAsync(
    "What's the latest on Optimus?", 
    new ChatOptions
    {
        Tools = [new GrokXSearchTool
        {
            // AllowedHandles = [...],
            // ExcludedHandles = [...],
            // EnableImageUnderstanding = true,
            // EnableVideoUnderstanding = true,
            // FromDate = ...,
            // ToDate = ...,
        }]
    });
```

Learn more about available filters at [X search parameters](https://docs.x.ai/docs/guides/tools/search-tools#x-search-parameters).

You can combine both web and X search in the same request by adding both tools.

## Code Execution

The code execution tool enables Grok to write and execute Python code in real-time, 
dramatically expanding its capabilities beyond text generation. This powerful feature 
allows Grok to perform precise calculations, complex data analysis, statistical 
computations, and solve mathematical problems that would be impossible through text alone.

This is Grok's equivalent of the OpenAI code interpreter, and is configured the same way:

```csharp
var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");
var response = await grok.GetResponseAsync(
    "Calculate the compound interest for $10,000 at 5% annually for 10 years",
    new ChatOptions
    {
        Tools = [new HostedCodeInterpreterTool()]
    });

var text = response.Text;
Assert.Contains("$6,288.95", text);
```

If you want to access the output from the code execution, you can add that as an 
include in the options:

```csharp
var grok = new GrokClient(Configuration["XAI_API_KEY"]!).AsIChatClient("grok-4-fast");
var options = new GrokChatOptions
{
    Include = { IncludeOption.CodeExecutionCallOutput },
    Tools = [new HostedCodeInterpreterTool()]
};

var response = await grok.GetResponseAsync(
    "Calculate the compound interest for $10,000 at 5% annually for 10 years",
    options);

var content = response.Messages
    .SelectMany(x => x.Contents)
    .OfType<CodeInterpreterToolResultContent>()
    .First();

foreach (AIContent output in content.Outputs)
    // process outputs from code interpreter
```

Learn more about the [code execution tool](https://docs.x.ai/docs/guides/tools/code-execution-tool).

## Collection Search

If you maintain a [collection](https://docs.x.ai/docs/key-information/collections), 
Grok can perform semantic search on it:

```csharp
var options = new ChatOptions
{
    Tools = [new HostedFileSearchTool {
        Inputs = [new HostedVectorStoreContent("[collection_id]")]
    }]
};
```

To receive the actual search results and file references, include `CollectionsSearchCallOutput` in the options:

```csharp
var options = new GrokChatOptions
{
    Include = [IncludeOption.CollectionsSearchCallOutput],
    Tools = [new HostedFileSearchTool {
        Inputs = [new HostedVectorStoreContent("[collection_id]")]
    }]
};

var response = await grok.GetResponseAsync(messages, options);

// Access the search results with file references
var results = response.Messages
    .SelectMany(x => x.Contents)
    .OfType<CollectionSearchToolResultContent>();

foreach (var result in results)
{
    // Each result contains files that were found and referenced
    var files = result.Outputs?.OfType<HostedFileContent>();
    foreach (var file in files ?? [])
    {
        Console.WriteLine($"File: {file.Name} (ID: {file.FileId})");
        
        // Files include citation annotations with snippets
        foreach (var citation in file.Annotations?.OfType<CitationAnnotation>() ?? [])
        {
            Console.WriteLine($"  Title: {citation.Title}");
            Console.WriteLine($"  Snippet: {citation.Snippet}");
            Console.WriteLine($"  URL: {citation.Url}"); // collections://[collection_id]/files/[file_id]
        }
    }
}
```

Citations from collection search include:
- **Title**: Extracted from the first line of the chunk content (if available), typically the file name or heading
- **Snippet**: The relevant text excerpt from the document
- **FileId**: Identifier of the source file in the collection
- **Url**: A `collections://` URI pointing to the specific file within the collection
- **ToolName**: Always set to `"collections_search"`

Learn more about [collection search](https://docs.x.ai/docs/guides/tools/collections-search-tool).

## Remote MCP

Remote MCP Tools allow Grok to connect to external MCP (Model Context Protocol) servers.
This example sets up the GitHub MCP server so queries about releases (limited specifically 
in this case): 

```csharp
var options = new ChatOptions
{
    Tools = [new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/") {
        AuthorizationToken = Configuration["GITHUB_TOKEN"]!,
        AllowedTools = ["list_releases"],
    }]
};
```

Just like with code execution, you can opt-in to surfacing the MCP outputs in 
the response:

```csharp
var options = new GrokChatOptions
{
    // Exposes McpServerToolResultContent in responses
    Include = { IncludeOption.McpCallOutput },
    Tools = [new HostedMcpServerTool("GitHub", "https://api.githubcopilot.com/mcp/") {
        AuthorizationToken = Configuration["GITHUB_TOKEN"]!,
        AllowedTools = ["list_releases"],
    }]
};

```

Learn more about [Remote MCP tools](https://docs.x.ai/docs/guides/tools/remote-mcp-tools).
<!-- #xai -->

# xAI.Protocol

[![Version](https://img.shields.io/nuget/vpre/xAI.Protocol.svg?color=royalblue)](https://www.nuget.org/packages/xAI.Protocol)
[![Downloads](https://img.shields.io/nuget/dt/xAI.Protocol.svg?color=green)](https://www.nuget.org/packages/xAI.Protocol)

<!-- #protocol -->
## Usage

The xAI.Protocol package provides a .NET client for the gRPC API from xAI with full support for all services 
documented in the [official API reference](https://docs.x.ai/docs/grpc-reference) and 
corresponding [proto files](https://github.com/xai-org/xai-proto/tree/main/proto/xai/api/v1).

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
of the [official .proto files from XAI](https://github.com/xai-org/xai-proto/tree/main/proto/xai/api/v1), 
ensuring it remains up-to-date with any changes or additions made to the API as soon as they are published.

See for example the [introduction of tool output and citations](https://github.com/devlooped/GrokClient/pull/3).

<!-- #protocol -->

<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Uno Platform](https://avatars.githubusercontent.com/u/52228309?v=4&s=39 "Uno Platform")](https://github.com/unoplatform)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![David JENNI](https://avatars.githubusercontent.com/u/3200210?v=4&s=39 "David JENNI")](https://github.com/davidjenni)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![Vincent Limo](https://avatars.githubusercontent.com/devlooped-user?s=39 "Vincent Limo")](https://github.com/v-limo)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Justin Wendlandt](https://avatars.githubusercontent.com/u/1068431?u=f7715ed6a8bf926d96ec286f0f1c65f94bf86928&v=4&s=39 "Justin Wendlandt")](https://github.com/jwendl)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![Michael Hagedorn](https://avatars.githubusercontent.com/u/61711586?u=8f653dfcb641e8c18cc5f78692ebc6bb3a0c92be&v=4&s=39 "Michael Hagedorn")](https://github.com/Eule02)
[![](https://avatars.githubusercontent.com/devlooped-user?s=39 "")](https://github.com/henkmartijn)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![mccaffers](https://avatars.githubusercontent.com/u/16667079?u=739e110e62a75870c981640447efa5eb2cb3bc8f&v=4&s=39 "mccaffers")](https://github.com/mccaffers)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
