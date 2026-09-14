# OneNote page schema — observed

**Status: filled in from a real dump, 2026-09-09.** Values below marked ✅ were read off a
hand-authored page; ❌ marks a place where the renderer currently disagrees with OneNote and must
change; ❔ marks a question this dump could not answer.

This file is the authority. Where it disagrees with DESIGN.md §5 or IMPLEMENTATION.md §5, those
documents are wrong and get corrected.

| | |
| --- | --- |
| OneNote build tested | 16.0.20326.20144 (Microsoft 365, x64, Click-to-Run) |
| Schema requested / namespace returned | xs2013 / `http://schemas.microsoft.com/office/onenote/2013/onenote` |
| Dumped on | 2026-09-09 (OneMore 7.1 → *Show XML*), re-dumped 2026-09-14 with binary data by the add-in's own *Dump Page XML* button |
| Raw dump | `docs/schema-dump/page-schema-2013.xml` (git-ignored); 09-14 dump on the Desktop, `Md2OneNote-dump-20260914-095811` |

---

## 1. Quick styles

Indices are **ours** (DESIGN.md §6.1, D3) and the dump confirms that reasoning is safe: OneNote
numbered its own block `PageTitle=0, p=1, h1=2 … h6=7`, a different order from ours, and nothing
depends on it. The **names** are what matter, and they are confirmed exactly.

| name | confirmed? | OneNote's actual definition | renderer emits |
| --- | --- | --- | --- |
| `PageTitle` | ✅ name | Calibri **Light**, 20.0, `automatic` | Calibri Light, 20, `#1F497D` ❌ colour |
| `p` | ✅ name | Calibri, 11.0, `automatic` | matches ✅ |
| `h1` | ✅ name | **Calibri**, 16.0, `#1E4E79` | Calibri **Light** ❌ |
| `h2` | ✅ name | **Calibri**, 14.0, `#2E75B5` | Calibri Light, `#2E74B5` ❌ both |
| `h3` | ✅ name | **Calibri**, 12.0, `#5B9BD5` | Calibri Light ❌ |
| `h4` | ✅ name | **Calibri**, **12.0**, `#5B9BD5`, italic | Calibri Light, 11, `#2E74B5` ❌ all three |
| `h5` | ✅ name | **Calibri**, 11.0, `#2E75B5` | Calibri Light, `#5B9BD5` ❌ both |
| `h6` | ✅ name | **Calibri**, 11.0, `#2E75B5`, italic | Calibri Light, `#808080` ❌ both |
| `blockquote` | ❔ | *absent — page had no Quote paragraph* | Calibri, 11, `#595959`, italic |
| `code` | ❔ | *absent* | Consolas, 9, `#000000` |
| `cite` | ❔ | *absent — page had no Citation paragraph* | Calibri, 9, `#808080` |

**The headline correction: OneNote's headings are Calibri, not Calibri Light.** Only `PageTitle`
uses Calibri Light. Since FR-8 is about output being indistinguishable from hand-authored content,
this one is worth matching precisely.

OneNote also emits `highlightColor="automatic"`, `spaceBefore="0.0"` and `spaceAfter="0.0"` on every
definition, and writes sizes as `"16.0"` rather than `"16"`. Whether it *requires* any of that on
input is untested.

The three missing styles are a gap in the source page, not in OneNote — the spike README asks for a
Quote and a Citation paragraph and they were not on it. Re-dump with those present to close them.

## 2. Tags

```xml
<one:TagDef index="0" type="0" symbol="3" fontColor="automatic" highlightColor="none" name="To Do"/>
<one:Tag index="0" completed="false" disabled="false" creationDate="2026-09-08T10:31:14.000Z"/>
```

- ✅ `symbol="3"`, `name="To Do"`, and `completed` is the attribute carrying checked state.
- ❌ **`type="0"`, not `type="3"`.** The renderer emits `type="3"` (`StyleTable.TagDefinition`).
- OneNote adds `creationDate` unasked; presumably optional on input.

## 3. Order of `one:Page` children

Observed:

```
one:TagDef → one:QuickStyleDef* → one:PageSettings → one:Title → one:Outline*
```

Renderer emits `one:QuickStyleDef* → one:TagDef → one:Meta* → one:Title → one:Outline`.

- ❌ **`TagDef` precedes `QuickStyleDef`**, the reverse of ours.
- ❔ **Still the open question that matters more: does OneNote *reject* a wrong order, or silently
  reorder?** A read-back cannot answer it — only a write can. First thing to try once the add-in can
  call `UpdatePageContent`.
- `one:PageSettings` (page size, rule lines) is emitted by OneNote and not by us. Probably optional.
- No `one:Meta` on this page, so its position is unobserved.

