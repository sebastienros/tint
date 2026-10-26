using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Tint.Generator;

internal static class TintDiagnostics
{
    private const string Category = "Tint";

    public static readonly DiagnosticDescriptor UnmatchedPath = new(
        "TINT001",
        "No AdditionalFiles item matches the attribute path",
        "[{0}(\"{1}\")] on '{2}' matches no AdditionalFiles item; add <{3} Include=\"{1}\" /> to the project (paths are relative to the project directory)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateClass = new(
        "TINT002",
        "Duplicate generated class name",
        "'{0}' is generated for both '{1}' and '{2}'; set the Name or Namespace metadata on one of the items",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidFile = new(
        "TINT003",
        "Invalid grammar or theme file",
        "'{0}' cannot be read as a {1}: {2}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRegex = new(
        "TINT004",
        "Invalid regular expression",
        "Invalid regular expression at {0} in '{1}': {2} (offset {3}); the pattern never matches at run time",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExternalScopes = new(
        "TINT005",
        "Grammar includes other grammars",
        "Grammar '{0}' includes other grammars; register these scopes alongside it: {1}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KernelsUnavailable = new(
        "TINT006",
        "Pattern kernels are not available",
        "Kernels were requested for '{0}' but are not emitted: {1}; patterns are parsed on first use",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GregsExUnavailable = new(
        "TINT007",
        "GregsEx is not reachable at build time",
        "The regular expressions of '{0}' are not validated at build time: {1}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingThemeInclude = new(
        "TINT008",
        "Included theme file not found",
        "Theme '{0}' includes '{1}', which is not an AdditionalFiles item; add it as a TintTheme or AdditionalFiles item",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidTarget = new(
        "TINT009",
        "Invalid attribute target",
        "'{0}' must be a non-generic, non-static partial class to carry [{1}]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMetadata = new(
        "TINT010",
        "Invalid metadata value",
        "Value '{0}' of {1} for '{2}' is ignored: {3}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KernelSummary = new(
        "TINT011",
        "Pattern kernels emitted",
        "Kernels for '{0}': {1} patterns compiled ahead of time, {2} left to the interpreter",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor[] s_all =
    [
        UnmatchedPath, DuplicateClass, InvalidFile, InvalidRegex, ExternalScopes, KernelsUnavailable, GregsExUnavailable,
        MissingThemeInclude, InvalidTarget, InvalidMetadata, KernelSummary,
    ];

    public static DiagnosticDescriptor Find(string id) => s_all.First(d => d.Id == id);
}

// A location that survives incremental caching (Location holds syntax trees).
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location)
        => location is { SourceTree: not null } ? new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

    public static LocationInfo InFile(string path, int line = 0, int column = 0)
        => new(path, default, new LinePositionSpan(new LinePosition(line, column), new LinePosition(line, column)));
}

// A diagnostic that survives incremental caching.
internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments)
        => new(descriptor.Id, location, arguments.ToEquatableArray());

    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(TintDiagnostics.Find(Id), Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, [.. Arguments]);
}
