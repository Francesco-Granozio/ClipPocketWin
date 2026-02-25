# AGENTS.md

## Purpose
- This file gives coding agents a practical, repo-specific playbook.
- Follow it for build, lint, test, and code-style decisions.
- Prefer small, focused changes that match existing conventions.

## Repository Snapshot
- Solution: `ClipPocketWin.slnx`
- App project: `ClipPocketWin/ClipPocketWin.csproj`
- Tech stack: C# + WinUI 3 + XAML
- Target framework: `net8.0-windows10.0.19041.0`
- Minimum Windows version: `10.0.17763.0`
- Runtime identifiers: `win-x86`, `win-x64`, `win-arm64`
- Nullable reference types are enabled (`<Nullable>enable</Nullable>`).
- Current repo has no test project yet.

## Build / Restore / Run Commands

### Restore
```bash
dotnet restore "ClipPocketWin.slnx"
```

### Build (default config/platform)
```bash
dotnet build "ClipPocketWin.slnx"
```

### Build (explicit Release x64)
```bash
dotnet build "ClipPocketWin.slnx" -c Release -p:Platform=x64
```

### Build single project
```bash
dotnet build "ClipPocketWin/ClipPocketWin.csproj" -c Debug -p:Platform=x64
```

### Run app
```bash
dotnet run --project "ClipPocketWin/ClipPocketWin.csproj" -p:Platform=x64
```

### Publish (example)
```bash
dotnet publish "ClipPocketWin/ClipPocketWin.csproj" -c Release -p:Platform=x64
```

## Lint / Formatting Commands

### Check formatting in CI style (no writes)
```bash
dotnet format "ClipPocketWin.slnx" --verify-no-changes
```

### Apply formatting fixes
```bash
dotnet format "ClipPocketWin.slnx"
```

Notes:
- No dedicated linter config is currently checked in (no `.editorconfig` found).
- Treat `dotnet format --verify-no-changes` as the lint gate.

## Test Commands

### Run all tests
```bash
dotnet test "ClipPocketWin.slnx"
```

### Run tests for one project
```bash
dotnet test "path/to/Your.Tests.csproj"
```

### Run a single test (fully-qualified name filter)
```bash
dotnet test "path/to/Your.Tests.csproj" --filter "FullyQualifiedName~Namespace.ClassName.TestMethodName"
```

### Run tests by class
```bash
dotnet test "path/to/Your.Tests.csproj" --filter "FullyQualifiedName~Namespace.ClassName"
```

### Run tests by method name substring
```bash
dotnet test "path/to/Your.Tests.csproj" --filter "Name~TestMethodName"
```

Notes:
- This repository currently has no test project; add one before expecting test discovery.
- If tests are added, keep them separate from UI app code (dedicated `*.Tests` project).

## Code Style - General
- Keep diffs minimal; do not refactor unrelated code opportunistically.
- Do not edit generated outputs in `bin/`, `obj/`, or IDE cache directories.
- Keep files ASCII unless a file already requires Unicode characters.
- Favor readability over cleverness.
- Remove dead code and unused usings in touched files.

## Code Style - C# Imports and File Layout
- Order usings with `System.*` first, then framework/library namespaces, then project namespaces.
- Keep using directives at the top of the file.
- Avoid global usings unless the repo adopts them consistently.
- Use one top-level type per file for non-trivial classes.
- Keep namespace and folder names aligned (`ClipPocketWin/...`).

## Code Style - C# Formatting
- Use 4 spaces for indentation; do not use tabs.
- Always use braces for control blocks, even single-line blocks.
- Keep methods short and focused; extract helpers when logic becomes dense.
- Prefer expression clarity over compact one-liners.
- Keep line lengths reasonable; wrap long argument lists cleanly.

## Code Style - C# Types and Nullability
- Respect nullable annotations; never silence nullability warnings without reason.
- Use explicit nullable types (`Type?`) when null is a valid state.
- Validate platform API assumptions before dereferencing optional objects.
- Prefer `var` when the type is obvious from the right-hand side.
- Use explicit types when clarity would otherwise suffer.

## Code Style - Naming
- Types, methods, properties, events: `PascalCase`.
- Local variables and parameters: `camelCase`.
- Private fields: prefer `_camelCase`.
- Existing legacy names (for example `m_AppWindow`) may remain unless you are already editing that area.
- Async methods must end with `Async`.
- Use intention-revealing names; avoid abbreviations that are not standard.

## Code Style - Error Handling and Reliability
- Guard early for unsupported platform features (for example `IsSupported()` checks).
- Fail fast on invalid state with clear exceptions where recovery is impossible.
- Do not swallow exceptions silently.
- If catching exceptions, either recover meaningfully or rethrow with context.
- Keep UI startup resilient: avoid introducing blocking or fragile initialization paths.

## Code Style - WinUI/XAML
- Use 4-space indentation and keep nested XAML readable.
- Keep one attribute per line for complex elements.
- Prefer shared resources in `App.xaml` for repeated brushes, spacing, and typography.
- Use `x:Name` only when code-behind access is required.
- Keep visual structure comments concise and high-signal.
- Preserve the existing visual language unless explicitly redesigning.

## Validation Checklist For Agents
- Run restore if dependencies changed.
- Run build for at least one concrete platform (`x64` recommended locally).
- Run `dotnet format --verify-no-changes` after C# edits.
- Run tests if a test project exists.
- Report exactly what you could not run and why.

## Cursor / Copilot Rule Files
- Checked paths: `.cursorrules`, `.cursor/rules/`, `.github/copilot-instructions.md`.
- Result at time of writing: no Cursor or Copilot instruction files were found.
- If any of these files are added later, treat them as higher-priority repository policy and update this document.