## 4. Lists

| question | answer |
| --- | --- |
| bulleted item markup | ✅ `one:List` → `one:Bullet bullet fontSize` |
| bullet glyph per depth | ✅ observed `2` → `3` → `13` for levels 1/2/3 |
| numbered item markup | ✅ `one:List` → `one:Number numberSequence numberFormat font fontSize text` |
| ordered lists can start at n | ✅ **`restartNumberingAt="5"`** — not `startAt` |
| nesting is by nested `one:OEChildren` | ✅ |

❌ **`numberSequence` does not mean what IMPLEMENTATION.md §5.2 says it means.** The dump shows
`numberSequence="0"` on *two independent* top-level ordered lists, `4` on both second-level lists and
`2` on both third-level ones — tracking the numbering **style** (0 = `1.`, 4 = `a.`, 2 = `i.`), not
the list instance. The second list restarts at 5 via `restartNumberingAt`, not via a fresh sequence.

Consequences:

- `RenderContext.NextNumberSequence()` — a monotonic per-list counter — is built on the misreading
  and should go.
- DESIGN.md §6.2's `NumberSequenceAllocator`, and the "`numberSequence` collisions" entry in
  IMPLEMENTATION.md §13, describe a bug that does not exist in the form stated.
- ❔ What actually separates two adjacent ordered lists is now unknown and needs its own test.

OneNote also writes a `text="1."` attribute holding the rendered marker.

## 5. Inline text

`one:T` holds a CDATA HTML fragment. Confirmed against the page:

| construct | OneNote's emission | renderer |
| --- | --- | --- |
| bold | ✅ `<span style='font-weight:bold'>` | matches |
| italic | ✅ `<span style='font-style:italic'>` | matches |
| link | ✅ `<a href="…">` | matches |
| inline code | ✅ `<span style='font-family:Consolas'>` | matches |
| superscript | ✅ **`<span style='vertical-align:super'>`** — *not* `<sup>` | ❌ not implemented |
| subscript | ✅ **`<span style='vertical-align:sub'>`** | ❌ not implemented |
| line break | ✅ literal `<br />` inside the CDATA | |
| non-breaking space | ✅ literal `&nbsp;` inside the CDATA, not double-escaped | matches |
| strikethrough | ❔ *absent — page had no struck-through text* | emits `text-decoration:line-through` |

**The superscript question is answered, and the assumed answer was wrong.** `<sup>` is not what
OneNote produces; footnote markers must use `vertical-align:super`.

`one:T` can also carry a `style` attribute (`<one:T style="font-family:Consolas;font-size:11.0pt">`)
that applies to the whole run — an alternative to wrapping everything in a span.

## 6. Tables

| question | answer |
| --- | --- |
| borders | ✅ `bordersVisible="true"` on `one:Table` |
| **header row** | ✅ **`hasHeaderRow="true"` is real schema support** — the note's guess that there was none was wrong |
| column widths | ✅ `one:Columns` → `one:Column index width` (no `isLocked` observed) |
| cell shading | ✅ `shadingColor="#D0CECE"` on `one:Cell` |
| a cell holds blocks | ✅ `one:Cell` → `one:OEChildren` → `one:OE` |
| a table is inside a paragraph | ✅ **`one:OE` → `one:Table`, never `one:OEChildren` → `one:Table`.** OneNote rejects the whole page otherwise: "Element Table is unexpected according to content model of parent element OEChildren. Expecting: OE, HTMLBlock" (first real import, 2026-09-14). The renderer had it wrong for every table, code block and rule; fixed. |

## 7. Images

```xml
<one:Image>
  <one:Size width="243.7499847412109" height="162.5812377929687" isSetByUser="true"/>
  <one:CallbackID callbackID="…"/>
  <one:OCRData lang="en-US">…</one:OCRData>
</one:Image>
```

- ✅ `one:Size` with `isSetByUser="true"` after a manual resize.
- ❌ **No `format` attribute on read-back**, with or without binary data (09-14 dump). We assume
  `format="png"` on write; whether OneNote requires, ignores, or rejects it is untested.
- ✅ `one:Data` is plain base64 of the image file: the 09-14 dump (`piBinaryData`) carries one
  `one:Data` element starting `/9j/4AAQSkZJRg…` — a JPEG signature — inside the `one:Image`.
- ❔ **Points versus pixels is still open**, and it is the one that decides whether the Phase 4
  "capture at 2×, halve `one:Size`" rule is right or off by a constant. Not answerable from a
  read-back alone; needs a write with a known-size image.
- OneNote attaches `CallbackID` and OCR results of its own accord.

