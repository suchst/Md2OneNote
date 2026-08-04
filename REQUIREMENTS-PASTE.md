# Md2OneNote — Paste as Markdown (feature addendum)

Extends `REQUIREMENTS.md`. Requirements are numbered `FR-P*` / `NFR-P*` to keep them distinct from
the import feature. Where this document and `REQUIREMENTS.md` disagree, §7 records the amendment.

Status: draft from requirements discovery, 2026-08-03.

---

## 1. Feature definition

A ribbon command — **Paste as Markdown** — that takes Markdown text from the clipboard, runs it
through the existing conversion pipeline, and inserts the styled result into the page the user is
currently editing, at the cursor.

**Why this is worth building:** the conversion engine already exists and is reused wholesale. What
the feature adds is reach. Markdown arrives on the clipboard constantly — from chat assistants, from
editors, from documentation sites — and today the only way into OneNote is a plain-text dump or a
pasted-HTML mess. This closes the gap without the user ever touching a `.md` file.

**What it is not:** an interception of `Ctrl+V`. OneNote's COM add-in model exposes ribbon callbacks
and the `Application` object, and no editing-surface or paste event whatsoever. Redirecting the
native paste key is not achievable through supported extensibility, and the unsupported route (a
`WH_KEYBOARD_LL` hook) is rejected on distribution grounds — see §6.

---

## 2. Decisions from discovery

| Decision | Value | Consequence |
|---|---|---|
| Insertion target | **At the cursor / selection** | Requires locating the selection in page XML and mutating a page the add-in did not create. Drives §4.1 and the FR-21 amendment. |
| Invocation | **Ribbon button, plus an opt-in global hotkey** | Hotkey is off by default, foreground-gated, and must fail gracefully when the combination is taken. |
| Content sources | **LLM output, editors, and web pages, evenly** | Needs the union of policies: no base directory for images, strict raw-HTML handling, no well-formedness assumption. |
| If insertion proves non-undoable | **Ship it; warn once** | The first-run consent dialog (FR-P13) is the user's only protection. No snapshot, no restore mechanism, no recovery path. |
| Delivery timing | **Not in v1** | Sequenced as Phase 6, gated on Phase 4 acceptance. See §9. |

---

## 3. Relationship to the import feature

Reused unchanged from `DESIGN.md`: pass 1 (parse), pass 3 (render), the diagram subsystem, the
sanitization boundary, the style table, the retry policy, localization.

New surface, and the entire cost of the feature:

1. Reading and validating the clipboard
2. Locating the insertion point within an existing page
3. Mutating that page safely
4. A trigger mechanism that is not a ribbon click

Note the shape of that list: **none of it is conversion.** The feature is cheap in the engine and
expensive in the interop and safety layers, which is the opposite of where the import feature's
difficulty sits.

---

## 4. Functional requirements

### 4.1 Invocation and target

- **FR-P1** A ribbon command **Paste as Markdown** is available while a page is open for editing.
- **FR-P2** The command converts clipboard text and inserts the result into the current page at the
  current cursor position.
- **FR-P3** If a range is selected, converted content is inserted **after** the selected content.
  The selection is not deleted. *(Diverges from native paste semantics — see OQ-P3.)*
- **FR-P4** If the insertion point cannot be determined confidently, the add-in appends to the end of
  the current page's primary outline and says so, rather than guessing a position.
- **FR-P5** An optional global hotkey invokes the same command. It is **off by default**, the
  combination is user-configurable, it fires only while OneNote is the foreground window, and a
  registration failure is reported once and never retried silently.
- **FR-P6** The command is unavailable, with a stated reason, when no page is open, when the page is
  read-only, or when the clipboard holds no text.

### 4.2 Content handling

- **FR-P7** Clipboard text is read as `CF_UNICODETEXT` and treated as Markdown without
  auto-detection. Because invocation is explicit, the user's choice is the signal; plain prose
  converts to plain paragraphs and that is a correct outcome.
