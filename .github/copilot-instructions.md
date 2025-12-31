# xAI .NET SDK Repository

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

The xAI .NET SDK repository contains a gRPC client for xAI generated from the original .proto files as well as implementations for Microsoft.Extensions.AI abstractions.

## Working Effectively

### Essential Build Commands
- **Restore dependencies**: `dotnet restore`

- **Build the entire solution**: `dotnet build`

- **Run tests**: `dotnet test`
  - Runs all unit tests across the solution

### Build Validation and CI Requirements
- **Always run before committing**: `dnx --yes retest`
- **NEVER CANCEL** long-running builds or tests

### Project Structure and Navigation

#### Key Directories

| Directory | Description |
|-----------|-------------|
| `src/xAI.Protocol/` | gRPC protocol client generated from `.proto` files. Contains protobuf definitions and generated gRPC service clients. |
| `src/xAI/` | Main SDK library implementing `Microsoft.Extensions.AI` abstractions (`IChatClient`). |
| `src/xAI/Extensions/` | Helper extension methods (`ChatExtensions.cs`, `Throw.cs`). |
| `src/xAI.Tests/` | Unit tests using xUnit. |
| `src/xAI.Tests/Extensions/` | Test helper utilities (`Attributes.cs`, `Configuration.cs`, `Logging.cs`, `CallHelpers.cs`). |
| `src/xAI.Protocol/google/protobuf/` | Google Protobuf well-known types (`empty.proto`, `timestamp.proto`). |

#### Projects

| Project | Target Framework(s) | Description |
|---------|---------------------|-------------|
| `xAI.Protocol` | `net8.0` | gRPC protocol client with generated Protobuf/gRPC code from `.proto` files. |
| `xAI` | `net8.0`, `net10.0` | Main SDK with `IChatClient` implementation and AI tool abstractions. |
| `xAI.Tests` | `net10.0` | Unit tests using xUnit, Moq, and Microsoft.NET.Test.Sdk. |

#### Build Outputs

| Output | Location |
|--------|----------|
| Protocol generated code | `src/xAI.Protocol/obj/Debug/net8.0/` (e.g., `Chat.cs`, `ChatGrpc.cs`, `Models.cs`, etc.) |
| xAI assembly | `src/xAI/bin/Debug/net8.0/` and `src/xAI/bin/Debug/net10.0/` |
| Test assembly | `src/xAI.Tests/bin/Debug/net10.0/` |

#### Protobuf Files

| File | Description |
|------|-------------|
| `src/xAI.Protocol/chat.proto` | Chat completion service definitions. |
| `src/xAI.Protocol/models.proto` | Model listing service definitions. |
| `src/xAI.Protocol/embed.proto` | Embedding service definitions. |
| `src/xAI.Protocol/image.proto` | Image generation service definitions. |
| `src/xAI.Protocol/tokenize.proto` | Tokenization service definitions. |
| `src/xAI.Protocol/sample.proto` | Sampling service definitions. |
| `src/xAI.Protocol/auth.proto` | Authentication service definitions. |
| `src/xAI.Protocol/documents.proto` | Document service definitions. |
| `src/xAI.Protocol/deferred.proto` | Deferred operations service definitions. |
| `src/xAI.Protocol/usage.proto` | Usage tracking service definitions. |
