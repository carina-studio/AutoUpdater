# AutoUpdater Solution

A cross-platform desktop auto-updater application built on Avalonia UI and the AppBase framework. Distributed as self-contained binaries for Windows, macOS, and Linux.

## Solution Structure

| Project | Purpose |
|---|---|
| `AutoUpdater` | Core library — update logic, view models, session management |
| `AutoUpdater.Avalonia` | GUI entry point — Avalonia app shell, resources, localization |
| `PackagingTool` | Console utility for building package manifests and version management |

## Build & Packaging

- **Target framework**: `net10.0` (all projects)
- **Language**: C# with nullable reference types enabled everywhere
- **Distribution**: self-contained, trimmed (partial trim mode), multi-architecture
- **Supported RIDs**: `win-x86`, `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`
- **Output**: `Packages/<VERSION>/` directories produced by platform build scripts

**Build scripts:**

| Script | Purpose |
|---|---|
| `BuildMacOSPackages.sh` | Build + package for macOS (osx-x64, osx-arm64) |
| `BuildWindowsPackages.bat` | Build + package for Windows (win-x86/x64/arm64) |
| `BuildLinuxPackages.bat` | Build + package for Linux (linux-x64, linux-arm64) |
| `GeneratePackageManifest.sh/.bat` | Generate `PackageManifest-Avalonia.json` |
| `NotarizeMacOSPackages.sh` | Code-sign and notarize macOS bundles |

## Workflow

When solving a bug or adding a feature, **always present a plan first** and wait for explicit user approval before making any code changes.

After a code change is confirmed, check whether the change affects the architecture or project structure. If so, ask the user whether to update `CLAUDE.md`, and update it only upon confirmation.

## Code Conventions

### General

- Nullable reference types are enabled (`#nullable enable`) everywhere.
- Unsafe blocks are allowed in `AutoUpdater.Avalonia`.
- Root namespace: `CarinaStudio.AutoUpdater`.
- All public async methods return `Task` or `ValueTask`; UI-thread operations use the application's dispatcher.
- Trim-incompatible code must be guarded or annotated appropriately — the app uses partial trimming.

### File and Type Organization

- One type per file; file name matches the type name exactly.
- Each subsystem gets its own subfolder (e.g. `ViewModels/`, `Strings/`, `Resources/`).
- Namespace matches the folder path under the root namespace.
- Inner types within a class are ordered **alphabetically** by name.
- Members within a type (properties, methods, enum values) are also ordered **alphabetically**. Exception: `[StructLayout(LayoutKind.Sequential)]` fields must preserve memory-layout order.

### Interfaces and Documentation

- Every public member carries an XML doc comment (`/// <summary>`); use `/// <inheritdoc/>` in implementations.
- Extension method classes are named `XxxExtensions` and placed in their own file.

### Localization

- String resources live in `AutoUpdater.Avalonia/Strings/`.
- Supported cultures: `Default` (English), `zh-CN`, `zh-TW`.
- All user-visible strings must have entries in all three resource files.

### Platform-Specific Code

- macOS-specific code may use `CarinaStudio.MacOS.*` (AppKit, CoreGraphics) via P/Invoke — no `CA1416` suppression needed for these custom bindings.
- Suppress `CA1416` only when calling .NET runtime APIs annotated with `[SupportedOSPlatform]`.
