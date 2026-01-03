# xAI .NET SDK Agent Hub

This directory contains specialized GitHub Copilot agents for working with the xAI .NET SDK repository.

## Available Agents

### 1. .NET Developer Agent
**File**: `dotnet-developer.md`  
**Expertise**: .NET 8.0/10.0, C#, NuGet, multi-targeting  
**Use for**: General C# development, project structure, dependency management

### 2. gRPC Protocol Agent
**File**: `grpc-protocol.md`  
**Expertise**: Protocol Buffers, gRPC, code generation  
**Use for**: Working with .proto files, gRPC services, protocol client implementation

### 3. Testing Agent
**File**: `testing.md`  
**Expertise**: xUnit, Moq, test patterns  
**Use for**: Writing and maintaining tests, test coverage, mocking

### 4. Microsoft.Extensions.AI Agent
**File**: `ai-extensions.md`  
**Expertise**: IChatClient, AI tools, chat abstractions  
**Use for**: Implementing AI features, tool integrations, chat completions

### 5. Documentation Agent
**File**: `documentation.md`  
**Expertise**: Technical writing, README, API docs  
**Use for**: Documentation, examples, changelog maintenance

## How to Use

These agents are specialized assistants that understand different aspects of the xAI .NET SDK codebase. GitHub Copilot will automatically leverage these agents based on your current context and task.

## Agent Capabilities

Each agent provides:
- Domain-specific knowledge
- Best practices and guidelines
- Project-specific context
- Code patterns and conventions
- Common tasks and workflows

## Repository Overview

The xAI .NET SDK provides:
- **xAI.Protocol**: gRPC client for xAI API (generated from .proto files)
- **xAI**: Microsoft.Extensions.AI implementation with IChatClient support
- **xAI.Tests**: Comprehensive unit test suite

## Quick Reference

### Build & Test
```bash
dotnet restore        # Restore dependencies
dotnet build          # Build solution
dotnet test           # Run tests
dnx --yes retest      # Full validation (required before commits)
```

### Project Structure
- `src/xAI.Protocol/`: Protocol Buffers and gRPC client
- `src/xAI/`: Main SDK implementation
- `src/xAI.Tests/`: Test suite

### Key Features
- Full gRPC API support
- Microsoft.Extensions.AI IChatClient implementation
- Agentic tools (web search, X search, code execution, etc.)
- Collection search and MCP server support

For detailed information, refer to the main repository README.md and individual agent files.
