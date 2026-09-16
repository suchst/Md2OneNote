# Changelog

All notable changes to Md2OneNote are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- A progress window during import: which file of how many, which step, and a Cancel button.
  Cancelling stops after the current file; pages already created are kept and listed in the
  summary.

## [1.0.0-preview.1] - 2026-09-15

First public preview. Not code-signed yet: Windows SmartScreen will warn when the installer runs.

### Added
- Import one or more Markdown files as pages in the current OneNote section, from a button on
  the Insert tab.
- Headings, paragraphs, emphasis, links, block quotes, bulleted, numbered, nested and task lists.
- Tables with a header row.
- Fenced code blocks with syntax highlighting, indentation preserved.
- Local images embedded at life size and scaled down to fit the page.
- Mermaid diagrams rendered offline to crisp images; a diagram that fails to render is kept as a
  code block with the reason under it.
- Re-import awareness: a file already imported into the section and unchanged since is skipped,
  with a one-click option to import it again as a new page. Pages already in OneNote are never
  overwritten.
- Diagrams degrade to code blocks when the WebView2 runtime is missing; the summary says so.
- An About button with the version, the environment (OneNote build, WebView2 runtime,
  Windows), links to the project, and two helpers for bug reports: open the log folder and save
  a diagnostic bundle.
- Per-user install and uninstall without administrator rights.

[Unreleased]: https://github.com/suchst/Md2OneNote/compare/v1.0.0-preview.1...HEAD
[1.0.0-preview.1]: https://github.com/suchst/Md2OneNote/releases/tag/v1.0.0-preview.1
