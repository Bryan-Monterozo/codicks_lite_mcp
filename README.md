# Codicks Lite MCP

Codicks Lite MCP is a local Model Context Protocol (MCP) server for macOS. It lets a connected MCP client work with folders you explicitly approve while keeping workspace access, mutation permissions, process execution, and session authorization under local control. It is built with C# and .NET 10 and communicates over stdio.

## Capabilities

- **Approved workspaces:** Each workspace has its own root, enabled state, and allowed operations.
- **File discovery:** List directories, inspect metadata, read bounded UTF-8 text, and search approved workspace content.
- **File changes:** Create files/directories, update files using SHA-256 optimistic concurrency, and move files without overwriting existing destinations.
- **Reviewed changes:** Preview complete-content diffs, preview strict unified patches, and apply only the exact reviewed patch through short-lived one-time review tokens.
- **Recoverable deletion:** Deleted files move to private recovery storage and can be restored. No permanent-delete MCP tool is exposed.
- **Controlled local execution:** `process_exec` can run explicitly configured executables inside approved workspaces. Host execution is supported; the optional Apple-container Sandbox backend is disabled by default.
- **Local session control:** Every MCP process starts locked. A local OTP grants a time-limited READ_ONLY or FULL session.
- **Safety limits:** Workspace-relative paths, denied paths such as `.git` and `.env`, symlink/hard-link rejection, bounded responses, editable-file limits, and execution limits remain authoritative.
- **Auditing:** File mutations, reviewed diff/patch workflows, and process execution write metadata-only audit records.
- **Diagnostics:** Server/security status, local doctor checks, and scratch probes help verify a connection.

The optional OpenAI Secure MCP Tunnel is configured separately. Tunnel credentials are not stored in `agent.json`.

## Requirements

- macOS for now lol
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building from source; `global.json` pins the repository SDK

Prebuilt release packages are self-contained and do not require the .NET SDK/runtime on the recipient Mac.

## Build from source

```bash
dotnet restore Codicks.Lite.Mcp.slnx
dotnet build Codicks.Lite.Mcp.slnx -c Release
dotnet test Codicks.Lite.Mcp.slnx -c Release
```

Build the **1.2.1** self-contained package for the current Mac architecture:

```bash
bash installers/build_codicks-lite_v1_2_1_release_macos --clean --with-tests
```

The installer build is intentionally performed locally from the current workspace. See [release packaging and installation](installers/README.md).

## Configure access

Installed configuration:

```text
~/Library/Application Support/CodicksLiteMcp/config/agent.json
```

Use [config/agent.example.json](config/agent.example.json) as the configuration reference.

Important permissions are explicit:

- `read` for file/query and review previews
- `update` for `file_update` and `file_patch_apply`
- `execute` for `process_exec`

Execution also requires top-level `Execution.Enabled=true`. Sandbox execution additionally requires `Execution.Sandbox.Enabled=true`.

The installer preserves an existing live `agent.json`; upgrades do not automatically grant new workspace permissions or enable execution.

## MCP tool surface

| Tool | Purpose | Session / permission |
|---|---|---|
| `server_info` | Server/runtime information | Available while locked |
| `security_status` | Local session state | Available while locked |
| `workspace_list` | List approved workspaces | READ_ONLY/FULL |
| `workspace_inspect` | Inspect one workspace | READ_ONLY/FULL |
| `file_list` | List directory entries | READ_ONLY/FULL + read |
| `file_stat` | File/directory metadata | READ_ONLY/FULL + read |
| `file_read` | Bounded UTF-8 read + SHA-256 | READ_ONLY/FULL + read |
| `file_search` | Bounded literal search | READ_ONLY/FULL + read |
| `file_diff` | Preview complete replacement content | READ_ONLY/FULL + read |
| `file_patch_preview` | Strict patch validation + canonical review | READ_ONLY/FULL + read |
| `file_patch_apply` | Apply exact reviewed patch | FULL + update |
| `file_create` | Create UTF-8 file | FULL + create |
| `directory_create` | Create directory | FULL + create |
| `file_update` | Full-content SHA-256 update | FULL + update |
| `file_move` | Move/rename regular file | FULL + move |
| `file_delete` | Recoverable delete | FULL + delete |
| `file_restore` | Restore recovery entry | FULL + restore |
| `process_exec` | Controlled configured process execution | FULL + execute |

There is no permanent-delete MCP tool and no raw `shell_exec` / implicit `bash -c` / `zsh -c` tool.

## Review workflow

The v1.2.1 reviewed-change workflow is:

1. `file_read`
2. `file_diff` or `file_patch_preview`
3. inspect the canonical review
4. obtain approval when appropriate
5. `file_patch_apply`
6. report the new SHA-256 and backup id

See:

- [file diff contract](docs/v1.2-file-diff.md)
- [patch preview contract](docs/v1.2-file-patch-preview.md)
- [patch apply contract](docs/v1.2-file-patch-apply.md)
- [review workflow](docs/v1.2-review-workflow.md)
- [review hardening](docs/v1.2-review-hardening.md)

## Installed commands

Common local commands:

```text
codicks-lite doctor-local
codicks-lite agent-config
codicks-lite status
codicks-lite lock
codicks-lite read --otp <OTP> --for <minutes>
codicks-lite full --otp <OTP> --for <minutes>
codicks-lite version
codicks-lite releases
codicks-lite rollback
```

`codicks-lite doctor` remains the tunnel/deployment doctor. `codicks-lite doctor-local` checks the local install and reports optional Apple `container` availability without requiring it.

## Project layout

- `src/LocalAgent.Host` — stdio MCP host/tool endpoints
- `src/LocalAgent.Core` — configuration, policies, and service contracts
- `src/LocalAgent.Infrastructure` — file operations, review/patch engine, recovery, execution, audit, and session state
- `tests/` — unit and integration tests
- `installers/` — local macOS release builder and installer

## Release history

- **v1.2.1 — 2026-09-25:** reviewed file changes and release hardening
- **v1.2.0 — 2026-09-24:** controlled `process_exec` and optional experimental Apple-container Sandbox execution
- **v1.1.1 — 2026-09-24:** local release builder/installer and bug fixes
- **v1.1.0 — 2026-09-24:** local OTP session locking/security controls
- **v1.0.0 — 2026-09-23:** initial release

### v1.2.1

- added deterministic `file_diff` review for complete replacement content
- added strict one-file unified-patch `file_patch_preview`
- added short-lived memory-only review-token binding
- added `file_patch_apply` using the existing backup/atomic update pipeline
- added client-neutral review summaries + structured hunks
- added metadata-only review auditing and Chunk 09 hardening coverage
- preserved existing `file_update`, recovery, session, execution, and path protections

### v1.2.0: Process executions and [EXPERIMENTAL] Sandbox executions
  - added 2 execution mcp-tools: host and sandbox process_exec
  - unimplemented file diff and review functionality

### v1.1.1: Added installer compiler and Bug Fix
  - added installer compiler for easy distribution
  - fixed chatgpt mcp connector creation error: security_status returning an anonymous object
  - fixed cli security current status null

### v1.1.0: MCP security status and session locking
  - added session locking to MCP server via expiration and otp
  - added manual session status and permission controls

### v1.0.0: Initial release
  - project initialization

## Related project

Also see the Rust implementation: [Codicks MCP Harness](https://github.com/Bryan-Monterozo/codicks-mcp-harness).
