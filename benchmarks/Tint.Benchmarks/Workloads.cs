using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tint.Grammars;
using Tint.Loading;

namespace Tint.Benchmarks;

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

    /// <summary>A registry with the C# grammar, through the loader.</summary>
    public static GrammarRegistry CSharpRegistry()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(CSharpGrammarPath, new GrammarInfo { LanguageId = "csharp" }));
        return registry;
    }

    /// <summary>A registry with every grammar of <c>grammars/manifest.json</c>, through the loader.</summary>
    public static GrammarRegistry AllGrammarsRegistry(TokenizerOptions? options = null)
    {
        var registry = new GrammarRegistry();
        if (options is not null)
        {
            registry.TokenizerOptions = options;
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
