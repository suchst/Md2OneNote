# Code

A fenced block with a language:

```csharp
public sealed class Page
{
    // Indentation has to survive the trip.
    public string Render(string markdown)
    {
        return markdown ?? string.Empty;
    }
}
```

A fenced block with no language:

```
plain  text   with     runs of spaces
	and a leading tab
```

A fenced block whose language has no highlighter and no renderer:

```brainfuck
++++[->++++<]>.
```

An indented block:

    indented four spaces
        and eight

Code containing characters that must not break the page:

```html
<script>alert("x & y")</script>
]]> is not a terminator here
```
