# UdonSharp Linter for Visual Studio Code

[![Open in Visual Studio Code](https://img.shields.io/static/v1?logo=visualstudiocode&label=&message=Open%20in%20VS%20Code&labelColor=2c2c32&color=007acc&logoColor=007acc)](https://open.vscode.dev/Project-Sakanah/vscode-udonsharp-linter)
[![Marketplace Version](https://vsmarketplacebadges.dev/version-short/refiaa.vscode-udonsharp-linter.svg)](https://marketplace.visualstudio.com/items?itemName=refiaa.vscode-udonsharp-linter)
[![Downloads](https://vsmarketplacebadges.dev/downloads-short/refiaa.vscode-udonsharp-linter.svg)](https://marketplace.visualstudio.com/items?itemName=refiaa.vscode-udonsharp-linter)
[![Installs](https://vsmarketplacebadges.dev/installs-short/refiaa.vscode-udonsharp-linter.svg)](https://marketplace.visualstudio.com/items?itemName=refiaa.vscode-udonsharp-linter)
[![Rating](https://vsmarketplacebadges.dev/rating-short/refiaa.vscode-udonsharp-linter.svg)](https://marketplace.visualstudio.com/items?itemName=refiaa.vscode-udonsharp-linter)


UdonSharp Linter brings static analysis for UdonSharp behaviour scripts directly into Visual Studio Code. The extension ships a cross-platform Roslyn-based language server that mirrors the diagnostics produced by the official UdonSharp compiler and VRChat policy packs, while adding responsive editor integrations that work even when Unity or VRChat assemblies are unavailable.

## Feature Highlights

- **Real-time diagnostics**: Roslyn analyzers surface USH0001–USH0045 findings as you type, including network event rules, runtime restrictions, API exposure limits, and language feature bans.
- **Offline-aware analysis**: Syntax-only fallbacks detect missing custom events, invalid network signatures, and unsupported APIs without Unity or VRChat stub assemblies.
- **Rule intelligence**: Search the full rule catalogue, inspect localized documentation, and review per-profile severities without leaving VS Code.
- **Status bar telemetry**: A shield badge shows the active profile, disabled rule counts, and live server health. Click it to swap profiles or open rule search.
- **Code action foundation**: The server advertises rule metadata describing which diagnostics can surface quick fixes, enabling incremental rollout of Roslyn code fixes.
- **Self-contained server runtime**: Bundled .NET 8 binaries for Linux (x64/arm64), macOS (Intel/Apple Silicon), and Windows (x64/arm64) require no user-managed runtime.



## Rule Coverage Overview

| Category          | Rule IDs                        | Highlights                                                                                 |
|-------------------|---------------------------------|---------------------------------------------------------------------------------------------|
| Network events    | USH0001–USH0006, USH0043        | Missing targets, non-public handlers, invalid naming, `[NetworkCallable]`, payload typing, sync mode gating, `nameof` guidance |
| Synchronization   | USH0007–USH0012                 | Sync mode compatibility, tweening restrictions, `UdonSynced` type validation                |
| Udon API surface  | USH0013–USH0015                 | Blocks access to forbidden Unity/VRChat APIs, members, and types (e.g., `GetComponent`, `Process`) |
| Runtime limits    | USH0016–USH0021                 | `Instantiate` guardrails, unsupported `is/as`, exception handling, VRChat player event signatures |
| Language features | USH0022–USH0039                 | Nullable value types, null-conditionals, multidimensional arrays, local functions, generics, static members, `goto`, etc. |
| Attributes        | USH0040–USH0042                 | `FieldChangeCallback` target validation and duplicate detection                             |
| Structure         | USH0044–USH0045                 | Namespace requirement and class/file name alignment                                         |

Each rule is represented by a Roslyn `DiagnosticDescriptor` and surfaced through the Language Server Protocol. Rule documentation is packaged in JSON policy packs and rendered via VS Code’s markdown engine.



## Commands and UI

| Command Palette Entry | Identifier                           | Description                                                                 |
|-----------------------|--------------------------------------|-----------------------------------------------------------------------------|
| UdonSharp Linter: Switch Profile | `udonsharpLinter.switchProfile` | Pick from bundled or custom policy packs and immediately reload severities. |
| UdonSharp Linter: Search Rules…  | `udonsharpLinter.searchRules`  | Filter the rule catalogue by ID, title, or category and open documentation. |
| UdonSharp Linter: Open Rule Documentation | `udonsharpLinter.openRuleDocs` | Display localized markdown guidance for a specific rule ID.                 |

The left-aligned status bar item mirrors the active profile and disabled rule count, and links back to the profile picker for fast changes.



## Configuration Reference

All settings live under the `udonsharpLinter` namespace.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `profile` | string (`latest` \| `legacy_0.x` \| `strict_experimental`) | `latest` | Selects the bundled policy pack that maps to VRChat/UdonSharp release trains. |
| `rules` | object | `{}` | Override individual rule severities (`error`, `warn`, `info`, `off`). Keys are normalized to upper-case rule IDs. |
| `unityApiSurface` | string (`bundled-stubs` \| `custom-stubs` \| `none`) | `bundled-stubs` | Controls the reference assemblies the Roslyn server loads. Use `none` for syntax-only mode. |
| `customStubPath` | string | `""` | Absolute or workspace-relative path to custom stub assemblies when `unityApiSurface` is `custom-stubs`. |
| `allow.refOut` | boolean | `false` | Treat `ref`/`out`/`in` parameters as allowed in the current workspace. |
| `codeActions.enable` | boolean | `true` | Turns Roslyn code fixes on or off when available. |
| `telemetry` | string (`off` \| `minimal`) | `minimal` | Disables telemetry entirely or limits it to anonymized usage counts. |
| `policyPackPaths` | string[] | `[]` | Additional policy pack JSON files to merge with the bundled catalogue. Paths are resolved relative to the workspace root. |

Additional environment variables:

- `UDONSHARP_LINTER_SERVER_PATH`: Override the bundled server executable (useful when iterating on the .NET project).
- `UDONSHARP_LINTER_TELEMETRY`: Set to `0` to force-disable telemetry inside the server process.



## Installation and Requirements

### Supported Editor and Platforms
- Visual Studio Code **1.105.0** or later.
- Operating systems: Windows 10/11 (x64, arm64), macOS 12+ (Intel or Apple Silicon), Ubuntu 20.04+/Debian 11+/other glibc-based Linux (x64 or arm64).
- No separate .NET runtime is required; the extension ships self-contained binaries published from the `net8.0` server project.

### From the Marketplace (NOT YET SUPPORTED)
1. Open the Extensions view in VS Code.
2. Search for “UdonSharp Linter”.
3. Install and reload VS Code when prompted.

### From Source
1. Clone this repository.
2. Run `npm install` (or `npm ci`) to restore client dependencies.
3. Execute `npm run compile` to build the TypeScript client.
4. Publish the server using the provided scripts (`DOTNET_CONFIGURATION=Release ./build_and_publish.sh` on macOS/Linux or `DOTNET_CONFIGURATION=Release ./build_and_publish.ps1` in Windows PowerShell). This populates `resources/server/<RID>/` with platform-specific binaries.
5. Package the extension with `npx vsce package` or test a development build via `code --extensionDevelopmentPath=/path/to/repo`.



## Quick Start

The project root includes `build_and_publish.sh` (bash) and `build_and_publish.ps1` (PowerShell) so you can publish the self-contained language server for your platform with a single command.

## Policy Packs and Rule Documentation

Policy packs are JSON documents under `server/PolicyPacks` that describe rule metadata, default severities, per-profile overrides, and localized documentation. At runtime:

1. `PolicyPackLoader` scans the bundled directory and any extra paths from `policyPackPaths`.
2. The merged catalogue populates `PolicyRepository`, which feeds severities to the Roslyn analyzers.
3. Rule documentation is exposed through the JSON-RPC method `udonsharp/rules/documentation` and rendered inside a VS Code webview with Content Security Policy restrictions.

You can author custom packs to experiment with new diagnostics or override messaging in localized deployments.



## Logging and Diagnostics

- Server logs are stored beside the platform executable in `resources/server/<RID>/logs/` (`server.log`, `boot.log`, `fatal.log`).
- Use the VS Code Output panel (`UdonSharp Linter` channel) to inspect client-side lifecycle events.
- Set `UDONSHARP_LINTER_SERVER_PATH` to a local `dotnet run` output to attach a debugger during server development.



## Development Workflow

### Prerequisites
- Node.js 22.x and npm (for the VS Code client).
- TypeScript 5.9, ESLint 9 (installed via npm).
- .NET 8 SDK with the `dotnet` CLI on your PATH.
- VSCE (`npm install -g @vscode/vsce`) for packaging, or `@vscode/test-cli` for tests.

### Common Tasks
| Task | Command |
|------|---------|
| Compile client | `npm run compile` |
| Type-check client | `npm run check-types` |
| Lint client | `npm run lint` |
| Watch mode | `npm run watch` (runs esbuild + TypeScript) |
| Run extension tests | `npm run test` |
| Publish server binaries | `./build_and_publish.sh` (macOS/Linux) or `./build_and_publish.ps1` (Windows); optionally set `DOTNET_RIDS="linux-x64 win-x64"` to limit targets |
| Package extension | `npm run package` (alias for `npm run compile` + `esbuild --production`) followed by `npx vsce package` |

The publish script builds self-contained binaries for Windows, macOS, and Linux by default and copies them into `resources/server/<RID>/`. Ensure these assets are committed before tagging a release.



## Troubleshooting

| Symptom | Suggested Action |
|---------|------------------|
| Status bar shows “UdonSharp: server offline” | Check `server.log` under `resources/server/<RID>/logs/` and verify antivirus software is not blocking the bundled binary. |
| Diagnostics do not appear | Confirm the script derives from `UdonSharpBehaviour`. The server skips standard C# files to reduce noise. |
| Custom policy packs ignored | Ensure the JSON file includes a root `rules` array with `id`, and that the path in `policyPackPaths` is absolute or workspace-relative. |
| Using custom Unity stubs | Set `unityApiSurface` to `custom-stubs` and provide `customStubPath`. The server will load all `.dll` files in that directory as metadata references. |
| Developing the server | Export `UDONSHARP_LINTER_SERVER_PATH=/absolute/path/to/udonsharp-lsp` and run `dotnet publish` separately to test without repacking the extension. |


## Contributing

Contributions are welcome. Please open issues for bugs or rule gaps before submitting pull requests. When contributing:

1. Run `npm run lint`, `npm run check-types`, and `dotnet build server/UdonSharpLsp.Server.csproj` before committing.
2. Include regression scenarios or rule documentation updates when expanding analyzer coverage.
3. Update `CHANGELOG.md` and this README whenever user-facing behaviour changes.

The project is licensed under the [MIT License](LICENSE).



## Support

- File bugs and feature requests via GitHub Issues.
- For VRChat policy questions, consult the official UdonSharp documentation.
- Commercial support is not provided; please fork and adapt for bespoke moderation flows.

Thank you for using UdonSharp Linter to keep your VRChat worlds reliable and compliant.
