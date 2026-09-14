# Md2OneNote — Implementation Specification

A COM add-in for **OneNote desktop on Windows** that imports Markdown files and renders them as
natively styled OneNote pages, including Mermaid and other diagrams.

This document is the working brief. Read it fully before writing code. Phase 0 is a spike whose
output changes later phases — do not skip it.

---

## 1. Goal and scope

**Goal:** the user picks one or more `.md` files from the OneNote ribbon; the add-in creates one
OneNote page per file, in the currently active section, that looks like a hand-authored OneNote
page rather than pasted HTML.

**In scope**

- CommonMark + GFM (tables, task lists, strikethrough, autolinks, footnotes)
- Native OneNote styling via `one:QuickStyleDef` (real heading styles, not inline CSS)
- Fenced code blocks with syntax highlighting
- Mermaid diagrams rendered offline to PNG
- Additional JS-renderable diagram formats through the same rendering shell
- Local images referenced from the Markdown file
- Fully offline operation — no network calls required for the core path

**Out of scope (do not build)**

- OneNote on the web, OneNote for Mac, OneNote for Windows 10 (UWP, end of support)
- Microsoft Graph / OneNote REST API
- Office JS add-in (`OneNote.run`) — unsupported on the Windows desktop client
- Round-tripping OneNote pages back to Markdown
- PlantUML in v1 (requires Java; see §8.4 for the optional path)

---

## 2. Target environment

| Item | Value |
|---|---|
| Host | OneNote desktop on Windows (M365 / 2016 / 2021 / 2024 lineage) |
| Extensibility model | COM add-in (`IDTExtensibility2` + `IRibbonExtensibility`) |
| Object model | `Microsoft.Office.Interop.OneNote`, XML schema `xs2013` |
| Runtime | .NET Framework 4.8 |
| Platform target | **AnyCPU** — a 32-bit OneNote will not load a 64-bit add-in |
| Diagram rendering | WebView2 (evergreen runtime, preinstalled on Windows 10/11) |

Use .NET Framework 4.8, not .NET 8. COM hosting on modern .NET works but adds bitness and
activation problems that are not worth it here.

---

## 3. Tech stack

| Concern | Library |
|---|---|
| Markdown parsing | `Markdig` (use the AST, not `Markdown.ToHtml`) |
| OneNote interop | COM reference to *Microsoft OneNote 16.0 Type Library* (`ONENOTE.TLB`), `Embed Interop Types = false` |
| Diagram rendering | `Microsoft.Web.WebView2` |
| Syntax highlighting | `ColorCode-Universal`, or a small hand-rolled tokenizer |
| XML generation | `System.Xml.Linq` (`XElement` / `XNamespace`) |
| Logging | plain file logger, no framework |
| Tests | xUnit for pure units; interop paths are exercised manually |

---

## 4. Project layout

```
Md2OneNote.sln
├── src/
│   ├── Md2OneNote.AddIn/          # COM add-in, ribbon, registration
│   │   ├── AddIn.cs
│   │   ├── Ribbon.xml             # embedded resource
│   │   └── Logging/FileLogger.cs
│   ├── Md2OneNote.Core/           # Markdown → OneNote XML. No interop, no UI.
│   │   ├── Model/                 # style tokens, page model
│   │   ├── Rendering/             # OneNoteXmlRenderer + per-block writers
│   │   └── Diagrams/              # IDiagramRenderer + registry
│   └── Md2OneNote.Diagrams.WebView2/
│       ├── WebViewDiagramRenderer.cs
│       └── Assets/                # mermaid.min.js, katex, viz.js — embedded
├── tests/
│   └── Md2OneNote.Core.Tests/
├── tools/
│   └── register.ps1               # per-user registry + COM registration
└── docs/
    └── page-schema-notes.md       # output of Phase 0 — fill this in
```

`Md2OneNote.Core` must not reference the interop assembly. It takes a Markdown string plus a base
directory and returns an XML string. That boundary is what makes the renderer testable.

---

## 5. OneNote XML — domain knowledge

