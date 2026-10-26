using Microsoft.CodeAnalysis.Diagnostics;

namespace Tint.Generator;

internal enum SourceKind
{
    Grammar,
    Theme,
}

// Project-wide options (build_property.*).
internal sealed record GlobalOptions(
    string? ProjectDirectory,
    string? RootNamespace,
    bool Kernels,
    string? GregsExPackagePath,
    string? GrammarsClass,
    string? ThemesClass,
    string? GrammarsSourcesPath = null,
    string? ThemesSourcesPath = null)
{
    public static GlobalOptions From(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.projectdir", out var projectDir);
        options.TryGetValue("build_property.rootnamespace", out var rootNamespace);
        options.TryGetValue("build_property.tintkernels", out var kernels);
        options.TryGetValue("build_property.tintgregsexpackagepath", out var gregsEx);
        options.TryGetValue("build_property.tintgrammarsclass", out var grammarsClass);
        options.TryGetValue("build_property.tintthemesclass", out var themesClass);
        options.TryGetValue("build_property.tintgrammarssourcespath", out var grammarsSources);
        options.TryGetValue("build_property.tintthemessourcespath", out var themesSources);
        return new GlobalOptions(
            string.IsNullOrWhiteSpace(projectDir) ? null : PathUtilities.Normalize(projectDir!, null),
            string.IsNullOrWhiteSpace(rootNamespace) ? null : rootNamespace!.Trim(),
            IsTrue(kernels),
            string.IsNullOrWhiteSpace(gregsEx) ? null : gregsEx!.Trim(),
            SourceSettings.NullIfEmpty(grammarsClass),
            SourceSettings.NullIfEmpty(themesClass),
            SourceSettings.NullIfEmpty(grammarsSources),
            SourceSettings.NullIfEmpty(themesSources));
    }

    // The sources package properties (Tint.Grammars.Sources, Tint.Themes.Sources) an attribute path may start with,
    // as MSBuild would expand them in an item: [TextMateGrammar("$(TintGrammarsSourcesPath)csharp/...")].
    public string ExpandSourcesPaths(string path)
    {
        path = Expand(path, "$(TintGrammarsSourcesPath)", GrammarsSourcesPath);
        return Expand(path, "$(TintThemesSourcesPath)", ThemesSourcesPath);

        static string Expand(string path, string token, string? value)
            => value is not null && path.StartsWith(token, StringComparison.OrdinalIgnoreCase)
                ? value.Replace('\\', '/').TrimEnd('/') + "/" + path.Substring(token.Length).TrimStart('/', '\\')
                : path;
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
    bool IsPublic,
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
    string RelativePath,
    string Text,
    bool Kernels,
    SourceSettings Settings,
    string HintName,
    bool IsPublic);

internal sealed record GenerationResult(string HintName, string? Source, string? KernelSource, EquatableArray<DiagnosticInfo> Diagnostics, AggregateEntry? Entry = null);

// A generated class as an aggregate class (TintGrammarsClass, TintThemesClass) lists it. Key is the scope name of a
// grammar or the name of a theme.
internal sealed record AggregateEntry(SourceKind Kind, string TypeName, string ClassName, string? Key, bool IsPublic)
{
    public static AggregateEntry For(GenerationUnit unit, string? key)
        => new(unit.Kind, "global::" + unit.HintName, unit.ClassName, key, unit.IsPublic);
}

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

    // The path relative to the project directory, or the full path (forward slashes) for a file outside it, such as
    // one from a package folder: diagnostics then name the file wherever it is.
    public static string Relative(string fullPath, string? projectDirectory)
    {
        fullPath = fullPath.Replace('\\', '/');
        if (projectDirectory is not null)
        {
            var directory = projectDirectory.Replace('\\', '/').TrimEnd('/');
            if (fullPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase) && fullPath.Length > directory.Length + 1)
            {
                return fullPath.Substring(directory.Length + 1);
            }
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
