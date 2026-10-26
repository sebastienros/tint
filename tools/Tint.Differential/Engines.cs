using System.Diagnostics;
using System.Reflection;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using Tint.Grammars;
using Tint.Loading;

namespace Tint.Differential;

/// <summary>A token of <c>TokenizeLine</c>: its range and its scopes joined by spaces.</summary>
public readonly record struct ScopeToken(int Start, int End, string Scopes);

/// <summary>A token of <c>TokenizeLine2</c> reduced to its style: where it starts and the resolved colors.</summary>
public readonly record struct StyleToken(int Start, string Foreground, string Background, int FontStyle);

/// <summary>What one engine produced for one line.</summary>
public sealed class LineOutput
{
    /// <summary>The tokens of <c>TokenizeLine</c>, normalized by <see cref="Comparison"/>.</summary>
    public ScopeToken[] Tokens { get; init; } = [];

    /// <summary>The tokens of <c>TokenizeLine2</c>, normalized by <see cref="Comparison"/>.</summary>
    public StyleToken[] Styles { get; init; } = [];

    /// <summary>Whether the line ran out of the time limit.</summary>
    public bool StoppedEarly { get; init; }

    /// <summary>The exception type and message, when the engine threw.</summary>
    public string? Exception { get; init; }

    /// <summary>The time spent in the engine, in <see cref="Stopwatch"/> ticks.</summary>
    public long Ticks { get; init; }
}

/// <summary>Tokenizes the lines of one document with one grammar, carrying the state. Not thread-safe.</summary>
public interface ILineTokenizer
{
    /// <summary>Tokenizes the next line (<c>TokenizeLine</c>, then <c>TokenizeLine2</c> from the same state).</summary>
    LineOutput Tokenize(string line);

    /// <summary>Goes back to the initial state, for a new document.</summary>
    void Reset();
}

/// <summary>An engine able to create tokenizers per grammar scope.</summary>
public interface IEngine
{
    /// <summary>A short name for reports.</summary>
    string Name { get; }

    /// <summary>The library version.</summary>
    string Version { get; }

    /// <summary>A tokenizer for the grammar, or null when the engine has no such grammar.</summary>
    ILineTokenizer? CreateTokenizer(string scopeName, TimeSpan timeLimit);
}

/// <summary>
/// Tint with the bundled grammars (<see cref="TintGrammars.All"/>) and Dark+ read as written
/// (<see cref="ThemeLoadMode.Raw"/>), as vscode-textmate and TextMateSharp read it.
/// </summary>
public sealed class TintEngine : IEngine
{
    private readonly GrammarRegistry _registry = new();
    private readonly Theme _theme;

    /// <summary>Registers every bundled grammar.</summary>
    public TintEngine(RepositoryData repository)
    {
        _registry.Add([.. TintGrammars.All]);
        _theme = Theme.FromSource(ThemeLoader.Load(Path.Combine(repository.ThemesDirectory, "dark_plus.json"), ThemeLoadMode.Raw));
    }

    /// <inheritdoc/>
    public string Name => "Tint";

    /// <inheritdoc/>
    public string Version => InformationalVersion(typeof(Tokenizer).Assembly);

    /// <inheritdoc/>
    public ILineTokenizer? CreateTokenizer(string scopeName, TimeSpan timeLimit)
    {
        if (!_registry.TryGet(scopeName, out _))
        {
            return null;
        }

        var tokenizer = _registry.CreateTokenizer(scopeName, _theme);
        tokenizer.TimeBudget = timeLimit == TimeSpan.Zero ? null : timeLimit;
        return new TintLineTokenizer(tokenizer, _theme);
    }

    // The innermost stack frame, to tell exceptions apart in reports.
    internal static string Frame(Exception e) => Environment.GetEnvironmentVariable("TINT_DIFF_STACK") == "1" ? e.ToString().ReplaceLineEndings(" / ") : e.StackTrace?.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    internal static string InformationalVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
           ?? assembly.GetName().Version?.ToString() ?? "?";

    private sealed class TintLineTokenizer(Tokenizer tokenizer, Theme theme) : ILineTokenizer
    {
        private StateStack _state = StateStack.Initial;

        public LineOutput Tokenize(string line)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                var tokens = tokenizer.TokenizeLine(line, _state, out var next);
                var stopped = tokenizer.StoppedEarly;
                var scopeTokens = new ScopeToken[tokens.Length];
                for (var i = 0; i < tokens.Length; i++)
                {
                    scopeTokens[i] = new ScopeToken(tokens[i].Start, tokens[i].End, tokens[i].Scopes.ToString());
                }

                var encoded = tokenizer.TokenizeLineEncoded(line, _state, out _);
                stopped |= tokenizer.StoppedEarly;
                var styles = new StyleToken[encoded.Length];
                for (var i = 0; i < encoded.Length; i++)
                {
                    var metadata = encoded[i].Metadata;
                    styles[i] = new StyleToken(
                        encoded[i].Start,
                        theme.ColorMap.GetColor(EncodedTokenAttributes.GetForeground(metadata)),
                        theme.ColorMap.GetColor(EncodedTokenAttributes.GetBackground(metadata)),
                        (int)EncodedTokenAttributes.GetFontStyle(metadata));
                }

