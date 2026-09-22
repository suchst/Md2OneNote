# Changelog

All notable changes to Md2OneNote are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- The release workflow signs the add-in's assemblies and the installer through SignPath when
  the repository holds SignPath credentials, and publishes unsigned otherwise, saying so in the
  release notes. Releases stay unsigned until SignPath Foundation approves the project.

## [1.0.0-preview.3] - 2026-09-22

Still unsigned: Windows SmartScreen will warn when the installer runs.

### Changed
- OneNote stays usable during an import. The import runs on its own thread; the ribbon button
  returns at once, and the progress window is the only thing that waits. A second Import while
  one is running is refused with a message rather than queued. Closing OneNote during an import
  stops it after the current file; the pages already made are kept.
- A busy OneNote (a dialog open there during an import) is retried for about ten seconds
  before the file is reported as failed, instead of one second.

## [1.0.0-preview.2] - 2026-09-19

Still unsigned: Windows SmartScreen will warn when the installer runs.

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

[Unreleased]: https://github.com/suchst/Md2OneNote/compare/v1.0.0-preview.3...HEAD
[1.0.0-preview.3]: https://github.com/suchst/Md2OneNote/releases/tag/v1.0.0-preview.3
[1.0.0-preview.2]: https://github.com/suchst/Md2OneNote/releases/tag/v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/suchst/Md2OneNote/releases/tag/v1.0.0-preview.1
