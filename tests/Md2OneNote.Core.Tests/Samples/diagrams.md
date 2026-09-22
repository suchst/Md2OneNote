# Diagrams

A diagram that renders:

```mermaid
graph TD
    A[Start] --> B{Decision}
    B -->|yes| C[Done]
```

A second diagram, which the fixture fails on purpose so the fallback path is covered:

```mermaid
sequenceDiagram
    Alice->>Bob: Hello
```

The same source twice: it is requested once and drawn at both sites.

```mermaid
graph TD
    A[Start] --> B{Decision}
    B -->|yes| C[Done]
```

A Graphviz diagram:

```dot
digraph { a -> b }
```

A formula in a math fence, and the same formula as a dollar block, which is requested once:

```math
\int_0^1 x^2 \, dx = \frac{1}{3}
```

$$
\int_0^1 x^2 \, dx = \frac{1}{3}
$$

Inline dollars are text, not math: it costs $5 and $x^2$ stays as typed.

A fence whose language has no renderer at all falls back to code:

```plantuml
@startuml
a -> b
@enduml
```