## 8. `one:Meta` and re-import — **answered 2026-09-14**

Experiment: `OneNoteGateway.CreatePage` in the "Md2OneNote spike" section, then
`ReplacePageContent` with a page carrying `<one:Meta name="md2onenote.source" content="probe-…"/>`
before its `one:Title`, then read back two ways. Read-back saved as `Desktop\Md2OneNote-meta-probe.xml`.

- ✅ **`one:Meta` appears in `GetHierarchy(section, hsPages)` output**, as a child of the
  `one:Page` element in the listing:
  ```xml
  <one:Page ID="…" name="Meta probe probe-20260914-100334" dateTime="…" lastModifiedTime="…" pageLevel="1">
    <one:Meta name="md2onenote.source" content="probe-20260914-100334" />
  </one:Page>
  ```
  ⇒ **`IPageIndex` is unnecessary.** One section listing answers "which pages did we create, from
  which source" without fetching any page (DESIGN.md §7.3, §14 Q1).
- ✅ `one:Meta` survives the `GetPageContent(piBasic)` round-trip, name and content intact.
- ✅ `UpdatePageContent` with `dateExpectedLastModified = DateTime.MinValue`, `xs2013`, `force`
  accepted the page as written.
- ✅ Partial answer to §3: we wrote `Meta → Title → Outline` with no `QuickStyleDef`; OneNote
  stored it as `QuickStyleDef → Meta → PageSettings → Title → Outline`. It **reorders and fills in
  on save rather than rejecting** — at least for this ordering. So `one:Meta` sits after the style
  definitions and before `PageSettings`; the renderer's `QuickStyleDef* → TagDef → Meta* → Title`
  differs only in the `TagDef` position already noted.

## 9. Anything else worth knowing

- Every `one:OE` carries `alignment="left"`; we emit none. Apparently defaulted.
- OneNote stamps `omHash` on `Title`, `Outline` and `OEChildren`, and `selected` on whatever the
  caret touched. Both are read-back noise, not input.
- The page root carries `name`, `dateTime`, `lastModifiedTime`, `pageLevel`, `lang`, and
  `isCurrentlyViewed` — useful, since it confirms the attribute `GetHierarchy` matching relies on.
- The main outline sits at `x="36.0" y="86.4"`, matching IMPLEMENTATION.md §9.1's (36, 86). Its
  width was 540, but that is this page's layout, not a constraint.
- An empty stray `one:Outline` appeared at x=774 — an accidental click. Real pages contain debris;
  anything that walks a page must tolerate it.

## 10. Why this was not dumped by `tools/schema-spike`

The spike bound to OneNote through `dynamic`, and that binder cannot talk to OneNote's
`Application` object at all. Established on 2026-09-09, understood on 2026-09-14:

- OneNote's `Application` answers **`E_FAIL` to `IDispatch::GetTypeInfo`**. The `dynamic` binder
  asks for type information before its first call and treats that failure as fatal
  (`ComRuntimeHelpers.GetITypeInfoFromIDispatch`), so every call failed with `E_FAIL` — before
  OneNote was asked anything. The 09-09 reading, "OneNote refuses out-of-process automation", was
  wrong; the stack trace from inside the add-in (same `E_FAIL`, same binder frames) settled it.
- Reflection (`Type.InvokeMember`) fails differently but for the same reason: it wants the type
  library through `LoadRegTypeLib`, and this install registers it under
  `TypeLib\{0EA692EE-…}\1.1\0\Win32` only → `TYPE_E_LIBNOTREGISTERED`.
- **Vtable calls work, in and out of process.** `Md2OneNote.Interop.IApplication` declares the
  interface from the type library in `ONENOTE.EXE` (resource 3); a plain PowerShell client using
  it read the active section and its pages from a running OneNote, with stock registration only
  (the per-user `win64` typelib key from 09-09 was removed first to prove that). This is what the
  primary interop assembly does, and what OneMore does.
- OneMore is *not* in-process either: OneNote hosts add-ins in a `dllhost.exe` surrogate and
  hands them an `Application` proxy in `OnConnection`. Calls through that proxy cross processes
  and succeed, for the same reason: OneMore calls the vtable.

**Consequence for the plan:** unchanged in outcome, corrected in reasoning. The remaining ❔ items
needed a write, and a temporary *Dump Page XML* button in the add-in (running in the surrogate,
calling the vtable) answered them; the 09-14 dump above came from it. Both the button and
`tools/schema-spike` were removed on 2026-09-14 once §8 was answered. Anything that needs a fresh
dump again can use OneMore's *Show XML*, or `Md2OneNote.Interop.IApplication` from a plain
PowerShell 5.1 script.