Namespace: `http://schemas.microsoft.com/office/onenote/2013/onenote`, conventional prefix `one`.

### 5.1 Page skeleton

```xml
<one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{pageId}">
  <one:QuickStyleDef index="0" name="PageTitle" font="Calibri Light" fontSize="20" fontColor="#1F497D"/>
  <one:QuickStyleDef index="1" name="h1" font="Calibri Light" fontSize="16" fontColor="#1E4E79"/>
  <one:QuickStyleDef index="2" name="h2" font="Calibri Light" fontSize="14" fontColor="#2E74B5"/>
  <one:QuickStyleDef index="3" name="h3" font="Calibri Light" fontSize="12" fontColor="#5B9BD5"/>
  <one:QuickStyleDef index="4" name="h4" font="Calibri Light" fontSize="11" fontColor="#2E74B5" italic="true"/>
  <one:QuickStyleDef index="5" name="h5" font="Calibri Light" fontSize="11" fontColor="#5B9BD5"/>
  <one:QuickStyleDef index="6" name="h6" font="Calibri Light" fontSize="11" fontColor="#808080" italic="true"/>
  <one:QuickStyleDef index="8" name="p" font="Calibri" fontSize="11" fontColor="automatic"/>
  <one:QuickStyleDef index="9" name="blockquote" font="Calibri" fontSize="11" italic="true" fontColor="#595959"/>
  <one:QuickStyleDef index="10" name="code" font="Consolas" fontSize="9" fontColor="#000000"/>
  <one:QuickStyleDef index="11" name="cite" font="Calibri" fontSize="9" fontColor="#808080"/>

  <one:TagDef index="0" type="3" symbol="3" name="To Do" fontColor="automatic" highlightColor="none"/>

  <one:Title>
    <one:OE quickStyleIndex="0"><one:T><![CDATA[Page title]]></one:T></one:OE>
  </one:Title>

  <one:Outline>
    <one:Position x="36" y="86" z="0"/>
    <one:Size width="700" height="100"/>
    <one:OEChildren>
      <!-- content -->
    </one:OEChildren>
  </one:Outline>
</one:Page>
```

The `name` values above are style names OneNote recognizes. **Index numbers are not guaranteed** —
Phase 0 confirms the real ones. Every content element carries `quickStyleIndex` pointing at one of
these definitions.

### 5.2 Element cheat sheet

**Paragraph**
```xml
<one:OE quickStyleIndex="8"><one:T><![CDATA[text with <b>inline</b> markup]]></one:T></one:OE>
```

**Bulleted item**
```xml
<one:OE quickStyleIndex="8">
  <one:List><one:Bullet bullet="2" fontSize="11"/></one:List>
  <one:T><![CDATA[item]]></one:T>
</one:OE>
```

**Numbered item**
```xml
<one:List><one:Number numberSequence="0" numberFormat="##." font="Calibri" fontSize="11"/></one:List>
```

`numberSequence` must be unique per list instance, otherwise separate lists share a counter.

**Task list item** (`- [ ]` / `- [x]`)
```xml
<one:OE quickStyleIndex="8">
  <one:Tag index="0" completed="false" disabled="false"/>
  <one:T><![CDATA[task]]></one:T>
</one:OE>
```

**Nesting** — wrap children in `one:OEChildren` *inside* the parent `one:OE`, after its `one:T`.

**Table**
```xml
<one:Table bordersVisible="true" hasHeaderRow="true">
  <one:Columns>
    <one:Column index="0" width="150"/>
    <one:Column index="1" width="400"/>
  </one:Columns>
  <one:Row>
    <one:Cell><one:OEChildren><one:OE quickStyleIndex="8"><one:T><![CDATA[a]]></one:T></one:OE></one:OEChildren></one:Cell>
    <one:Cell><one:OEChildren><one:OE quickStyleIndex="8"><one:T><![CDATA[b]]></one:T></one:OE></one:OEChildren></one:Cell>
  </one:Row>
</one:Table>
```

