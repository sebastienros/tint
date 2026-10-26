using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tint.Generator;

namespace Tint.Sync;

/// <summary>
/// Writes the MSBuild item lists the <c>Tint.Grammars</c> and <c>Tint.Themes</c> bundles compile:
/// <c>src/Tint.Grammars/VSCodeGrammars.props</c> from <c>grammars/manifest.json</c> and
/// <c>src/Tint.Themes/VSCodeThemes.props</c> from <c>themes/manifest.json</c>.
/// </summary>
/// <remarks>
/// Class names are written explicitly, with the generator's own naming rules, so that a later sync never renames a
/// public class by accident. The output is sorted and uses <c>\n</c> line endings so it is byte-identical on every OS.
/// </remarks>
internal static class BundleProps
{
    private static readonly JsonDocumentOptions s_lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static void Write(string root)
    {
        var grammars = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "grammars", "manifest.json")), documentOptions: s_lenient)!;
        var themes = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "themes", "manifest.json")), documentOptions: s_lenient)!;

        var grammarProps = WriteGrammars(grammars, out var grammarCount);
        var themeProps = WriteThemes(themes, out var themeCount);
        WriteFile(Path.Combine(root, "src", "Tint.Grammars", "VSCodeGrammars.props"), grammarProps);
        WriteFile(Path.Combine(root, "src", "Tint.Themes", "VSCodeThemes.props"), themeProps);
        Console.WriteLine($"Bundle items:            {grammarCount} grammars, {themeCount} themes");
    }

    internal static string WriteGrammars(JsonNode manifest, out int count)
    {
        // One item per file; a file registered for several languages keeps one LanguageId and every alias and extension.
        var files = manifest["grammars"]!.AsArray()
            .Select(n => n!)
            .GroupBy(n => (string)n["path"]!, StringComparer.Ordinal)
            .Select(group => GrammarItem.From(group.ToList()))
            .OrderBy(item => item.ScopeName, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ToList();

        var names = AssignNames(files, GrammarClassName, item => Naming.PascalCase(item.ScopeName.Split('.')[0]));

        var builder = Header(manifest, "grammars/manifest.json", "Tint.Grammars");
        builder.Append("  <ItemGroup>\n");
        foreach (var item in files.OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            builder.Append("    <TintGrammar Include=\"$(MSBuildThisFileDirectory)../../grammars/").Append(Escape(item.Path)).Append('"');
            Attribute(builder, "Name", names[item]);
            Attribute(builder, "Accessibility", "public");
            Attribute(builder, "LanguageId", item.LanguageId);
            Attribute(builder, "Aliases", item.Aliases);
            Attribute(builder, "FileExtensions", item.Extensions);
            Attribute(builder, "EmbeddedLanguages", item.EmbeddedLanguages);
            Attribute(builder, "InjectTo", item.InjectTo);
            Attribute(builder, "TokenTypes", item.TokenTypes);
            if (item.BalancedBracketScopes is { } balanced)
            {
                Attribute(builder, "BalancedBracketScopes", balanced);
            }

            Attribute(builder, "UnbalancedBracketScopes", item.UnbalancedBracketScopes);
            builder.Append(" />\n");
        }

        builder.Append("  </ItemGroup>\n");
        builder.Append("</Project>\n");
        count = files.Count;
        return builder.ToString();
    }

    // Bundle class names are public API, so they are chosen here rather than left to the generator's scope-based
    // default (which stays as it is for user projects): an explicit override for the scope, else the language id in
    // PascalCase with s_languageNames spelling out the ids PascalCase alone gets wrong, else the generator's rule on
    // the scope name. BundleTests pins the resulting list, so a sync cannot rename a class silently.
    private static string GrammarClassName(GrammarItem item)
    {
        if (s_scopeNames.TryGetValue(item.ScopeName, out var name))
        {
            return name;
        }

        if (item.LanguageId is { } language)
        {
            return (s_languageNames.TryGetValue(language, out var spelled) ? spelled : Naming.PascalCase(language)) + "Grammar";
        }

        return Naming.GrammarClassName(item.ScopeName);
    }

    // Grammars without a language (included by others or injected), and the second grammar of a language whose name
    // the language's main grammar takes: php is text.html.php, as VS Code maps it, and cpp is source.cpp.
    private static readonly Dictionary<string, string> s_scopeNames = new(StringComparer.Ordinal)
    {
        ["documentation.injection.js.jsx"] = "JsDocJavaScriptInjectionGrammar",
        ["documentation.injection.ts"] = "JsDocTypeScriptInjectionGrammar",
        ["markdown.math.block"] = "MarkdownMathBlockGrammar",
        ["markdown.math.codeblock"] = "MarkdownMathCodeBlockGrammar",
        ["markdown.math.inline"] = "MarkdownMathInlineGrammar",
        ["source.c.platform"] = "CPlatformGrammar",
        ["source.cpp.embedded.macro"] = "CppMacroGrammar",
        ["source.js.regexp"] = "JavaScriptRegexpGrammar",
        ["source.php"] = "PhpSourceGrammar",
        ["source.regexp.python"] = "PythonRegexpGrammar",
        ["source.sassdoc"] = "SassdocGrammar",
        ["source.yaml.1.0"] = "Yaml10Grammar",
        ["source.yaml.1.1"] = "Yaml11Grammar",
        ["source.yaml.1.2"] = "Yaml12Grammar",
        ["source.yaml.1.3"] = "Yaml13Grammar",
        ["source.yaml.embedded"] = "YamlEmbeddedGrammar",
        ["text.html.basic"] = "HtmlBasicGrammar",
    };

    // Language ids whose PascalCase is not the name: word boundaries inside the id, and ids named after something else.
    private static readonly Dictionary<string, string> s_languageNames = new(StringComparer.Ordinal)
    {
        ["bat"] = "Batch",
        ["coffeescript"] = "CoffeeScript",
        ["cpp_embedded_latex"] = "CppEmbeddedLatex",
        ["csharp"] = "CSharp",
        ["cuda-cpp"] = "CudaCpp",
        ["fsharp"] = "FSharp",
        ["git-commit"] = "GitCommit",
        ["git-rebase"] = "GitRebase",
        ["html"] = "Html",
        ["jade"] = "Pug",
        ["javascript"] = "JavaScript",
        ["javascriptreact"] = "JavaScriptReact",
        ["jsonl"] = "JsonLines",
        ["latex"] = "Latex",
        ["markdown-math"] = "MarkdownMath",
        ["markdown_latex_combined"] = "MarkdownLatexCombined",
        ["objective-c"] = "ObjectiveC",
        ["objective-cpp"] = "ObjectiveCpp",
        ["powershell"] = "PowerShell",
        ["razor"] = "Razor",
        ["restructuredtext"] = "RestructuredText",
        ["search-result"] = "SearchResult",
        ["shaderlab"] = "ShaderLab",
        ["shellscript"] = "ShellScript",
        ["typescript"] = "TypeScript",
        ["typescriptreact"] = "TypeScriptReact",
        ["vb"] = "VisualBasic",
        ["xsl"] = "Xsl",
    };

    internal static string WriteThemes(JsonNode manifest, out int count)
    {
        var themes = manifest["themes"]!.AsArray()
            .Select(n => (Label: (string)n!["label"]!, Path: (string)n["path"]!, Include: (string?)n["include"]))
            .OrderBy(t => t.Path, StringComparer.Ordinal)
            .ToList();

        var names = AssignNames(themes, t => Naming.ThemeClassName(t.Label), _ => "");

        // Includes that are not themselves bundled themes still have to reach the generator as AdditionalFiles.
        var themePaths = themes.Select(t => t.Path).ToHashSet(StringComparer.Ordinal);
        var includes = themes
            .Where(t => t.Include is not null)
            .Select(t => ResolveInclude(t.Path, t.Include!))
            .Where(path => !themePaths.Contains(path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var builder = Header(manifest, "themes/manifest.json", "Tint.Themes");
        builder.Append("  <ItemGroup>\n");
        foreach (var theme in themes)
        {
            builder.Append("    <TintTheme Include=\"$(MSBuildThisFileDirectory)../../themes/").Append(Escape(theme.Path)).Append('"');
            Attribute(builder, "Name", names[theme]);
            Attribute(builder, "Accessibility", "public");
            builder.Append(" />\n");
        }

        builder.Append("  </ItemGroup>\n");
        if (includes.Count > 0)
        {
            builder.Append("  <ItemGroup>\n");
            foreach (var include in includes)
            {
                builder.Append("    <AdditionalFiles Include=\"$(MSBuildThisFileDirectory)../../themes/").Append(Escape(include)).Append("\" />\n");
            }

            builder.Append("  </ItemGroup>\n");
        }

        builder.Append("</Project>\n");
        count = themes.Count;
        return builder.ToString();
    }

    // First come, first served in the given order; a later collision is prefixed (source.x vs text.x), then numbered.
    private static Dictionary<T, string> AssignNames<T>(IReadOnlyList<T> items, Func<T, string> name, Func<T, string> prefix)
        where T : notnull
    {
        var names = new Dictionary<T, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var candidate = name(item);
            if (used.Contains(candidate) && prefix(item) is { Length: > 0 } p)
            {
                candidate = p + candidate;
            }

            var unique = candidate;
            for (var i = 2; used.Contains(unique); i++)
            {
                unique = candidate.EndsWith("Grammar", StringComparison.Ordinal) || candidate.EndsWith("Theme", StringComparison.Ordinal)
                    ? candidate.Insert(candidate.Length - (candidate.EndsWith("Theme", StringComparison.Ordinal) ? 5 : 7), i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : candidate + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            used.Add(unique);
            names[item] = unique;
        }

        return names;
    }

    private static string ResolveInclude(string themePath, string include)
    {
        var parts = themePath.Split('/').SkipLast(1).ToList();
        foreach (var segment in include.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(segment);
            }
        }

        return string.Join('/', parts);
    }

    private static StringBuilder Header(JsonNode manifest, string source, string project)
    {
        var builder = new StringBuilder();
        builder.Append("<Project>\n");
        builder.Append($"  <!-- Generated by tools/Tint.Sync from {source} (microsoft/vscode@{(string?)manifest["commit"]}); do not edit by hand.\n");
        builder.Append($"       Imported by {project}.csproj; paths are relative to this file. -->\n");
        return builder;
    }

    private static void Attribute(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');
        }
    }

    private static void Attribute(StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            builder.Append(' ').Append(name).Append("=\"").Append(string.Join(';', values.Select(Escape))).Append('"');
        }
    }

    // MSBuild escapes (%XX) for its special characters, then XML escapes for the attribute.
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '%' => "%25",
                '$' => "%24",
                '@' => "%40",
                '\'' => "%27",
                ';' => "%3B",
                '*' => "%2A",
                '?' => "%3F",
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    private sealed record GrammarItem(
        string ScopeName,
        string Path,
        string? LanguageId,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> Extensions,
        IReadOnlyList<string> EmbeddedLanguages,
        IReadOnlyList<string> InjectTo,
        IReadOnlyList<string> TokenTypes,
        IReadOnlyList<string>? BalancedBracketScopes,
        IReadOnlyList<string> UnbalancedBracketScopes)
    {
        // Registrations of one file, in manifest order. The language whose id is the scope's last segment (yaml for
        // source.yaml over dockercompose) names the grammar, else the first registration; its metadata is kept and
        // the other registrations only add aliases and extensions.
        public static GrammarItem From(List<JsonNode> registrations)
        {
            var scopeName = (string)registrations[0]["scopeName"]!;
            var lastSegment = scopeName.Split('.')[^1];
            var primary = registrations.FirstOrDefault(r => string.Equals((string?)r["language"], lastSegment, StringComparison.Ordinal)) ?? registrations[0];
            var ordered = registrations.Where(r => r != primary).Prepend(primary).ToList();

            return new GrammarItem(
                scopeName,
                (string)primary["path"]!,
                (string?)primary["language"],
                ordered.SelectMany(r => Strings(r["aliases"])).Distinct(StringComparer.Ordinal).ToList(),
                ordered.SelectMany(r => Strings(r["extensions"])).Distinct(StringComparer.Ordinal).ToList(),
                Pairs(primary["embeddedLanguages"]),
                Strings(primary["injectTo"]),
                Pairs(primary["tokenTypes"]),
                primary["balancedBracketScopes"] is JsonArray ? Strings(primary["balancedBracketScopes"]) : null,
                Strings(primary["unbalancedBracketScopes"]));
        }

        private static List<string> Strings(JsonNode? node)
            => node is JsonArray array ? array.Where(v => v?.GetValueKind() == JsonValueKind.String).Select(v => (string)v!).ToList() : [];

        private static List<string> Pairs(JsonNode? node)
            => node is JsonObject map
                ? map.Where(p => p.Value?.GetValueKind() == JsonValueKind.String).Select(p => p.Key + "=" + (string)p.Value!).ToList()
                : [];
    }
}
