# Codicks Lite MCP

Codicks Lite MCP is a local Model Context Protocol (MCP) server for macOS and (Windows probably). It lets a connected MCP client inspect and work with folders you explicitly approve, while keeping file access and write permissions under your control. It is built with C# and .NET 10 and communicates over stdio.

## Capabilities

- **Approved workspaces:** List and inspect configured folders. Each workspace has its own enabled state and allowed operations.
- **File discovery:** List directories, inspect file metadata, read bounded UTF-8 text, and search for text within approved workspaces.
- **File changes:** Create files and directories, update files using a prior SHA-256 hash, and move files without overwriting an existing destination. Updates support dry runs.
- **Recoverable deletion:** Deleted files move to private recovery storage and can be restored; the MCP tool surface does not provide permanent deletion.
- **Local session control:** Sessions start locked. A local one-time code grants a time-limited read or full session, and local commands can return the server to locked mode.
- **Safety limits:** Workspace-relative paths, denied paths such as `.git` and `.env`, bounded reads and searches, and configurable operation limits restrict what the server can access.
- **Diagnostics:** Server information, security status, and a fixed scratch-file probe help verify a connection.

The server can be used with an MCP client that supports stdio. Optional scripts help configure and run an OpenAI Secure MCP Tunnel; the tunnel client and its credentials are obtained separately.

## Requirements

- macOS (Apple silicon or Intel) for now lol
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building from source; `global.json` pins SDK 10.0.401

Prebuilt release packages are self-contained and do not require the .NET SDK or runtime on the recipient's Mac.

## Build from source

```bash
dotnet restore Codicks.Lite.Mcp.slnx
dotnet build Codicks.Lite.Mcp.slnx -c Release
dotnet test tests/LocalAgent.UnitTests/LocalAgent.UnitTests.csproj -c Release
```

To make a self-contained package for the current Mac architecture:

```bash
bash installers/build_codicks-lite_v1_1_1_release_macos
```

Extract the archive created in `installers/dist/` and run its `./install` script. See [release packaging and installation](installers/README.md) for architecture options and installed commands.

## Configure access

The server's configuration lives at `~/Library/Application Support/CodicksLiteMcp/config/agent.json` after installation. Use [the example configuration](config/agent.example.json) to see the available workspace permissions, file limits, exclusions, and session settings. Workspaces must be explicitly enabled before their files are available through MCP.

After installation, `codicks-lite status` shows the local session state. Run `codicks-lite setup` to configure the optional tunnel, or use `codicks-lite doctor-local` to check the local installation. The installed MCP launcher is `~/.local/bin/codicks-lite-mcp`.

## Project layout

- `src/LocalAgent.Host` — stdio MCP host and tool endpoints
- `src/LocalAgent.Core` — configuration, policies, and service contracts
- `src/LocalAgent.Infrastructure` — file operations, recovery, and session state
- `tests/` — unit and integration tests
- `installers/` — macOS release builder and package installer

## Project status

This is an evolving project. The unit tests pass, while some older stdio integration tests still assume a writable session at startup and need updating for the locked-by-default session model. Review the configuration and test the access policy against your own folders before relying on it for important files.

## Changelogs

- Latest release: v1.1.1 (2026-09-24) skipped version on github init
- v1.1 (2026-09-24)
- v1.0 (2026-09-23)

### v1.1.1 (2026-09-24): Added installer compiler and Bug Fix
  - added installer compiler for easy distribution
  - fixed chatgpt mcp connector creation error: security_status returning an anonymous object
  - fixed cli security current status null

### v1.1.0 (2026-09-24): MCP security status and session locking
  - added session locking to MCP server via expiration and otp
  - added manual session status and permission controls

### v1.0.0 (2026-09-23): Initial release
  - project initialization

## ADVERTISEMENT

Also check out the better version of this project written in rust (first time rust development) [Codicks](https://https://github.com/Bryan-Monterozo/codicks-mcp-harness).