**Code block** — a single-cell table with a shaded background, one `one:OE` per source line:
```xml
<one:Table bordersVisible="false">
  <one:Columns><one:Column index="0" width="660"/></one:Columns>
  <one:Row>
    <one:Cell shadingColor="#F2F2F2">
      <one:OEChildren>
        <one:OE quickStyleIndex="10"><one:T><![CDATA[def main():]]></one:T></one:OE>
        <one:OE quickStyleIndex="10"><one:T><![CDATA[&nbsp;&nbsp;&nbsp;&nbsp;pass]]></one:T></one:OE>
      </one:OEChildren>
    </one:Cell>
  </one:Row>
</one:Table>
```

**Image**
```xml
<one:OE>
  <one:Image format="png">
    <one:Size width="600" height="380" isSetByUser="true"/>
    <one:Data>iVBORw0KGgo...</one:Data>
  </one:Image>
</one:OE>
```

**Metadata** — used for idempotent re-import (§9.2)
```xml
<one:Meta name="Md2OneNote.Source" content="C:\notes\design.md"/>
<one:Meta name="Md2OneNote.Hash" content="sha256:..."/>
```

### 5.3 Inline formatting inside `one:T`

`one:T` content is a small HTML fragment wrapped in CDATA. Supported and used by this project:

| Markdown | Output |
|---|---|
| `**bold**` | `<span style='font-weight:bold'>` |
| `*italic*` | `<span style='font-style:italic'>` |
| `~~strike~~` | `<span style='text-decoration:line-through'>` |
| `` `code` `` | `<span style='font-family:Consolas;background-color:#F2F2F2'>` |
| `[text](url)` | `<a href="url">text</a>` |

**Whitespace collapses.** Leading indentation must be emitted as `&nbsp;`. Because the fragment is
already inside CDATA, do not double-escape: `&nbsp;` is written literally, but `<`, `>` and `&`
appearing in *user text* must be HTML-escaped before being placed in the fragment.

---

## 6. Interop surface

Only these members are needed:

```csharp
// Microsoft.Office.Interop.OneNote.Application
void GetHierarchy(string startNodeId, HierarchyScope scope, out string xml, XMLSchema schema);
void GetPageContent(string pageId, out string xml, PageInfo info, XMLSchema schema);
void UpdatePageContent(string pageChangesXml, DateTime expectedLastModified, XMLSchema schema, bool force);
void CreateNewPage(string sectionId, out string pageId, NewPageStyle style);
void NavigateTo(string objectId, string objectName, bool newWindow);
void GetSpecialLocation(SpecialLocation location, out string path);
```

Standard creation flow:

```csharp
onenote.CreateNewPage(sectionId, out var pageId, NewPageStyle.npsBlankPageWithTitle);
onenote.GetPageContent(pageId, out var skeleton, PageInfo.piBasic, XMLSchema.xs2013);
var page = BuildPage(skeleton, markdown);            // keep the ID attribute from the skeleton
onenote.UpdatePageContent(page.ToString(), DateTime.MinValue, XMLSchema.xs2013, force: true);
onenote.NavigateTo(pageId, null, false);
```

`DateTime.MinValue` skips the concurrency check; `force: true` overwrites. `UpdatePageContent` is
whole-page transactional — build the complete document, then submit once.

To find the active section, call `GetHierarchy(null, HierarchyScope.hsPages, ...)` and read the
`isCurrentlyViewed="true"` attribute.

**All interop calls must be wrapped in retry.** OneNote returns `COMException` with
`RPC_E_SERVERCALL_RETRYLATER` (`0x8001010A`) when busy. Retry 3 times with a 300 ms backoff.

---

## 7. Markdown mapping

