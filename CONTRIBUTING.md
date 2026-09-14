# Contributing

Issues and pull requests are welcome. Small, focused changes with a test are the easiest to
review and merge.

## Building

Requirements: Windows, the .NET SDK (any recent version; the projects target .NET Framework 4.8),
and OneNote for Windows to try the result.

```powershell
dotnet build
dotnet test
```

The tests do not need OneNote or WebView2; the conversion pipeline is pure and covered by golden
files under `tests/Md2OneNote.Core.Tests/Golden`. When a golden file changes, say in the pull
request why the new output is right.

## Running it in OneNote

```powershell
# OneNote must be closed. Builds, copies the output to %LOCALAPPDATA%\Md2OneNote\bin,
# and registers the add-in for the current user (no administrator rights).
.\tools\register.ps1 -Install

# Removes every registry key the install created.
.\tools\register.ps1 -Uninstall
```

OneNote hosts COM add-ins in a `dllhost.exe` surrogate, which keeps the DLL open for a few
seconds after OneNote exits; the script asks you to retry rather than deploying over it.
The add-in logs to `%LOCALAPPDATA%\Md2OneNote\log.txt`; that file is the first place to look
when something does not happen.

Ad-hoc scripts that load the add-in assemblies must run under Windows PowerShell 5.1
(`powershell.exe`), not PowerShell 7: the WebView2 loader is not found from `pwsh`.

## Where things are

| Project | Holds |
|---|---|
| `Md2OneNote.Core` | Markdown parsing, the OneNote XML renderer, asset policy. Pure, no I/O. |
| `Md2OneNote.Application` | The import use case: decide, convert, write, summarise. |
| `Md2OneNote.Interop` | The OneNote `Application` COM surface, late-bound. |
| `Md2OneNote.Storage` | File system access. |
| `Md2OneNote.Diagrams` | Mermaid rendering in a hidden WebView2. |
| `Md2OneNote.AddIn` | The COM add-in: ribbon, dialogs, logging, composition root. |

`docs/DESIGN.md` explains why it is cut this way; `docs/IMPLEMENTATION.md` records the OneNote
specifics (registry keys, XML schema, pitfalls); `docs/page-schema-notes.md` is what OneNote
actually writes, dumped from real pages, and wins over anything else when they disagree.

## Conventions

- C# 7.3, warnings are errors.
- Comments explain why, not what; a OneNote quirk that cost a day gets a sentence and a date.
- User-visible text goes through the string catalog, never inline, so a translation is possible.
- No settings dialog. Sensible fixed defaults instead of options.
- Commit messages: `type(scope): imperative summary`, e.g. `fix(core): size diagrams by capture scale`.
