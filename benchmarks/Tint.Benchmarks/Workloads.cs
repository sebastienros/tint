using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using Tint.Grammars;
using Tint.Loading;

namespace Tint.Benchmarks;

/// <summary>Where Tint's grammars come from.</summary>
public enum TintGrammarSet
{
    /// <summary><c>GrammarLoader</c> on the JSON files of <c>grammars/</c>.</summary>
    Loader,

    /// <summary>The <c>Tint.Grammars</c> bundle (generated, interpreted patterns).</summary>
    Bundled,

    /// <summary>The C# grammar generated in this project with ahead-of-time kernels (<c>CSharpKernelsGrammar</c>).</summary>
    Kernels,
}

/// <summary>Files, grammars and themes shared by the benchmarks, located in the checkout.</summary>
public static class Workloads
{
    /// <summary>TextMateSharp's big C# file (133k lines), downloaded on first use.</summary>
    public const string BigFileUrl = "https://raw.githubusercontent.com/danipen/TextMateSharp/master/src/TextMateSharp.Demo/testdata/samplefiles/bigfile.cs";

    /// <summary>The languages of the docs-heavy workload: fixture file name, Tint scope, TextMateSharp scope.</summary>
    public static readonly IReadOnlyDictionary<string, string> DocsScopes = new Dictionary<string, string>
    {
        ["csharp"] = "source.cs",
        ["typescript"] = "source.ts",
        ["javascript"] = "source.js",
        ["html"] = "text.html.basic",
        ["json"] = "source.json",
        ["yaml"] = "source.yaml",
        ["bash"] = "source.shell",
        ["markdown"] = "text.html.markdown",
    };

    /// <summary>The Passenger fixtures (<c>benchmarks/samples/passenger</c>): how many times each file is repeated, as Passenger's harness does.</summary>
    public static readonly IReadOnlyDictionary<string, int> PassengerRepeats = new Dictionary<string, int>
    {
        ["xml.props"] = 97,
        ["json.json"] = 112,
        ["yaml.yml"] = 32,
        ["markdown.md"] = 23,
        ["html.html"] = 14,
        ["css.css"] = 24,
        ["javascript.js"] = 58,
        ["shell.sh"] = 26,
        ["scss.scss"] = 588,
        ["less.less"] = 588,
    };

    /// <summary>The grammar scope of a Passenger fixture, from its extension.</summary>
    public static string PassengerScope(string file) => Path.GetExtension(file) switch
    {
        ".props" => "text.xml",
        ".json" => "source.json",
        ".yml" => "source.yaml",
        ".md" => "text.html.markdown",
        ".html" => "text.html.basic",
        ".css" => "source.css",
        ".js" => "source.js",
        ".sh" => "source.shell",
        ".scss" => "source.css.scss",
        ".less" => "source.css.less",
        _ => throw new ArgumentException($"No scope for '{file}'.", nameof(file)),
    };

    /// <summary>
    /// The lines of a Passenger fixture repeated as Passenger's harness repeats it: line endings normalized to <c>\n</c>,
    /// the file plus a newline repeated <see cref="PassengerRepeats"/> times, split on <c>\n</c>.
    /// </summary>
    public static string[] PassengerLines(string file)
    {
        var text = File.ReadAllText(Sample(Path.Combine("passenger", file))).Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Concat(Enumerable.Repeat(text + "\n", PassengerRepeats[file])).Split('\n');
    }

    /// <summary>The repository root.</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The path of a file under <c>benchmarks/samples</c>.</summary>
    public static string Sample(string name) => Path.Combine(RepositoryRoot, "benchmarks", "samples", name);

