# Security

Md2OneNote treats every Markdown file as untrusted input: the file may have come from anywhere.
The add-in therefore

- never passes raw HTML through to OneNote (HTML blocks are dropped and reported, inline tags
  become plain text),
- embeds only images that live next to the Markdown file; absolute paths, `..` traversal,
  network shares and remote URLs are refused,
- renders diagrams in a WebView2 with every network request blocked and no access to local files,
- bounds file size, page size, image size, diagram count and diagram time so a hostile file
  fails cleanly,
- makes no network connection of any kind, and has no telemetry or update check.

The full model is in `docs/REQUIREMENTS.md`, section 4.2.

## Reporting a vulnerability

If you find a way for a Markdown file to run code, read a file, reach the network, or damage a
notebook, please do not open a public issue. Use GitHub's private vulnerability reporting on this
repository (Security tab, "Report a vulnerability"). Include the Markdown that triggers it and the
add-in version.

You will get an acknowledgement within a week. Fixes ship as a new release with a note in
`CHANGELOG.md`; credit is given if you want it.

## Scope

In scope: the add-in and its installer. Out of scope: OneNote itself, the WebView2 runtime, and
the third-party libraries listed in `THIRD-PARTY-NOTICES.md` (report those upstream, and tell us
so the dependency can be updated).