                _state = next;
                var ticks = Stopwatch.GetTimestamp() - start;
                return new LineOutput
                {
                    Tokens = Comparison.NormalizeTokens(scopeTokens, line.Length),
                    Styles = Comparison.NormalizeStyles(styles, line.Length),
                    StoppedEarly = stopped,
                    Ticks = ticks,
                };
            }
            catch (Exception e)
            {
                return new LineOutput { Exception = $"{e.GetType().Name}: {e.Message} {Frame(e)}", Ticks = Stopwatch.GetTimestamp() - start };
            }
        }

        public void Reset() => _state = StateStack.Initial;
    }
}

/// <summary>Which grammar files TextMateSharp reads.</summary>
public enum ReferenceGrammars
{
    /// <summary>The checkout's <c>grammars/</c> and <c>themes/</c>, the files Tint's bundles are generated from: differences come from the engines.</summary>
    Same,

    /// <summary>The grammars and themes embedded in TextMateSharp.Grammars 2.0.4: differences also come from grammar versions.</summary>
    Bundled,
}

/// <summary>TextMateSharp 2.0.4 (Onigwrap, native Oniguruma) with Dark+.</summary>
public sealed class TextMateSharpEngine : IEngine
{
    private readonly ReferenceGrammars _grammars;
    private readonly RepositoryData _repository;

    /// <summary>Creates the reference engine.</summary>
    public TextMateSharpEngine(ReferenceGrammars grammars, RepositoryData repository)
    {
        _grammars = grammars;
        _repository = repository;
    }

    /// <inheritdoc/>
    public string Name => "TextMateSharp";

    /// <inheritdoc/>
    public string Version => TintEngine.InformationalVersion(typeof(IGrammar).Assembly) + (_grammars == ReferenceGrammars.Bundled ? " (bundled grammars)" : " (Tint's grammar files)");

    /// <inheritdoc/>
    public ILineTokenizer? CreateTokenizer(string scopeName, TimeSpan timeLimit)
    {
        // One registry per tokenizer: TextMateSharp's registry is not documented as thread-safe.
        IRegistryOptions options = _grammars == ReferenceGrammars.Bundled ? new RegistryOptions(ThemeName.DarkPlus) : new FileRegistryOptions(_repository);
        var registry = new Registry(options);
        var grammar = registry.LoadGrammar(scopeName);
        if (grammar is null)
        {
            return null;
        }

        return new ReferenceLineTokenizer(grammar, [.. registry.GetColorMap()], timeLimit == TimeSpan.Zero ? TimeSpan.MaxValue : timeLimit);
    }

    /// <summary>The scope name and <c>version</c> header of every grammar TextMateSharp.Grammars embeds.</summary>
    public static IReadOnlyDictionary<string, string?> BundledGrammarVersions()
    {
        var assembly = typeof(RegistryOptions).Assembly;
        var versions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(".json", StringComparison.Ordinal) || !name.Contains(".Grammars.", StringComparison.Ordinal) || name.EndsWith("package.json", StringComparison.Ordinal)
                || name.EndsWith("package.nls.json", StringComparison.Ordinal) || name.EndsWith("cgmanifest.json", StringComparison.Ordinal) || name.Contains("configuration", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = System.Text.Json.JsonDocument.Parse(stream, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("scopeName", out var scope))
            {
                versions[scope.GetString()!] = document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
            }
        }

        return versions;
    }

    private sealed class ReferenceLineTokenizer(IGrammar grammar, string[] colorMap, TimeSpan timeLimit) : ILineTokenizer
    {
        private IStateStack? _state;

        public LineOutput Tokenize(string line)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                var result = grammar.TokenizeLine(line, _state, timeLimit);
                var scopeTokens = new ScopeToken[result.Tokens.Length];
                for (var i = 0; i < scopeTokens.Length; i++)
                {
                    var token = result.Tokens[i];
                    scopeTokens[i] = new ScopeToken(token.StartIndex, token.EndIndex, string.Join(' ', token.Scopes));
                }

                var result2 = grammar.TokenizeLine2(line, _state, timeLimit);
                var encoded = result2.Tokens;
                var styles = new StyleToken[encoded.Length / 2];
                for (var i = 0; i < styles.Length; i++)
                {
                    var metadata = (uint)encoded[2 * i + 1];
                    styles[i] = new StyleToken(
                        encoded[2 * i],
                        Color(EncodedTokenAttributes.GetForeground(metadata)),
                        Color(EncodedTokenAttributes.GetBackground(metadata)),
                        (int)EncodedTokenAttributes.GetFontStyle(metadata));
                }

