# Md2OneNote — Requirements

Companion to `IMPLEMENTATION.md`. That document specifies *how*; this one specifies *what* and
*for whom*, and is the authority where the two disagree. Conflicts are listed in §7.

Status: draft from requirements discovery, 2026-08-03. Open questions in §8 are unresolved.

---

## 1. Product definition

A COM add-in for OneNote desktop on Windows that converts Markdown files into natively styled
OneNote pages, including rendered diagrams, entirely offline.

**Primary user:** someone who writes notes or documentation in Markdown and wants them to live in
OneNote as first-class pages — searchable, taggable, and editable in OneNote — rather than as
pasted HTML or attached files.

**Distribution:** public release. The tool must work on machines the author cannot inspect, install
without elevation, and never damage the user's notebook or OneNote installation.

**Core value:** the imported page is indistinguishable from one a person typed in OneNote. The
moment output looks like "pasted from a browser," the product has failed its purpose.

---

## 2. Usage model — decided

These four decisions were made during discovery and constrain everything below.

| Decision | Value | Consequence |
|---|---|---|
| Source of truth | **Import once; OneNote owns the page afterward** | The add-in never overwrites a page it created. See §3.3. |
| Corpus | **Ad-hoc individual files** | No vault, no wiki-links, no folder→section mapping, no watch folder. |
| Audience | **Broad / public** | Signing, version matrix, clean uninstall, and self-healing failure modes become requirements, not polish. |
| Invocation | **Ribbon button only** | No CLI, no automation surface. Import runs inside `ONENOTE.EXE`. |

---

## 3. Functional requirements

### 3.1 Import

- **FR-1** The user selects one or more `.md` files via a file picker from a OneNote ribbon button.
- **FR-2** Each selected file produces exactly one OneNote page in the currently active section.
- **FR-3** Files are processed sequentially; a failure on one file does not abort the others.
- **FR-4** On completion the user sees a summary: created / skipped / failed, with a per-file reason
  for every non-created file.
- **FR-5** A long import is cancellable. Cancelling stops before the next file; pages already
  created stay.
- **FR-6** If no section is active, or the active section is read-only or unavailable, the import
  refuses to start and says why. It does not create pages in an arbitrary fallback location.

### 3.2 Fidelity

- **FR-7** CommonMark plus GFM: tables, task lists, strikethrough, autolinks, footnotes.
- **FR-8** Headings, paragraphs, quotes, and lists use OneNote's own paragraph styles, so that
  OneNote's outline view, style picker, and search behave as they do for hand-authored content.
- **FR-9** Code blocks are visually distinct, monospaced, preserve indentation, and are
  syntax-highlighted where the language is known.
- **FR-10** Local images referenced relative to the source file are embedded in the page. A missing
  or unreadable image yields a visible placeholder plus a reported warning — never a silent gap.
- **FR-11** Mermaid diagrams render to embedded images, legible at OneNote's default zoom on a
  HiDPI display.
- **FR-12** Additional JS-renderable formats (Graphviz, Vega-Lite, Chart.js, KaTeX) render through
  the same mechanism.
- **FR-13** A fenced block whose language has no renderer falls back to a plain code block. Unknown
  content is never dropped and never throws.
- **FR-14** A diagram that fails to render emits the original source as a code block plus the error
  text. The import still completes.
- **FR-15** The page title comes from front-matter `title`, else a leading H1, else the filename.
- **FR-16** Links to `http(s)` targets are live hyperlinks. Links to other local files are preserved
  as text or file links (see OQ-4) — the add-in does not attempt to resolve them to OneNote pages.

### 3.3 Re-import — replaces IMPLEMENTATION.md §9.2

The add-in must never destroy user work. Because OneNote owns the page after import, an imported
page may have been edited, moved, or annotated, and the add-in cannot distinguish those edits from
its own output reliably enough to overwrite safely.

- **FR-17** Every created page records the source path, a SHA-256 of the source file, and the tool
  version, as page metadata.
- **FR-18** On import, the add-in searches **the active section only** for a page whose recorded
  source path matches the file being imported.
- **FR-19** Behaviour matrix:

  | Match in active section | Hash | Action |
  |---|---|---|
  | none | — | create a new page |
  | found | identical | **skip**; report "unchanged" |
  | found | differs | **create a new page**; leave the existing one untouched |

- **FR-20** A page created by FR-19 row 3 is visibly distinguishable from the page it supersedes
  (see OQ-1 for the naming convention).
- **FR-21** No *unattended* code path modifies a page the add-in did not create in the same
  operation. File import in particular never touches an existing page: overwriting a page as a side
  effect of re-import is out of scope entirely.
  - **FR-21a** The sole exception is an explicit, user-initiated paste command
    (`REQUIREMENTS-PASTE.md`), where mutating the current page *is* the user's stated intent. That
    path never runs without a direct user action and is guarded by optimistic concurrency (FR-P10).

This is strictly simpler than the overwrite design: no page lookup-and-rewrite path, no ID
preservation, no concurrency handling against a live page. It trades duplicate pages for
never losing data — the correct trade for a tool distributed to strangers.

