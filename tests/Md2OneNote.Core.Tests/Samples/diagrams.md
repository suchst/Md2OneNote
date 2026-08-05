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

A fence whose language has no renderer at all falls back to code:

```plantuml
@startuml
a -> b
@enduml
```