    /// <summary>The VS Code C# grammar file.</summary>
    public static string CSharpGrammarPath => Path.Combine(RepositoryRoot, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json");

    /// <summary>A VS Code theme file from <c>themes/theme-defaults</c>.</summary>
    public static string ThemePath(string file) => Path.Combine(RepositoryRoot, "themes", "theme-defaults", file);

    /// <summary>The path of <c>bigfile.cs</c> in <c>benchmarks/samples/.cache</c>, downloading it when missing.</summary>
    public static string BigFile()
    {
        var path = Sample(Path.Combine(".cache", "bigfile.cs"));
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var client = new HttpClient();
            var text = client.GetStringAsync(BigFileUrl).GetAwaiter().GetResult();
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        return path;
    }

    /// <summary>The lines of a sample file: <c>bigfile.cs</c> from the cache, others from <c>benchmarks/samples</c>.</summary>
    public static string[] ReadLines(string file) => File.ReadAllLines(file == "bigfile.cs" ? BigFile() : Sample(file));

    /// <summary>The fenced blocks of <c>benchmarks/samples/docs/&lt;language&gt;.md</c>, each as its lines.</summary>
    public static string[][] DocsBlocks(string language)
    {
        var blocks = new List<string[]>();
        List<string>? current = null;
        foreach (var line in File.ReadAllLines(Sample(Path.Combine("docs", language + ".md"))))
        {
            if (line.StartsWith("````", StringComparison.Ordinal))
            {
                if (current is null)
                {
                    current = [];
                }
                else
                {
                    blocks.Add([.. current]);
                    current = null;
                }
            }
            else
            {
                current?.Add(line);
            }
        }

        return [.. blocks];
    }

    /// <summary>The UTF-8 size of lines joined with <c>\n</c>.</summary>
    public static long Utf8Bytes(IEnumerable<string> lines) => lines.Sum(l => (long)Encoding.UTF8.GetByteCount(l) + 1);

    /// <summary>A registry with the C# grammar: through the loader, from the bundle, or kernel-compiled.</summary>
    public static GrammarRegistry CSharpRegistry(TintGrammarSet grammars = TintGrammarSet.Loader)
    {
        var registry = new GrammarRegistry();
        registry.Add(grammars switch
        {
            TintGrammarSet.Bundled => TintGrammars.CSharp,
            TintGrammarSet.Kernels => CSharpKernelsGrammar.Instance,
            _ => GrammarLoader.Load(CSharpGrammarPath, new GrammarInfo { LanguageId = "csharp" }),
        });
        return registry;
    }

    /// <summary>
    /// A registry with every grammar of <c>grammars/manifest.json</c>: through the loader, from the bundle, or from the
    /// bundle with the kernel-compiled C# grammar in place of the bundled one.
    /// </summary>
    public static GrammarRegistry AllGrammarsRegistry(TokenizerOptions? options = null, TintGrammarSet set = TintGrammarSet.Loader)
    {
        var registry = new GrammarRegistry();
        if (options is not null)
        {
            registry.TokenizerOptions = options;
        }

        if (set != TintGrammarSet.Loader)
        {
            registry.Add([.. TintGrammars.All]);
            if (set == TintGrammarSet.Kernels)
            {
                registry.Add(CSharpKernelsGrammar.Instance);
            }

            return registry;
        }

        var grammars = Path.Combine(RepositoryRoot, "grammars");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(grammars, "manifest.json")));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.RootElement.GetProperty("grammars").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!;
            if (!seen.Add(path))
            {
                continue;
            }