| Markdig node | Output |
|---|---|
| `HeadingBlock` 1–6 | `one:OE` with `quickStyleIndex` for `h1`–`h6` |
| First H1, if the document starts with one | becomes `one:Title`; otherwise use the filename |
| `ParagraphBlock` | `one:OE` with `p` style |
| `QuoteBlock` | `one:OE` with `blockquote` style, nested via `one:OEChildren` |
| `ListBlock` unordered | `one:Bullet bullet="2"` |
| `ListBlock` ordered | `one:Number numberSequence` (fresh per list) |
| `TaskList` inline | `one:Tag index="0" completed="…"` |
| `Table` | `one:Table` with `hasHeaderRow` |
| `FencedCodeBlock` with a diagram language | route to `IDiagramRenderer` (§8) |
| `FencedCodeBlock` otherwise | shaded single-cell table, highlighted |
| `CodeBlock` (indented) | same as fenced, no highlighting |
| `ThematicBreakBlock` | an empty body paragraph (vertical space); OneNote has no rule, and a shaded table read as a grey box |
| `LinkInline` with `IsImage` | resolve relative to the Markdown file, embed as `one:Image` |
| `FootnoteGroup` | appended at the end under an `h2` "Notes", `cite` style |
| YAML front matter | parsed, not rendered; `title` overrides the page title |

Front matter keys to honour: `title`, `tags` (mapped to OneNote tags on the title line if trivial;
otherwise ignore in v1).

---

## 8. Diagram rendering

### 8.1 Contract

```csharp
public interface IDiagramRenderer
{
    bool CanRender(string language);                 // "mermaid", "dot", "vega-lite", ...
    Task<DiagramImage> RenderAsync(string source, CancellationToken ct);
}

public sealed record DiagramImage(byte[] Png, int WidthPx, int HeightPx, double Scale);
```

Renderers are resolved from a registry keyed by the fence info string. An unknown language falls
back to the plain code-block writer — **never** throw and never drop content.

### 8.2 WebView2 renderer

One hidden `WebView2` instance, reused across diagrams, created on the UI thread.

1. `EnsureCoreWebView2Async()`, then `NavigateToString(shellHtml)` where the shell embeds
   `mermaid.min.js` and friends as embedded resources served through
   `SetVirtualHostNameToFolderMapping` or inlined into the HTML.
2. Call a JS entry point `renderDiagram(lang, source)` via `ExecuteScriptAsync`; it returns
   `{"ok":true,"w":720,"h":410}` or `{"ok":false,"error":"..."}`.
3. The shell takes `w`/`h` from the SVG's `viewBox` and pins the SVG to them. Mermaid emits
   `width="100%"`, so measuring the rendered box gives the viewport's width: a wide sequence
   diagram came out squeezed and blurred that way (2026-09-14).
4. `Emulation.setDeviceMetricsOverride {width: w, height: h, deviceScaleFactor: 2}` then
   `Page.captureScreenshot {clip: 0,0,w,h, captureBeyondViewport: true}` over
   `CallDevToolsProtocolMethodAsync`. Not `CapturePreviewAsync`: it captures the window, and the
   window cannot be made the diagram's size (Windows clamps the first resize of a shown window to
   the screen; a docked child does not follow the next). A plain `captureBeyondViewport` without
   the metrics override grew the capture downwards only.
5. Check the PNG header against `w*2 × h*2` (`PngHeader`); a mismatch is a failed render.
6. Emit `one:Size` at **half** the captured pixel dimensions, fitted to the content column like
   any image (§9.1), so the picture stays crisp when enlarged.

Mermaid config: `{ startOnLoad: false, htmlLabels: false, securityLevel: 'strict' }`.
`htmlLabels: false` is required — `foreignObject` content does not rasterize reliably.

### 8.3 Formats via the same shell

`mermaid`, `dot` / `graphviz` (`@hpcc-js/wasm`), `vega-lite`, `chartjs`, and `math` (KaTeX, for
`$$…$$` blocks). All are JS libraries loaded into the shell; adding one is a new branch in
`renderDiagram` plus an embedded asset.

### 8.4 PlantUML (optional, off by default)

Requires Java or a server. If implemented, expose it as a setting pointing at a Kroki or PlantUML
server URL, disabled unless the user sets it. Do not make the core path depend on it.

### 8.5 Failure handling

If rendering fails, insert the original fence as a code block plus a `cite`-styled line with the
error message. The import must always complete.

---

## 9. Behaviour details

### 9.1 Sizing