---

## 4. Non-functional requirements

### 4.1 Safety

- **NFR-1** The add-in must never leave a page partially written. Page content is submitted in one
  transaction; a failure mid-build creates no page at all.
- **NFR-2** A crash, exception, or hang in the add-in must not crash OneNote or lose unsaved user
  content.
- **NFR-3** If OneNote disables the add-in after a startup failure, the product provides the user a
  supported way back — either self-detection and repair, or a documented one-click fix. "Edit the
  Resiliency registry key" is not acceptable guidance for a public audience.

### 4.2 Security — new; public distribution creates these

Users will open Markdown files they did not write. Every input is untrusted.

- **NFR-4** Raw HTML embedded in Markdown is sanitized or dropped. It is never passed through into
  page content in a form that can execute, fetch a remote resource, or forge OneNote UI.
- **NFR-5** The diagram renderer has no network access. Every asset is embedded in the binary; no
  CDN, no remote fonts, no telemetry. A machine with no internet produces identical output.
- **NFR-6** Diagram source is treated as untrusted script input and cannot read local files, reach
  the host, or persist state between renders.
- **NFR-7** Image embedding resolves paths relative to the source file and refuses absolute or
  traversing paths that escape it, so a malicious `.md` cannot cause an arbitrary local file to be
  embedded into a page the user may later share.
- **NFR-8** A hostile or pathological input (enormous image, diagram that never terminates, deeply
  nested structure) fails within a bounded time and is reported, rather than hanging OneNote.

### 4.3 Compatibility

- **NFR-9** A published support matrix of OneNote builds, tested before release. At minimum the
  M365 desktop app and the 2016/2019/2021/2024 lineage, in both 32- and 64-bit.
- **NFR-10** Installs and runs per-user without administrator rights.
- **NFR-11** Behaves correctly on a non-English OneNote UI. No user-visible string, style lookup, or
  ribbon binding may depend on English.
- **NFR-12** If the WebView2 runtime is absent, the product detects it, states it plainly, and
  continues to import everything except diagrams. Diagram support degrading is acceptable; a failed
  install or a silent crash is not.

### 4.4 Distribution

- **NFR-13** Binaries and installer are Authenticode-signed. **This is a hard prerequisite for
  public release** — an unsigned COM DLL registering itself into Office draws SmartScreen and AV
  intervention and will not survive contact with real users. Certificate procurement is a
  dependency, not a task; it must be resolved before Phase 5 (see OQ-2).
- **NFR-14** Uninstall removes every registry key, CLSID entry, and file the install created, and is
  verified on a clean machine.
- **NFR-15** Third-party components (Markdig, Mermaid, KaTeX, Graphviz/wasm, Vega-Lite, Chart.js,
  ColorCode, WebView2) are inventoried with licences and attributed in the shipped package.
- **NFR-16** "Fully offline" is a stated product property and must remain literally true. Any
  update check is manual or opt-in, never automatic and never on by default.
- **NFR-17** A user can produce a diagnostic bundle — log file plus version and environment stamp —
  without a developer walking them through it.

### 4.5 Performance

- **NFR-18** A typical README-sized document (under 200 lines, no diagrams) imports in under two
  seconds.
- **NFR-19** A document with 20 diagrams completes without appearing hung; progress is visible
  throughout. Target under 30 seconds. *(Unvalidated — see OQ-5.)*
- **NFR-20** Repeated identical diagram source within a single import session is rendered once.

### 4.6 Testability

- **NFR-21** Markdown→OneNote-XML conversion is pure, headless, and covered by golden-file tests. No
  part of it may require OneNote, WebView2, or a UI thread to execute.
- **NFR-22** The import pipeline is exercisable end-to-end without OneNote hosting the code, so that
  regressions across the NFR-9 version matrix are diagnosable. *(This is a test requirement, not a
  shipped CLI — invocation stays ribbon-only per §2.)*

---

## 5. User stories

- **US-1** As a note-taker, I pick a Markdown file from the ribbon and get a OneNote page that looks
  like I typed it, so I can keep writing in whichever tool suits the moment.
  *Accept:* headings appear in OneNote's outline view; the style picker shows real named styles;
  OneNote search finds body text.

- **US-2** As a documentation author, I import a file containing tables, code, and images and find
  all three rendered correctly, so I don't hand-rebuild them.
  *Accept:* a 6-column table keeps its header row; a 40-line code block preserves indentation and
  highlighting; two local PNGs are embedded at readable size.

- **US-3** As an architect, I import a file with Mermaid diagrams and see them as crisp images.
  *Accept:* flowchart, sequence, class, state, and Gantt diagrams are legible at 100% zoom on a
  150% -scaled display.

- **US-4** As a user re-importing a file I already brought in and then edited in OneNote, my edits
  survive.
  *Accept:* the original page is byte-identical after re-import; unchanged files are skipped and
  reported; changed files produce a clearly-labelled second page.

