using System.Text;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tint.Generator;

internal enum SourceKind
{
    Grammar,
    Theme,
}

// Project-wide options (build_property.*).
internal sealed record GlobalOptions(string? ProjectDirectory, string? RootNamespace, bool Kernels, string? GregsExPackagePath)
{
    public static GlobalOptions From(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.projectdir", out var projectDir);
        options.TryGetValue("build_property.rootnamespace", out var rootNamespace);
        options.TryGetValue("build_property.tintkernels", out var kernels);
        options.TryGetValue("build_property.tintgregsexpackagepath", out var gregsEx);
        return new GlobalOptions(
            string.IsNullOrWhiteSpace(projectDir) ? null : PathUtilities.Normalize(projectDir!, null),
            string.IsNullOrWhiteSpace(rootNamespace) ? null : rootNamespace!.Trim(),
            IsTrue(kernels),
            string.IsNullOrWhiteSpace(gregsEx) ? null : gregsEx!.Trim());
    }

    public static bool IsTrue(string? value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

// What the generator needs from the compilation, reduced to values so the pipeline caches.
internal sealed record CompilationFacts(string? GregsExReferencePath, bool SupportsKernels);

// The grammar and theme options shared by item metadata and attribute properties.
internal sealed record SourceSettings(
    string? LanguageId,
    EquatableArray<string> Aliases,
    EquatableArray<string> FileExtensions,
    EquatableArray<string> EmbeddedLanguages,
    EquatableArray<string> InjectTo,
    string? InjectionSelector,
    EquatableArray<string> TokenTypes,
    EquatableArray<string>? BalancedBracketScopes,
    EquatableArray<string> UnbalancedBracketScopes,
    string? ThemeMode)
{
    public static SourceSettings Empty { get; } = new(null, default, default, default, default, null, default, null, default, null);

    public static SourceSettings FromMetadata(Func<string, string?> get) => new(
        NullIfEmpty(get("LanguageId")),
        List(get("Aliases")),
        List(get("FileExtensions")),
        List(get("EmbeddedLanguages")),
        List(get("InjectTo")),
        NullIfEmpty(get("InjectionSelector")),
        List(get("TokenTypes")),
        string.IsNullOrWhiteSpace(get("BalancedBracketScopes")) ? null : List(get("BalancedBracketScopes")),
        List(get("UnbalancedBracketScopes")),
        NullIfEmpty(get("ThemeMode")));

    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    // MSBuild lists, which Tint.props turns comma-separated (the analyzer config file cuts values at ';'); blanks dropped.
    public static EquatableArray<string> List(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? default
            : value!.Split(',', ';').Select(s => s.Trim()).Where(s => s.Length > 0).ToEquatableArray();
}

// An AdditionalFiles item. Text is kept for TintGrammar/TintTheme items and for files a theme may include.
internal sealed record ProjectFile(
    string FullPath,
    string DisplayPath,
    SourceKind? Kind,
    string? Text,
    string? ScopeName,
    string? Name,
    string? Namespace,
    string? Accessibility,
    bool? Kernels,
    SourceSettings Settings);

// A partial class carrying [TextMateGrammar] or [TextMateTheme].
internal sealed record AttributeTarget(
    SourceKind Kind,
    string Path,
    bool? Kernels,
    SourceSettings Settings,
    string ClassName,
    string? Namespace,
    EquatableArray<string> ContainingTypes,
    string TypeDisplay,
    bool IsValidTarget,
    LocationInfo? Location);

// One class to generate.
internal sealed record GenerationUnit(
    SourceKind Kind,
    string ClassName,
    string? Namespace,
    EquatableArray<string> ContainingTypes,
    string? Accessibility,
    string FullPath,
    string DisplayPath,
    string Text,
    bool Kernels,
    SourceSettings Settings,
    string HintName);

internal sealed record GenerationResult(string HintName, string? Source, string? KernelSource, EquatableArray<DiagnosticInfo> Diagnostics);

internal static class PathUtilities
{
    public static string Normalize(string path, string? baseDirectory)
    {
        path = path.Trim().Replace('\\', '/');
        if (!Path.IsPathRooted(path) && baseDirectory is not null)
        {
            path = baseDirectory.TrimEnd('/') + "/" + path;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return path.Replace('\\', '/').TrimEnd('/');
    }

    public static bool PathEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public static string Relative(string fullPath, string? projectDirectory)
    {
        if (projectDirectory is not null && fullPath.StartsWith(projectDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(projectDirectory.Length + 1);
        }

        return fullPath;
    }

    public static string FileNameWithoutExtensions(string path)
    {
        var name = path.Substring(path.LastIndexOf('/') + 1);
        foreach (var extension in new[] { ".tmLanguage.json", ".tmLanguage", ".tmTheme", ".plist", ".json" })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - extension.Length);
            }
        }

        var dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }
}

internal static class Naming
{
    // Scope segments that are abbreviations of a language name.
    private static readonly Dictionary<string, string> s_wellKnown = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "CSharp",
        ["fs"] = "FSharp",
        ["vb"] = "VisualBasic",
        ["js"] = "JavaScript",
        ["jsx"] = "JavaScriptReact",
        ["ts"] = "TypeScript",
        ["tsx"] = "TypeScriptReact",
        ["objc"] = "ObjectiveC",
        ["objcpp"] = "ObjectiveCpp",
        ["cpp"] = "Cpp",
    };

    // source.cs -> CSharpGrammar, text.html.basic -> HtmlBasicGrammar: the segments after the first, in PascalCase.
    public static string GrammarClassName(string scopeName)
    {
        var segments = scopeName.Split('.');
        var builder = new StringBuilder();
        for (var i = segments.Length > 1 ? 1 : 0; i < segments.Length; i++)
        {
            builder.Append(s_wellKnown.TryGetValue(segments[i], out var known) ? known : PascalCase(segments[i]));
        }

        return Identifier(builder.ToString(), "Grammar");
    }

    // dark_plus.json -> DarkPlusTheme.
    public static string ThemeClassName(string fileName) => Identifier(PascalCase(fileName), "Theme");

    public static string PascalCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        var upper = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
                if (c == '+')
                {
                    builder.Append("Plus");
                }
                else if (c == '#')
                {
                    builder.Append("Sharp");
                }
            }
        }

        return builder.ToString();
    }

    private static string Identifier(string name, string suffix)
    {
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
        {
            name += suffix;
        }

        return char.IsDigit(name[0]) ? "_" + name : name;
    }
}