            var info = new GrammarInfo
            {
                LanguageId = entry.GetProperty("language").GetString(),
                EmbeddedLanguages = ReadMap(entry, "embeddedLanguages"),
                InjectTo = entry.TryGetProperty("injectTo", out var injectTo) && injectTo.ValueKind == JsonValueKind.Array
                    ? [.. injectTo.EnumerateArray().Select(e => e.GetString()!)]
                    : [],
                TokenTypes = [.. ReadMap(entry, "tokenTypes").Select(p => new KeyValuePair<string, StandardTokenType>(p.Key, p.Value switch
                {
                    "comment" => StandardTokenType.Comment,
                    "string" => StandardTokenType.String,
                    "regex" => StandardTokenType.RegEx,
                    _ => StandardTokenType.Other,
                }))],
            };
            registry.Add(GrammarLoader.Load(Path.Combine(grammars, path), info));
        }

        return registry;
    }

    /// <summary>A theme from <c>themes/theme-defaults</c>, through the loader.</summary>
    public static Theme LoadTheme(string file) => Theme.FromSource(ThemeLoader.Load(ThemePath(file)));

    /// <summary>
    /// A VS Code theme with its <c>include</c> chain flattened into one JSON document, for libraries that do not
    /// resolve includes (TextMateLib).
    /// </summary>
    public static string FlattenedThemeJson(string file)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var colors = new JsonObject();
        var tokenColors = new JsonArray();
        string? name = null;
        string? type = null;
        void Load(string path)
        {
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: options)!.AsObject();
            if (node["include"]?.GetValue<string>() is { } include)
            {
                Load(Path.Combine(Path.GetDirectoryName(path)!, include));
            }

            name = node["name"]?.GetValue<string>() ?? name;
            type = node["type"]?.GetValue<string>() ?? type;
            if (node["colors"] is JsonObject c)
            {
                foreach (var (key, value) in c)
                {
                    colors[key] = value?.DeepClone();
                }
            }

            if (node["tokenColors"] is JsonArray t)
            {
                foreach (var rule in t)
                {
                    tokenColors.Add(rule?.DeepClone());
                }
            }
        }

        Load(ThemePath(file));
        return new JsonObject { ["name"] = name, ["type"] = type, ["colors"] = colors, ["tokenColors"] = tokenColors }.ToJsonString();
    }

    /// <summary>
    /// A TextMateSharp registry reading the checkout's grammar files (<c>grammars/manifest.json</c>) with Dark+ from
    /// TextMateSharp.Grammars, so a comparison with Tint's loader runs the same grammars.
    /// </summary>
    public static Registry TextMateSharpFileRegistry() => new(new FileRegistryOptions());

    // TextMateSharp 2.0.4 reading the grammar files vscode-textmate reads: two shapes it rejects are fixed first, as
    // tools/Tint.Differential does (strings among captures, which it casts to rules; a begin rule without end or
    // while, which it passes to the regex engine as null).
    private sealed class FileRegistryOptions : IRegistryOptions
    {
        private readonly RegistryOptions _bundled = new(ThemeName.DarkPlus);
        private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

        public FileRegistryOptions()
        {
            var grammars = Path.Combine(RepositoryRoot, "grammars");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(grammars, "manifest.json")));
            foreach (var entry in manifest.RootElement.GetProperty("grammars").EnumerateArray())
            {
                _paths.TryAdd(entry.GetProperty("scopeName").GetString()!, Path.Combine(grammars, entry.GetProperty("path").GetString()!));
            }
        }

        public IRawGrammar? GetGrammar(string scopeName)
        {
            if (!_paths.TryGetValue(scopeName, out var path) || !path.EndsWith(".json", StringComparison.Ordinal))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
            Sanitize(root);
            using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString())));
            return GrammarReader.ReadGrammarSync(reader);
        }

        public ICollection<string> GetInjections(string scopeName) => null!;

        public IRawTheme GetTheme(string scopeName) => _bundled.GetTheme(scopeName);

        public IRawTheme GetDefaultTheme() => _bundled.GetDefaultTheme();

        private static void Sanitize(JsonNode? node)
        {
            if (node is JsonObject rule)
            {
                if (rule.ContainsKey("begin") && !rule.ContainsKey("end") && !rule.ContainsKey("while"))
                {
                    rule["end"] = "";
                }

                foreach (var (key, value) in rule.ToList())
                {
                    if (key is "captures" or "beginCaptures" or "endCaptures" or "whileCaptures" && value is JsonObject captures)
                    {
                        foreach (var (capture, captureRule) in captures.ToList())
                        {
                            if (captureRule is not JsonObject)
                            {
                                captures.Remove(capture);
                            }
                        }
                    }

                    Sanitize(value);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    Sanitize(item);
                }
            }
        }
    }

    private static Dictionary<string, string> ReadMap(JsonElement entry, string property)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in value.EnumerateObject())
            {
                map[pair.Name] = pair.Value.GetString()!;
            }
        }

        return map;
    }

    private static string FindRoot([CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