Outline width 700 px at `x=36, y=86`. Images wider than 660 px are scaled down proportionally.
Table column widths are distributed evenly unless the Markdown table has an obvious key column
(first column narrower); do not over-engineer this in v1.

### 9.2 Re-import

Store `one:Meta` entries with the source path and a SHA-256 of the file. On import, read the
active section's page listing once (`GetHierarchy(sectionId, hsPages)` includes each page's
`one:Meta`) and look for the last page with a matching `Md2OneNote.Source`:

- no match → create a new page
- match, hash differs → create a new, superseding page with a distinguishable title; the original
  is untouched (FR-21: nothing in the product modifies an existing page — DESIGN.md §7.2)
- match, hash identical → skip, report as unchanged

### 9.3 Multi-file import

Process sequentially. Show progress in the ribbon's status or a small modeless form. Report a
summary at the end: created / updated / skipped / failed, with per-file errors.

---

## 10. Registration and deployment

OneNote uses an **unversioned** add-in registry path, unlike Word and Excel:

```
HKCU\Software\Microsoft\Office\OneNote\AddIns\Md2OneNote.AddIn
    FriendlyName  REG_SZ    "Markdown → OneNote"
    Description   REG_SZ    "Imports Markdown files as styled OneNote pages"
    LoadBehavior  REG_DWORD 3
```

COM class registration: either `regasm /codebase` (needs admin) or a per-user write to
`HKCU\Software\Classes\CLSID\{guid}\InprocServer32` with `mscoree.dll`, plus `Class`, `Assembly`,
`RuntimeVersion`, and `CodeBase` values. Prefer per-user — no elevation.

**OneNote hosts COM add-ins out of process**, in a `dllhost.exe` COM surrogate — it never loads
`mscoree.dll` into `ONENOTE.EXE`. A DLL can only be activated that way with a surrogate
registration, so these two entries are mandatory, not optional:

```
HKCU\Software\Classes\CLSID\{guid}
    AppID         REG_SZ    "{guid}"
HKCU\Software\Classes\AppID\{guid}
    DllSurrogate  REG_SZ    ""            (empty = the system surrogate, dllhost.exe)
```

Without them activation fails before any add-in code runs; OneNote reports "a runtime error
occurred during the loading of the COM Add-in" and sets `LoadBehavior=2`. Per-user is enough:
verified 2026-09-13 against the working OneMore registration, which has the same keys under HKCU.

`tools/register.ps1` must support `-Install` and `-Uninstall`, and must refuse to run while
`ONENOTE.EXE` or the add-in's `dllhost.exe` surrogate is alive (the surrogate keeps the DLL open
for a few seconds after OneNote exits).

Installer: Inno Setup, per-user, invoking the same registration logic.

---

## 11. Implementation phases

Each phase ends with a working, demonstrable state. Do not start a phase before the previous one
passes its acceptance check.

### Phase 0 — Schema spike (do this first)

- [ ] Manually author a OneNote page containing: H1–H3, a paragraph, a bulleted list, a numbered
      list, a nested list, a task list, a 3×3 table with a header row, a shaded cell, a code-like
      block in Consolas, a blockquote, and an inline image
- [ ] Dump it: `GetPageContent(pageId, out xml, PageInfo.piAll, XMLSchema.xs2013)`
- [ ] Record the real `QuickStyleDef` indices, `TagDef` values, bullet numbers, and attribute
      spellings in `docs/page-schema-notes.md`
- [ ] Update §5 of this document if reality differs

**Accept when:** `docs/page-schema-notes.md` contains a verified style table, and any contradiction
with §5 is resolved in favour of the dump.

### Phase 1 — Add-in shell

- [ ] .NET Framework 4.8 class library, `ComVisible`, fixed GUID and ProgId
- [ ] `IDTExtensibility2` + `IRibbonExtensibility`, ribbon button on `TabInsert`
- [ ] File logger to `%LOCALAPPDATA%\Md2OneNote\log.txt`, `OnConnection` fully wrapped in try/catch
- [ ] `tools/register.ps1` install/uninstall
- [ ] Button shows a message box with the active section name, read via `GetHierarchy`

