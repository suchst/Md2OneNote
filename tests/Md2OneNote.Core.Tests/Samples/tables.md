# Tables

A six-column table with a header row:

| Requirement | Area | Priority | Owner | Status | Notes |
|---|---|---|---|---|---|
| FR-1 | Import | High | A | Done | Ribbon entry point |
| FR-9 | Fidelity | High | B | Open | Needs `ColorCode` |
| NFR-4 | Security | High | C | Done | HTML is **dropped** |

A ragged table, which is legal Markdown and must still produce square rows:

| a | b | c |
|---|---|---|
| 1 | 2 |
| 3 |

A table whose cells carry inline formatting and awkward characters:

| Input | Result |
|---|---|
| `a < b && c > d` | escaped |
| [link](https://example.com) | live |
| ~~removed~~ | struck |
