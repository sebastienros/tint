# HTML output

`Highlighter.ToHtml` and `Tint.Rendering.HtmlRenderer` write markup in the shape popularized by
[shiki](https://shiki.style/guide/), the highlighter most static-site generators use. Tint does not
depend on shiki; it follows the same conventions so that stylesheets, line-highlight transformers
and copy buttons written for shiki output apply to Tint output unchanged, and a site migrating from
one to the other renders identically.

## Shape

```csharp
var registry = new GrammarRegistry();
registry.Add(TintGrammars.CSharp);

var highlighter = registry.CreateHighlighter("source.cs", Theme.FromSource(TintThemes.DarkPlus));
string html = highlighter.ToHtml("// Greets the caller.\npublic static string Greet(string name) => $\"Hello, {name}!\";\n");
```

```html
<pre class="tint" style="background-color:#1e1e1e;color:#d4d4d4" tabindex="0"><code><span class="line"><span style="color:#6a9955">// Greets the caller.</span></span>
<span class="line"><span style="color:#569cd6">public</span> <span style="color:#569cd6">static</span> <span style="color:#569cd6">string</span> <span style="color:#dcdcaa">Greet</span>(<span style="color:#569cd6">string</span> <span style="color:#9cdcfe">name</span>) =&gt; <span style="color:#ce9178">$"Hello, </span>{<span style="color:#9cdcfe">name</span>}<span style="color:#ce9178">!"</span>;</span></code></pre>
```

The conventions, top to bottom:

- `<pre class="tint">` carries the theme's `editor.background` and `editor.foreground` inline and
  `tabindex="0"`, so a block that scrolls horizontally is reachable from the keyboard. The class is
  `HtmlRendererOptions.PreClass`.
- `<code>` holds one `<span class="line">` per line. Lines are separated by a newline text node, not
  `<br>`, so copying the block keeps its line breaks and CSS can target a line (`.line:nth-child`,
  or the `data-line` attribute when `HtmlRendererOptions.LineNumbers` is on).
- Inside a line, one `<span style="…">` per run of tokens that share a style. Only the properties that
  differ from the theme defaults are written (`color`, `background-color`, `font-style:italic`,
  `font-weight:bold`, `text-decoration:underline` or `line-through`); text in the default color stays
  outside any span, as `(`, `)`, `{`, `}` and `;` above.
- Text is HTML-escaped (`=&gt;` above). Nothing else is added: no line numbers, no language class, no
  wrapper `div`, unless options ask for it.

A theme is self-sufficient: every color comes from the TextMate theme and is written inline, so the
output needs no stylesheet to render correctly.

## CSS-class mode

`HtmlStyleMode.CssClasses` replaces inline styles with classes (`tint-fg-<id>`, `tint-bg-<id>`,
`tint-i`, `tint-b`, `tint-u`, `tint-s`) and `GetStyleSheet()` returns the matching rules for the
theme. Use it when many blocks share one theme and the page should carry the colors once, or when a
content security policy forbids inline styles. The class prefix is `HtmlRendererOptions.ClassPrefix`.

## Several themes

With a light and a dark theme, the default theme's values are written inline and the others as CSS
custom properties on the same spans, the scheme shiki documents for
[dual themes](https://shiki.style/guide/dual-themes):

```csharp
var highlighter = registry.CreateHighlighter("source.cs",
    light: Theme.FromSource(TintThemes.LightPlus), dark: Theme.FromSource(TintThemes.DarkPlus));
```

```html
<pre class="tint" style="background-color:#ffffff;--tint-dark-bg:#1e1e1e;color:#000000;--tint-dark:#d4d4d4" tabindex="0"><code><span class="line"><span style="color:#008000;--tint-dark:#6a9955">// Greets the caller.</span></span>
<span class="line"><span style="color:#0000ff;--tint-dark:#569cd6">public</span> <span style="color:#0000ff;--tint-dark:#569cd6">static</span> <span style="color:#0000ff;--tint-dark:#569cd6">string</span> <span style="color:#795e26;--tint-dark:#dcdcaa">Greet</span>(<span style="color:#0000ff;--tint-dark:#569cd6">string</span> <span style="color:#001080;--tint-dark:#9cdcfe">name</span>) =&gt; <span style="color:#a31515;--tint-dark:#ce9178">$"Hello, </span><span style="color:#000000ff;--tint-dark:inherit">{</span><span style="color:#001080;--tint-dark:#9cdcfe">name</span><span style="color:#000000ff;--tint-dark:inherit">}</span><span style="color:#a31515;--tint-dark:#ce9178">!"</span>;</span></code></pre>
```

The variables are `--tint-<name>` for the color, `--tint-<name>-bg`, `--tint-<name>-font-style`,
`--tint-<name>-font-weight` and `--tint-<name>-text-decoration`; a token the other theme leaves at
its default gets `inherit`, so the switch below never keeps a stale value. One rule flips the page:

```css
@media (prefers-color-scheme: dark) {
  .tint, .tint span {
    color: var(--tint-dark) !important;
    background-color: var(--tint-dark-bg) !important;
    font-style: var(--tint-dark-font-style) !important;
    font-weight: var(--tint-dark-font-weight) !important;
    text-decoration: var(--tint-dark-text-decoration) !important;
  }
}
```

Additional named themes work the same way: `CreateHighlighter(scope, defaultTheme, [new("dim", dim)])`
writes `--tint-dim`. The variable prefix is `HtmlRendererOptions.CssVariablePrefix`.

## Differences from shiki

- The default `pre` class is `tint`, not `shiki`, and no theme-name class is added; set `PreClass`
  to match an existing stylesheet.
- Colors are written as the theme declares them (lower-cased hex, alpha kept when present, as the
  `#000000ff` above from Light+).
- Lines are separated by `\n` only; shiki offers transformers that can change this. Tint exposes
  `HtmlRenderer.Render` over tokenizers of your own instead.