**Accept when:** the button appears in OneNote after running the script, and clicking it names the
current section.

### Phase 2 — Text rendering

- [ ] `Md2OneNote.Core` with `OneNoteXmlRenderer.Render(string markdown, string baseDir) → string`
- [ ] Headings, paragraphs, blockquotes, inline formatting, links
- [ ] Bulleted, numbered, and nested lists; task lists
- [ ] Page creation and `UpdatePageContent` wired to the ribbon button, with a file picker
- [ ] Retry wrapper for `RPC_E_SERVERCALL_RETRYLATER`

**Accept when:** a README-sized Markdown file imports and is visually indistinguishable from
hand-authored OneNote content.

### Phase 3 — Tables, code, local images

- [ ] GFM tables with header row
- [ ] Fenced code blocks as shaded tables, `&nbsp;` indentation, syntax highlighting
- [ ] Local images resolved relative to the source file, embedded as base64, scaled to fit

**Accept when:** a document with a 6-column table, a 40-line code block, and two PNGs imports
correctly.

### Phase 4 — Mermaid

- [ ] `Md2OneNote.Diagrams.WebView2` with the shell and `renderDiagram`
- [ ] Mermaid at 2× capture, half-size `one:Size`
- [ ] Graceful failure per §8.5

**Accept when:** flowchart, sequence, class, state, and Gantt diagrams all render legibly.

### Phase 5 — Additional formats and polish

- [ ] Graphviz, Vega-Lite, Chart.js, KaTeX through the same shell
- [ ] Multi-file import with progress and summary
- [ ] Re-import via `one:Meta` hash matching
- [ ] Inno Setup installer

---

## 12. Testing

`Md2OneNote.Core` is pure and gets real tests: given a Markdown string, assert on the produced
XML using XPath against the `one` namespace. Golden-file tests for a handful of representative
documents are the highest-value form here.

Keep a `samples/` folder with: `basic.md`, `lists.md`, `tables.md`, `code.md`, `diagrams.md`,
`kitchen-sink.md`. Every phase re-imports all of them before it is considered done.

Interop and WebView2 paths are verified manually; do not invest in mocking the OneNote COM
surface.

---

## 13. Known pitfalls

- **`LoadBehavior` silently resets to `2`** when the add-in throws during startup, and OneNote then
  refuses to load it. Check `HKCU\Software\Microsoft\Office\16.0\OneNote\Resiliency\DisabledItems`
  and clear it during development.
- **Missing `AppID` + `DllSurrogate`** (§10) is the one registration mistake that leaves no trace:
  "a runtime error occurred during the loading of the COM Add-in", `LoadBehavior` → 2, and nothing
  in our log, because nothing of ours ran. Activating the class from PowerShell proves nothing —
  that activates in-process, which OneNote never does.
- **Bitness mismatch** silently prevents loading. Build AnyCPU.
- **Debugging** means attaching to the `dllhost.exe` whose command line carries our CLSID
  (`DllHost.exe /Processid:{04185F61-…}`), not to `ONENOTE.EXE` and not pressing F5. Message boxes
  come from that process and can open behind the OneNote window.
- **Whitespace collapses** in `one:T`. Indentation requires `&nbsp;`.
- **Base64 images inflate the payload.** A document with 20 diagrams produces several MB of XML;
  that works but is not instant. Build the whole page, submit once.
- **`numberSequence` collisions** make separate ordered lists continue each other's numbering.
- **CDATA and escaping** interact confusingly. Escape user text as HTML, then place it inside
  CDATA — do not escape twice.
- **WebView2 must be created on a UI thread** with a message pump. Marshal accordingly if the
  import runs on a background thread.

---

## 14. Conventions

- C# 7.3 (Framework 4.8 constraint), nullable annotations not available — use explicit guards
- No `async void` outside event handlers
- No third-party DI container; constructor injection by hand
- Every public method in `Core` is deterministic and side-effect free
- Log at boundaries only: ribbon callbacks, interop calls, diagram renders
- Commit per phase, tagged `phase-0` … `phase-5`
