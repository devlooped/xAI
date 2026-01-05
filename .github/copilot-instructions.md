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

### Code Style and Formatting

#### EditorConfig Rules
The repository uses `.editorconfig` for consistent code style:
- **Indentation**: 4 spaces for C# files, 2 spaces for XML/YAML/JSON
- **Line endings**: LF (Unix-style)
- **Sort using directives**: System.* namespaces first (`dotnet_sort_system_directives_first = true`)
- **Type references**: Prefer language keywords over framework type names (`int` vs `Int32`)
- **Modern C# features**: Use object/collection initializers, coalesce expressions when possible

#### Formatting Validation
- CI enforces formatting with `dotnet format whitespace` and `dotnet format style`
- Run locally: `dotnet format whitespace --verify-no-changes -v:diag --exclude ~/.nuget`
- Fix formatting: `dotnet format` (without `--verify-no-changes`)

### Testing Practices

#### Test Framework
- **xUnit** for all unit and integration tests
- **Moq** for mocking dependencies
- Located in `src/xAI.Tests/`

#### Test Attributes
Custom xUnit attributes for conditional test execution:
- `[SecretsFact("XAI_API_KEY")]` - Skips test if required secrets are missing from user secrets or environment variables
- `[LocalFact("SECRET")]` - Runs only locally (skips in CI), requires specified secrets
- `[CIFact]` - Runs only in CI environment

#### API Keys and Secrets
- **Production API Key**: `XAI_API_KEY` (for xAI/Grok API)
- **CI API Key**: `CI_XAI_API_KEY` (used in GitHub Actions)
- **GitHub Token**: `GITHUB_TOKEN` (for GitHub API tests)
- **Local development**: Use `dotnet user-secrets` or `.env` file (copied to output directory)
- **Configuration loading**: Uses `ConfigurationExtensions.Configuration` which loads from environment variables and user secrets

#### Running Tests
- Full test suite: `dnx --yes retest` (NEVER cancel this - it's the CI validation command)
- With dotnet test: `dotnet test --no-build` (after building)
- Tests require API keys to run integration tests

### Dependency Management

#### Package Sources
- Main packages from NuGet.org
- Internal feed configuration in `src/nuget.config`
- Dev packages may use Sleet feed (configured in CI)

#### Key Dependencies
- **Google.Protobuf** (3.33.2) - Protobuf runtime for gRPC
- **Grpc.Net.Client** - gRPC client library
- **Microsoft.Extensions.AI.Abstractions** (10.1.1) - IChatClient abstractions
- **xUnit** (2.9.3) - Testing framework
- **Moq** (4.20.72) - Mocking framework

#### Adding Dependencies
- Add to appropriate `.csproj` file
- Run `dotnet restore` to update dependencies
- Ensure version consistency across projects where applicable

### Protobuf and Code Generation

#### Auto-Generated Code
- **DO NOT EDIT** generated files in `src/xAI.Protocol/obj/Debug/net8.0/` (e.g., `Chat.cs`, `ChatGrpc.cs`)
- Generated code is produced from `.proto` files during build
- Uses `Grpc.Tools` package for code generation

#### Proto File Updates
- Proto files are automatically synced from [xai-org/xai-proto](https://github.com/xai-org/xai-proto)
- Managed via `.netconfig` with `dotnet-file` tool
- A custom `protofix.cs` script runs before proto compilation to fix issues
- Build target `FixProto` executes: `dotnet run --file protofix.cs`

#### Modifying Protocol
1. **Never edit .proto files directly** - they are auto-synced
2. If protocol changes needed, coordinate with xAI upstream
3. Run `dotnet build` to regenerate client code after proto updates

### Common Workflows and Troubleshooting

#### Build Issues
- **gRPC generation fails**: Clean solution with `dotnet clean`, then rebuild
- **Proto compilation errors**: Check `protofix.cs` execution in build output
- **Missing types**: Ensure `dotnet restore` completed successfully

#### Test Issues
- **Tests skipped**: Missing API keys - set `XAI_API_KEY` in user secrets or environment
- **Authentication errors**: Verify API key is valid and not expired
- **CI-only failures**: May be using different API keys or missing secrets

#### CI/CD Pipeline
- **Build workflow**: `.github/workflows/build.yml` - runs on PR and push to main/dev branches
- **Publish workflow**: Publishes to Sleet feed when `SLEET_CONNECTION` secret is available
- **OS matrix**: Configured in `.github/workflows/os-matrix.json` (defaults to ubuntu-latest)

### Multi-Targeting

#### Target Frameworks
- **xAI.Protocol**: `net8.0` only
- **xAI**: `net8.0` and `net10.0` (multi-targeted)
- **xAI.Tests**: `net10.0` only

#### Build Considerations
- Use `-f` flag to build specific framework: `dotnet build -f net8.0`
- Tests run on latest framework (net10.0)
- Ensure code compatibility across all target frameworks

### Special Files and Tools

#### dnx Command
- **Purpose**: Custom dotnet tool for running tests with retry logic
- **Usage**: `dnx --yes retest` - runs tests with automatic retry on transient failures
- **In CI**: `dnx --yes retest -- --no-build` (skips build, runs tests only)
- **Important**: NEVER cancel this command during execution

#### Directory.Build.rsp
- MSBuild response file with default build arguments
- `-nr:false` - disables node reuse
- `-m:1` - single-threaded build (for stability)
- `-v:m` - minimal verbosity

### Microsoft.Extensions.AI Integration

#### IChatClient Implementation
- Main integration point: `GrokClient.AsIChatClient(modelId)`
- Supports all standard `Microsoft.Extensions.AI` patterns
- Grok-specific extensions via `GrokChatClient` and `GrokChatOptions`

#### Tool Support
- **Web Search**: `HostedWebSearchTool` or `GrokSearchTool`
- **X Search**: `GrokXSearchTool` with filtering options
- **Code Execution**: `HostedCodeInterpreterTool`
- **Collection Search**: `HostedFileSearchTool` with vector store content
- **Remote MCP**: `HostedMcpServerTool` for Model Context Protocol servers

### Security and Best Practices

#### API Keys
- **NEVER** commit API keys to source code
- Use environment variables or user secrets for local development
- CI uses encrypted secrets in GitHub Actions

#### Code Quality
- All PRs must pass format validation
- Tests must pass on all target frameworks
- Follow existing patterns and conventions in the codebase