- **FR-P8** All conversion behaviour matches the import feature: GFM, code highlighting, tables,
  task lists, diagrams, and the raw-HTML policy are identical. There is no second dialect.
- **FR-P9** Clipboard content has **no base directory**, so relative image references cannot be
  resolved. They render as a visible placeholder with a warning. Absolute and UNC paths remain
  rejected per NFR-7. Data-URI images are embedded normally.

### 4.3 Safe mutation

- **FR-P10** The page write uses **optimistic concurrency**: the last-modified timestamp read with
  the page is passed back on write with force disabled. If the user changed the page between read
  and write, the write fails and is reported — it never wins the race by force.
- **FR-P11** The mutation is a single transaction. A failure at any stage leaves the page exactly as
  it was.
- **FR-P12** Content already on the page is never deleted, reordered, or restyled. The operation is
  strictly additive.
- **FR-P13** Before the first insertion into an existing page, the user is shown a one-time
  explanation that the action may not be reversible with `Ctrl+Z`, with an explicit acknowledgement
  and a "don't show again" option.
- **FR-P14** A paste that produces no content (empty or whitespace-only clipboard) makes no change
  to the page.

### 4.4 Feedback

- **FR-P15** A paste with no diagrams completes without any visible progress UI.
- **FR-P16** A paste requiring diagram rendering shows lightweight, cancellable progress. Cancelling
  before the write leaves the page untouched.
- **FR-P17** Warnings (unresolvable images, failed diagrams, dropped HTML) are surfaced
  non-modally after the paste. They never block, and they never suppress the insertion.

---

## 5. Non-functional requirements

- **NFR-P1** A paste of typical content — under 100 lines, no diagrams — completes in under
  300 ms from invocation to visible result. Anything slower stops feeling like paste.
- **NFR-P2** Failure of the paste command must never damage the current page, and never crash
  OneNote (inherits NFR-2).
- **NFR-P3** The global hotkey registration is released on add-in shutdown and on OneNote exit. It
  must not persist, and must not interfere with other applications when OneNote is not foreground.
- **NFR-P4** Web-sourced Markdown makes the sanitization boundary (NFR-4) more load-bearing, not
  less: pasted content lands inside a page the user already trusts. The raw-HTML policy applies
  identically and without relaxation.
- **NFR-P5** No supported build depends on a low-level keyboard hook, a global message filter, or
  window subclassing of the OneNote UI (§6).
- **NFR-P6** Insertion-point resolution is covered by tests over recorded page XML fixtures, not
  only by manual verification, since it is the component that can corrupt user data.

---

## 6. Rejected: intercepting Ctrl+V

Recorded so it is not revisited without new information.

A `WH_KEYBOARD_LL` hook could intercept `Ctrl+V` and substitute the conversion. It is rejected
because:

- A signed Office add-in installing a global keyboard hook is a strong heuristic signal for AV and
  EDR products. NFR-13 already commits to a fight for SmartScreen reputation; this makes it worse.
- It would need to distinguish "clipboard holds Markdown" from "clipboard holds anything else" on
  every paste, silently and correctly. Getting that wrong breaks the ordinary paste key — the single
  most-used editing action in the product.
- It is unsupported by the host and would break without warning across OneNote builds, which NFR-9
  makes a maintenance liability rather than a one-time cost.

The opt-in global hotkey (FR-P5) delivers most of the ergonomic benefit at a fraction of the risk,
and Office's automatic KeyTips give a supported Alt-key path for free.

---

## 7. Amendments to existing documents

