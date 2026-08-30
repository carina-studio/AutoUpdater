# AutoUpdater Solution

A cross-platform desktop auto-updater application built on Avalonia UI and the AppBase framework. Distributed as self-contained binaries for Windows, macOS, and Linux.

## Solution Structure

| Project | Purpose |
|---|---|
| `AutoUpdater` | Core library — update logic, view models, session management |
| `AutoUpdater.Avalonia` | GUI entry point — Avalonia app shell, resources, localization |

`PackagingTool.cs` is a file-based C# app at the repository root — not a project, and not part of `AutoUpdater.sln`. It builds package manifests and reports versions, and the build scripts invoke it as `dotnet run PackagingTool.cs -- <command>`.

## Build & Packaging

- **Target framework**: `net10.0` (all projects)
- **Language**: C# with nullable reference types enabled everywhere
- **Distribution**: self-contained, trimmed (partial trim mode), multi-architecture
- **Supported RIDs**: `win-x86`, `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`
- **Output**: `Packages/<VERSION>/` directories produced by platform build scripts
- **macOS window style**: before signing, `BuildMacOSPackages.sh` uses `vtool` (requires Xcode) to rewrite the linked SDK version of the application binary to `26.0`, which opts the app in to the window design of macOS 26+

**Build scripts:**

| Script | Purpose |
|---|---|
| `BuildMacOSPackages.sh` | Build + package for macOS (osx-x64, osx-arm64) |
| `BuildWindowsPackages.bat` | Build + package for Windows (win-x86/x64/arm64) |
| `BuildLinuxPackages.sh/.bat` | Build + package for Linux (linux-x64, linux-arm64) |
| `GeneratePackageManifest.sh/.bat` | Generate `PackageManifest-Avalonia.json` |
| `NotarizeMacOSPackages.sh` | Code-sign and notarize macOS bundles |

The `.sh` scripts archive with `ditto`, so they run on macOS — including `BuildLinuxPackages.sh`, which cross-builds the Linux packages from macOS.

## Workflow

When solving a bug or adding a feature, **always present a plan first** and wait for explicit user approval before making any code changes.

After a code change is confirmed, check whether the change affects the architecture or project structure. If so, ask the user whether to update `AGENTS.md`, and update it only upon confirmation.

### Commits

**Never commit on your own** — do not run `git add`, `git commit`, or any other commit-creating command until the user has explicitly asked you to commit (e.g. "commit", "commit it", "create a commit"). Approval to apply a code change is **not** approval to commit it; the user decides when (and whether) the change becomes a commit. Two forms of that mistake are worth naming, because both read as approval and neither is:

- **An instruction which approves the work is not a commit instruction.** "go", "go ahead", and an instruction which simply names the change to make — "update doc", "delete the dead code" — approve the editing and nothing else.
- **A commit instruction never carries forward.** Work split into several parts needs its own instruction for each part, even when the part immediately before it was committed on request. The approval covered that part, not the sequence.

End a part by saying the changes are ready and what they are, then wait. The same rule applies to `git push`, branch creation, and any other shared-state action — wait for an explicit instruction.

**Write the commit message in the house style** — a single line, sentence case, ending with a period, saying what the change does: `Fix code ordering.`, `Apply macOS 26 window style.` A longer single line is fine when the change needs one: `Update package manifest to 2.2.3.424.` When one commit genuinely carries several independent parts, number them — **one part per line, each its own sentence ending with a period, with no blank line between them**, as `3b7a7d6` does:

    [1] Upgrade to AppBase 2.3.3.722.
    [2] Upgrade to Avalonia 11.3.20.
    [3] Upgrade to Avalonia XAML Behaviors 11.3.0.6.
    [4] Upgrade to NLog 6.2.0.
    [5] Upgrade to .NET libraries 10.0.11.

Do **not** run the parts together on one line. Beware that `git log --oneline` gives no evidence either way: git reads everything up to the first blank line as the subject, so it joins the numbered lines back into one and both spellings look identical there — check with `git log --format=%B` instead. **No body, no trailers, no emoji — and never a `Co-Authored-By` trailer**, whatever the agent's own default guidance says. The repository has none, and adding one to the handful of commits an agent touched makes the history inconsistent for no benefit.

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
- Supported cultures: `Default` (English), `ja-JP`, `zh-CN`, `zh-TW`.
- All user-visible strings must have entries in all four resource files.

### Platform-Specific Code

- macOS-specific code may use `CarinaStudio.MacOS.*` (AppKit, CoreGraphics) via P/Invoke — no `CA1416` suppression needed for these custom bindings.
- Suppress `CA1416` only when calling .NET runtime APIs annotated with `[SupportedOSPlatform]`.