                var stopped = StoppedEarly(result) || StoppedEarly(result2);
                _state = result.RuleStack;
                var ticks = Stopwatch.GetTimestamp() - start;
                return new LineOutput
                {
                    Tokens = Comparison.NormalizeTokens(scopeTokens, line.Length),
                    Styles = Comparison.NormalizeStyles(styles, line.Length),
                    StoppedEarly = stopped,
                    Ticks = ticks,
                };
            }
            catch (Exception e)
            {
                return new LineOutput { Exception = $"{e.GetType().Name}: {e.Message} {TintEngine.Frame(e)}", Ticks = Stopwatch.GetTimestamp() - start };
            }
        }

        public void Reset() => _state = null;

        // TextMateSharp's color map lists its colors by id from 1; id 0 is "no color".
        private string Color(int id) => id > 0 && id <= colorMap.Length ? colorMap[id - 1] : string.Empty;

        // StoppedEarly is public on TextMateSharp's internal result classes only.
        private static bool StoppedEarly(object result)
            => result.GetType().GetProperty("StoppedEarly")?.GetValue(result) is true;
    }

    // Reads the checkout's grammar and theme files, with injections from grammars/manifest.json (TextMateSharp's own
    // RegistryOptions returns none).
    private sealed class FileRegistryOptions(RepositoryData repository) : IRegistryOptions
    {
        public IRawGrammar? GetGrammar(string scopeName)
        {
            var grammar = repository.Grammars.FirstOrDefault(g => g.ScopeName == scopeName);
            if (grammar is null)
            {
                return null;
            }

            var path = repository.GrammarPath(grammar);
            var json = grammar.Format == "plist" ? PlistToJson.Convert(File.ReadAllText(path)) : File.ReadAllText(path);
            json = SanitizeForReference(json);
            using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
            return GrammarReader.ReadGrammarSync(reader);
        }

        // Two shapes vscode-textmate accepts and TextMateSharp 2.0.4 does not, fixed for the reference only:
        // - strings among captures ("comment", "name", "end" in VS Code's XML and YAML grammars), which
        //   vscode-textmate ignores and TextMateSharp casts to rules (InvalidCastException in Raw.TryGetObject);
        // - a begin rule without end or while (XML's malformed comment rule), which vscode-textmate compiles with
        //   "\uFFFF" as its end and TextMateSharp passes to Regex.Match as null (ArgumentNullException).
        private static string SanitizeForReference(string json)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            Visit(root);
            return root.ToJsonString();

            static void Visit(System.Text.Json.Nodes.JsonNode? node)
            {
                if (node is System.Text.Json.Nodes.JsonObject obj)
                {
                    if (obj.ContainsKey("begin") && !obj.ContainsKey("end") && !obj.ContainsKey("while"))
                    {
                        obj["end"] = "\uFFFF";
                    }

                    foreach (var (key, value) in obj.ToList())
                    {
                        if (key is "captures" or "beginCaptures" or "endCaptures" or "whileCaptures" && value is System.Text.Json.Nodes.JsonObject captures)
                        {
                            foreach (var (capture, rule) in captures.ToList())
                            {
                                if (rule is not System.Text.Json.Nodes.JsonObject)
                                {
                                    captures.Remove(capture);
                                }
                            }
                        }

                        Visit(value);
                    }
                }
                else if (node is System.Text.Json.Nodes.JsonArray array)
                {
                    foreach (var item in array)
                    {
                        Visit(item);
                    }
                }
            }
        }

        public ICollection<string> GetInjections(string scopeName)
            => [.. repository.Grammars.Where(g => g.InjectTo.Contains(scopeName)).Select(g => g.ScopeName)];

        public IRawTheme? GetTheme(string scopeName)
        {
            var theme = Flatten(Path.GetFileName(scopeName));
            if (theme is null)
            {
                return null;
            }

            using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(theme.ToJsonString())));
            return ThemeReader.ReadThemeSync(reader);
        }

        // VS Code flattens an included theme's rules before its own, into one rule list. TextMateSharp 2.0.4 keeps the
        // include in a second trie consulted only after the theme's own matches (so "support.type" in dark_plus.json
        // wins over "support.type.property-name" in dark_vs.json): give it the flattened theme instead. Its JSON
        // reader also does not skip the comments and trailing commas VS Code's themes have.
        private System.Text.Json.Nodes.JsonObject? Flatten(string fileName)
        {
            var path = Path.Combine(repository.ThemesDirectory, fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var theme = System.Text.Json.Nodes.JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
            if (theme["include"] is { } include && Flatten(Path.GetFileName((string)include!)) is { } included)
            {
                theme.Remove("include");
                var rules = new System.Text.Json.Nodes.JsonArray();
                foreach (var rule in (included["tokenColors"] as System.Text.Json.Nodes.JsonArray ?? []).Concat(theme["tokenColors"] as System.Text.Json.Nodes.JsonArray ?? []))
                {
                    rules.Add(rule?.DeepClone());
                }

                theme["tokenColors"] = rules;
            }

            return theme;
        }

        public IRawTheme? GetDefaultTheme() => GetTheme("dark_plus.json");
    }
}