| Document | Change | Status |
|---|---|---|
| `REQUIREMENTS.md` FR-21 | Narrowed to *unattended* modification; FR-21a admits the explicit paste command as the sole exception | **applied** |
| `REQUIREMENTS.md` §6 | Out-of-scope bullet on editing existing pages now excepts the paste command | **applied** |
| `DESIGN.md` §5.2 | `IOneNoteGateway` deliberately has no operation that modifies an existing page. It must gain one — guarded so the guarantee stays structural: the operation should require a last-modified token rather than accepting a force flag, making FR-P10 impossible to bypass by omission | **pending `/sc:design`** |
| `DESIGN.md` §2 | Clipboard reading, selection resolution, and hotkey registration are new responsibilities needing a home; none belong in `Core` | **pending `/sc:design`** |
| `DESIGN.md` §13 | D2 ("gateway cannot modify existing pages") needs restating as "cannot modify unattended, and cannot modify without a concurrency token" | **pending `/sc:design`** |

---

## 8. Open questions and spike items

Three of these gate the feature and belong in Phase 0 alongside the existing schema spike.

- **OQ-P1 — Does `UpdatePageContent` participate in OneNote's undo stack?** *(gating)* Determines
  whether FR-P13's warning is a formality or the user's only protection. Test: insert via the API,
  press `Ctrl+Z`, observe.
- **OQ-P2 — How precisely does `GetPageContent(…, PageInfo.piSelection, …)` report a collapsed
  caret?** *(gating)* The `selected` attribute is documented for selected *ranges*; whether a
  blinking cursor with no selection marks its containing element is unverified. If it does not,
  FR-P2 is unachievable as written and the feature degrades to FR-P4 (append) — which is a
  materially different product.
- **OQ-P3 — Should a selected range be replaced rather than appended after?** FR-P3 currently
  preserves it, which is safer but diverges from what every other paste in Windows does. Decide once
  OQ-P2 establishes what is actually detectable.
- **OQ-P5 — Default hotkey combination**, if the user enables it. `Ctrl+Shift+V` is the intuitive
  choice and is also widely claimed by other software.
- **OQ-P7 — Page-size ceiling.** Should a very large clipboard payload be refused rather than
  inserted into a page that may already be large? Relates to NFR-8.

### Closed

IDs are kept stable rather than renumbered, so earlier references stay valid.

- **OQ-P4 — Pre-mutation page snapshots. Withdrawn.** No snapshot is written and no recovery
  mechanism exists; FR-P13's one-time warning stands alone.
- **OQ-P6 — v1 inclusion. Resolved: not in v1.** Paste is Phase 6, entered only after Phase 4
  accepts. Rationale in §9.

---

## 9. Sequencing and next step

**Paste is Phase 6.** It is not part of v1 and is not started until Phase 4 accepts.

The reasoning is the shared engine. Import and paste run the same conversion, so every conversion
bug exists in both — but the two have very different consequences. A bad conversion during import
produces a bad *new* page, which the user deletes in one click. The same bug during paste damages a
page that may hold a year of notes, and with OQ-P4 withdrawn there is nothing to restore it with.
Letting import find those bugs first costs nothing and is the only real mitigation left in the
design.

Phase 4 rather than Phase 3 is the gate because clipboard Markdown from chat assistants routinely
contains Mermaid. The diagram path and its fallback behaviour should be proven against pages the
add-in created before they ever run against pages it did not.

Next:

1. Run **OQ-P1 and OQ-P2** during Phase 0, alongside the schema spike. Both are under an hour, and
   either one can invalidate the chosen design: no undo makes FR-P13 the user's only protection, and
   no caret detection collapses "paste at cursor" into "append to page" — a materially different
   feature. Running them now means the answer is in hand long before Phase 6 begins.
2. Settle **OQ-P3**, **OQ-P5**, and **OQ-P7** at the start of Phase 6, not before — OQ-P3 in
   particular depends on what OQ-P2 reveals.
3. `/sc:design` to fold the paste path into the architecture — principally the guarded gateway
   operation and where selection resolution lives. This can happen at any point; it is design, not
   code, and the §7 amendments to `DESIGN.md` are pending regardless.