- **US-5** As someone importing 15 files at once, I see progress, can cancel, and get a summary
  telling me exactly what happened to each file.
  *Accept:* per-file outcome is reported; a failure on file 7 does not prevent files 8–15.

- **US-6** As a user on an unfamiliar machine, installing takes no admin rights, triggers no
  security warning, and uninstalling leaves nothing behind.
  *Accept:* clean-VM install/uninstall with a registry and filesystem diff showing no residue.

- **US-7** As a user who opened a Markdown file from the internet, importing it cannot execute
  anything or reach the network.
  *Accept:* a corpus of hostile `.md` fixtures (embedded HTML, script, remote images, path
  traversal, diagram bombs) imports safely or fails cleanly, with no outbound connection observed.

---

## 6. Explicitly out of scope

Confirmed by §2, beyond the exclusions already in `IMPLEMENTATION.md` §1:

- Wiki-links (`[[…]]`), Obsidian/Logseq/Notion-flavoured syntax
- Folder hierarchy → notebook/section-group mapping; recursive folder import
- Resolving Markdown-to-Markdown links into OneNote page links
- Watch-folder or continuous sync
- CLI, scripting, or automation surface
- Editing OneNote pages the add-in did not just create, *except* the explicit paste command
  (`REQUIREMENTS-PASTE.md`); any merge or conflict resolution remains out of scope
- Splitting one Markdown file across multiple OneNote pages or subpages

---

## 7. Conflicts with IMPLEMENTATION.md

Resolve in favour of this document.

| Location | Conflict | Resolution |
|---|---|---|
| §9.2 | "match, hash differs → overwrite that page (keep its ID and title)" | Contradicts FR-19/FR-21. Never overwrite; create a new page. Simplifies the interop surface. |
| §6 | Interop list assumes rewriting existing pages | `UpdatePageContent` is still needed for the page just created, but no lookup-and-rewrite-existing path is required. |
| §11 Phase 5 | "Inno Setup installer" as one checkbox | Public release makes distribution its own phase: signing, version matrix, clean uninstall, WebView2 absence, licences. §4.4 is not a checkbox's worth of work. |
| §11 Phase 2 accept | "visually indistinguishable from hand-authored OneNote content" | Not testable as written. Replace with golden-file XML assertions plus the concrete checks in US-1. |
| §13 | "Check Resiliency\DisabledItems and clear it during development" | Adequate for the author, not for public users. NFR-3 requires a supported recovery path. |
| §1 in-scope | "Fully offline — no network calls required for the core path" | Strengthen: offline is a *guarantee*, not a default. NFR-5/NFR-16. |
| §7 mapping | Footnotes appended under an H2 "Notes" | Hardcoded English string; NFR-11. |
| §2 | WebView2 "preinstalled on Windows 10/11" | True for most, not all — Win10 LTSC, Server, stripped enterprise images. NFR-12 required. |
| — | No security section | Untrusted input was not a consideration for a personal tool; it is for a public one. §4.2 is new. |

---

## 8. Open questions

- **OQ-1 — Superseding-page naming.** When a changed file is re-imported (FR-19 row 3), what
  distinguishes the new page? Suggested: keep the title and let OneNote's page list show the date;
  or suffix with the import timestamp. Should the new page link back to the one it supersedes?
- **OQ-2 — Code signing.** Which certificate, and who pays? An OV cert is the cheap path but still
  accrues SmartScreen reputation slowly; EV clears it immediately at higher cost. **Blocks public
  release; decide before building the installer, not after.**
- **OQ-3 — Support matrix breadth.** How far back does NFR-9 actually reach? Testing 2016 through
  2024 across both bitnesses is a meaningful ongoing cost. Is dropping 2016 acceptable?
- **OQ-4 — Local file links.** `[see](./design.md)` — render as plain text, as a `file://`
  hyperlink, or drop the link and keep the text? A `file://` link works only on the importing
  machine and breaks when the page is shared.
- **OQ-5 — Diagram performance.** NFR-19's 30-second target is a guess. Needs measurement in
  Phase 4; adjust once real numbers exist.
- **OQ-6 — Front-matter tags.** §7 says tags map to OneNote tags "if trivial; otherwise ignore."
  Decide: map to OneNote tags, render as a visible line, or ignore entirely in v1.
- **OQ-7 — Duplicate detection scope.** FR-18 scopes the search to the active section. If the user
  imports the same file into a different section, that is a new page. Confirm this is intended.
- **OQ-8 — Page title collisions.** Two different files with the same title in one section — allow
  the collision, or disambiguate?
- **OQ-9 — Size ceiling.** Is there a file size or diagram count above which the add-in should
  refuse rather than try? Relates to NFR-8.

---

## 9. Next step

Requirements only — no architecture decided here. Recommended order:

1. Resolve **OQ-2** (signing). It is procurement with a lead time and it gates release.
2. Run **Phase 0** from `IMPLEMENTATION.md` unchanged — the schema spike is still the right first
   move and nothing above alters it.
3. Then `/sc:design` for the diagram-renderer and metadata design, or `/sc:workflow` to plan the
   phases against these requirements.
