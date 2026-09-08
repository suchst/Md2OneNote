# OneNote page schema — observed

**Status: not yet filled in.** Everything below is what `Md2OneNote.Core` assumes today. Run
`tools/schema-spike` against a hand-authored page (see its README), then replace each *assumed*
value with what OneNote actually returned and mark the row.

This file is the authority once it is filled in. Where it disagrees with DESIGN.md §5, DESIGN.md is
wrong and gets corrected.

| | |
| --- | --- |
| OneNote build tested | *(not yet)* |
| Schema requested / namespace returned | *(not yet)* |
| Dumped on | *(not yet)* |

---

## 1. Quick styles

`StyleTable` authors the whole `one:QuickStyleDef` block, so the **indices are ours** — pages are
created blank and filled in one shot, and nothing forces a gap (DESIGN.md §6.1, D3). The **names**
are not ours: they are what makes OneNote treat the output as its own styles in the outline view
and style picker (FR-8).

So this table has one column that cannot be wrong and one that can.

| index (ours) | name (OneNote's) | assumed font | assumed size | assumed colour | confirmed? |
| --- | --- | --- | --- | --- | --- |
| 0 | `PageTitle` | Calibri Light | 20 | `#1F497D` | ☐ |
| 1 | `h1` | Calibri Light | 16 | `#1E4E79` | ☐ |
| 2 | `h2` | Calibri Light | 14 | `#2E74B5` | ☐ |
| 3 | `h3` | Calibri Light | 12 | `#5B9BD5` | ☐ |
| 4 | `h4` | Calibri Light | 11 | `#2E74B5`, italic | ☐ |
| 5 | `h5` | Calibri Light | 11 | `#5B9BD5` | ☐ |
| 6 | `h6` | Calibri Light | 11 | `#808080`, italic | ☐ |
| 7 | `p` | Calibri | 11 | automatic | ☐ |
| 8 | `blockquote` | Calibri | 11 | `#595959`, italic | ☐ |
| 9 | `code` | Consolas | 9 | `#000000` | ☐ |
| 10 | `cite` | Calibri | 9 | `#808080` | ☐ |

A name OneNote does not recognise is not a crash — it is a page that renders but drops out of the
outline view, which is why this is worth checking before Phase 2 rather than after.

## 2. Tags

Assumed: the To Do checkbox is `<one:TagDef index="0" type="3" symbol="3" name="To Do" …/>`,
referenced from an `one:OE` by `<one:Tag index="0" completed="true|false"/>`.

- [ ] `type` confirmed
- [ ] `symbol` confirmed
- [ ] the attribute carrying checked state confirmed (`completed`?)

## 3. Order of `one:Page` children

Assumed, as emitted by `OneNoteXmlRenderer.Render`:

```
one:QuickStyleDef* → one:TagDef → one:Meta* → one:Title → one:Outline
```

- [ ] confirmed against a real page
- [ ] confirmed that OneNote **rejects** a wrong order rather than silently reordering

The second question matters more than the first: if a wrong order is silently tolerated, this is a
non-issue; if it is rejected, it is the first thing Phase 2 will hit.

## 4. Lists

| question | assumed | confirmed |
| --- | --- | --- |
| bulleted item markup | `one:List` → `one:Bullet` with `bullet` and `fontSize` | ☐ |
| numbered item markup | `one:List` → `one:Number` with `numberSequence` and `numberFormat` | ☐ |
| bullet glyph number per depth | *(not yet observed)* | ☐ |
| ordered lists can start at n | `startAt` on `one:Number` | ☐ |
| nesting is by nested `one:OEChildren` | yes | ☐ |

`startAt` is the one with a fallback cost: without it, `5. item` in Markdown renumbers from 1 and
FR-13 needs a different answer.

## 5. Inline text

`one:T` carries a CDATA section holding a small HTML subset. What is actually in that subset
decides what `InlineWriter` may emit.

| construct | assumed emission | confirmed |
| --- | --- | --- |
| bold | `<span style='font-weight:bold'>` | ☐ |
| italic | `<span style='font-style:italic'>` | ☐ |
| strikethrough | `<span style='text-decoration:line-through'>` | ☐ |
| inline code | `<span style='font-family:Consolas'>` | ☐ |
| superscript | `<sup>` | ☐ |
| subscript | `<sub>` | ☐ |
| link | `<a href="…">` | ☐ |

Superscript is the known unknown: if `one:T` does not honour `<sup>`, footnote markers need a
different rendering.

## 6. Tables

| question | assumed | confirmed |
| --- | --- | --- |
| borders | `bordersVisible` on `one:Table` | ☐ |
| column widths | `one:Columns` → `one:Column index width isLocked` | ☐ |
| header row | *(no schema support — first row styled instead?)* | ☐ |
| cell shading | `shadingColor` on `one:Cell` | ☐ |
| a cell holds blocks | `one:Cell` → `one:OEChildren` | ☐ |

## 7. Images

| question | assumed | confirmed |
| --- | --- | --- |
| bytes | base64 in `one:Data` | ☐ |
| declared format | `format` on `one:Image` | ☐ |
| display size | `one:Size width height` in points, not pixels | ☐ |
| `isSetByUser` needed to stop auto-sizing | probably | ☐ |

The points-versus-pixels question is what makes Phase 4's "capture at 2×, set `one:Size` at half"
either correct or off by a constant factor.

## 8. `one:Meta` and re-import

**The question that decides whether `IPageIndex` ships.**

FR-18 matches a re-imported file to its existing page by the `one:Meta` stamped at creation. If
`GetHierarchy` returns that metadata with the section listing, matching is a single call and
`IPageIndex` — with its persistence, invalidation, and "cache, never an authority" degradation —
does not need to exist at all (DESIGN.md §7.3).

- [ ] `one:Meta` survives `GetPageContent` round-trip
- [ ] `one:Meta` appears in `GetHierarchy` output ⇒ **delete `IPageIndex`**
- [ ] if not: what *does* `GetHierarchy` return per page (id, name, dateTime, lastModifiedTime …)?

Run `SchemaSpike.exe --meta-probe` to settle it.

## 9. Anything else worth knowing

*(Record surprises here — attributes OneNote adds unasked, things it silently rewrites, constructs
that vanish on round-trip. Those are the ones that cost a day later.)*
