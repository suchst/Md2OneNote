# Schema spike (Phase 0)

`Md2OneNote.Core` renders OneNote page XML against a specification nobody has checked against
OneNote. `StyleTable` picks quick-style names, `OneNoteXmlRenderer` picks an element order, and
`InlineWriter` assumes what `one:T` accepts. All of it is covered by golden-file tests, which means
the tests agree with the assumptions rather than with OneNote.

This throwaway tool closes that gap: hand-author a page containing every construct, dump it, and
read back what OneNote actually produces.

Delete this folder once `docs/page-schema-notes.md` is filled in.

## 1. Author the page

In **OneNote desktop** (2016 or Microsoft 365 — the Store app has no COM API), create a page in a
scratch section and put every one of these on it:

- A page title.
- Paragraphs in **Heading 1** through **Heading 6**, and one in **Normal**.
- A paragraph exercising **bold**, *italic*, ~~strikethrough~~, a hyperlink, superscript (`x²`),
  subscript (`H₂O`), and a run set to Consolas as a stand-in for inline code.
- A bulleted list three levels deep.
- A numbered list three levels deep, plus a second numbered list set to **start at 5**
  (right-click → Numbering → Numbering Options).
- Two To Do checkboxes, one ticked and one not.
- A 3×3 table with a header row, visible borders, and one shaded cell.
- A paragraph styled as **Quote**, and one styled as **Citation**.
- A pasted image, resized, with a caption paragraph under it.

The dump can only report constructs the page contains. Anything you skip comes back as
"absent from this page" in the findings.

## 2. Run it

Leave that page open and on screen, then:

```
dotnet build tools/schema-spike
tools/schema-spike/bin/Debug/net48/SchemaSpike.exe
```

Output lands in `docs/schema-dump/` (git-ignored — it is a copy of your notebook content):

| file | what it is |
| --- | --- |
| `page-schema-0/1/2.xml` | the same page at each schema version, indented |
| `hierarchy-pages.xml` | `GetHierarchy` over the section |
| `findings.md` | the reduction of all of it to the facts the renderer depends on |

Read `findings.md` first. It states each observed value next to what the renderer currently assumes
and flags the mismatches, so the raw XML is only needed when something disagrees.

### If it will not connect

`CO_E_SERVER_EXEC_FAILURE (0x80080005)` on startup almost always means **the shell is elevated**.
COM will not hand a medium-integrity server to a high-integrity client, and it cannot start a
second elevated OneNote either, so activation fails outright. Run from an ordinary,
non-administrator shell — including when driving this from an editor or agent that happens to be
running as administrator.

This is worth remembering beyond the spike: it is the same reason `tools/register.ps1` writes to
`HKCU` and must never require elevation (IMPLEMENTATION.md §10). The add-in itself is unaffected —
it loads *inside* `ONENOTE.EXE`, so there is no cross-integrity activation at all.

### The `one:Meta` probe

```
SchemaSpike.exe --meta-probe
```

**This creates a page** titled "Md2OneNote Phase 0 probe — safe to delete" in the section you have
open, writes a `one:Meta` onto it, and reads the section listing back. Delete the page afterwards.

It answers the question that decides whether `IPageIndex` ships at all: if `GetHierarchy` returns
our `one:Meta`, re-import matching (FR-18) reads it straight off the section listing and the whole
index — with its persistence, invalidation and stale-entry degradation — is unnecessary
(DESIGN.md §7.3).

## 3. Record what you found

Fill in `docs/page-schema-notes.md`, then correct `Md2OneNote.Core` and DESIGN.md §5 where reality
disagrees. The golden files will need regenerating for any change that reaches the output.

## Notes

- Late binding through `IDispatch`, no interop assembly, no type library. `Md2OneNote.Interop` has
  to work across OneNote builds without pinning an interop version, so the spike is written the way
  the real gateway would be — including the `RPC_E_SERVERCALL_RETRYLATER` retry, which OneNote
  triggers often enough that you will see it scroll past.
- AnyCPU is fine either way: OneNote registers as an out-of-process COM server, so a 64-bit client
  can drive a 32-bit OneNote.
- Not in `Md2OneNote.sln` on purpose — it cannot run without OneNote, so CI has no use for it.
