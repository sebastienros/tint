using System.Text;
using System.Xml;
using Tint.Loading;
using Tint.Themes;

namespace Tint.Generator;

internal static class ThemeEmitter
{
    public static GenerationResult Generate(GenerationUnit unit, EquatableArray<ProjectFile> files, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var location = LocationInfo.InFile(unit.DisplayPath);
        var mode = ThemeLoadMode.VSCode;
        if (unit.Settings.ThemeMode is { } modeText)
        {
            if (string.Equals(modeText, "Raw", StringComparison.OrdinalIgnoreCase))
            {
                mode = ThemeLoadMode.Raw;
            }
            else if (!string.Equals(modeText, "VSCode", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidMetadata, location, modeText, "ThemeMode", unit.ClassName, "expected 'VSCode' or 'Raw'"));
            }
        }

        // Includes resolve relative to the theme file among the project's AdditionalFiles.
        var directory = unit.FullPath.Substring(0, unit.FullPath.LastIndexOf('/') + 1);
        string? missing = null;
        ThemeIncludeResolver resolver = include =>
        {
            var full = PathUtilities.Normalize(include, directory.TrimEnd('/'));
            var file = files.FirstOrDefault(f => f.Text is not null && PathUtilities.PathEquals(f.FullPath, full));
            if (file is null)
            {
                missing ??= include;
                return null;
            }

            return new MemoryStream(Encoding.UTF8.GetBytes(file.Text!), writable: false);
        };

        RawTheme theme;
        try
        {
            var format = unit.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
            theme = ThemeLoader.Read(Encoding.UTF8.GetBytes(unit.Text), format, resolver, string.Empty, 0, mode);
        }
        catch (FileNotFoundException) when (missing is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.MissingThemeInclude, location, unit.RelativePath, missing));
            return new GenerationResult(unit.HintName, null, null, diagnostics.ToEquatableArray());
        }
        catch (JsonTreeException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, LocationInfo.InFile(unit.DisplayPath, e.Line - 1, e.Column - 1), unit.RelativePath, "theme", e.Message));
            return new GenerationResult(unit.HintName, null, null, diagnostics.ToEquatableArray());
        }
        catch (XmlException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, LocationInfo.InFile(unit.DisplayPath, Math.Max(0, e.LineNumber - 1), Math.Max(0, e.LinePosition - 1)), unit.RelativePath, "theme", e.Message));
            return new GenerationResult(unit.HintName, null, null, diagnostics.ToEquatableArray());
        }
        catch (FormatException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, location, unit.RelativePath, "theme", e.Message));
            return new GenerationResult(unit.HintName, null, null, diagnostics.ToEquatableArray());
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new GenerationResult(unit.HintName, Emit(unit, theme), null, diagnostics.ToEquatableArray());
    }

    private static string Emit(GenerationUnit unit, RawTheme theme)
    {
        const string RuleType = "global::Tint.Themes.RawThemeRule";
        var w = new CodeWriter();
        var nesting = GrammarEmitter.Header(w, unit, " : global::Tint.Themes.IThemeSource");
        var c = unit.ClassName;
        w.Line("private static global::Tint.Themes.RawTheme? __tintTheme;");
        w.Line();
        w.Line("/// <summary>The shared instance.</summary>");
        w.Line("public static " + c + " Instance { get; } = new " + c + "();");
        w.Line();
        w.Line("/// <inheritdoc/>");
        w.Line("public string? Name => " + CodeWriter.Literal(theme.Name) + ";");
        w.Line();
        w.Line("/// <inheritdoc/>");
        w.Open("public global::Tint.Themes.RawTheme GetTheme()");
        w.Line("var theme = global::System.Threading.Volatile.Read(ref __tintTheme);");
        w.Line("if (theme is not null)").Line("{").Line("    return theme;").Line("}");
        w.Line();
        w.Line("theme = __TintBuild();");
        w.Line("return global::System.Threading.Interlocked.CompareExchange(ref __tintTheme, theme, null) ?? theme;");
        w.Close();
        w.Line();
        w.Open("private static global::Tint.Themes.RawTheme __TintBuild()");
        w.Line("var rules = new " + RuleType + "[]");
        w.Line("{");
        w.Indent();
        foreach (var rule in theme.Rules)
        {
            var parts = new List<string>();
            if (rule.Name is not null)
            {
                parts.Add("Name = " + CodeWriter.Literal(rule.Name));
            }

            if (rule.Scopes is not null)
            {
                parts.Add("Scopes = " + CodeWriter.StringArray(rule.Scopes));
            }

            if (rule.FontStyle is not null)
            {
                parts.Add("FontStyle = " + CodeWriter.Literal(rule.FontStyle));
            }

            if (rule.Foreground is not null)
            {
                parts.Add("Foreground = " + CodeWriter.Literal(rule.Foreground));
            }

            if (rule.Background is not null)
            {
                parts.Add("Background = " + CodeWriter.Literal(rule.Background));
            }

            w.Line("new " + RuleType + (parts.Count == 0 ? "()" : " { " + string.Join(", ", parts) + " }") + ",");
        }

        w.Unindent();
        w.Line("};");
        w.Line("var colors = new global::System.Collections.Generic.Dictionary<string, string>(" + theme.UiColors.Count + ", global::System.StringComparer.Ordinal);");
        foreach (var color in theme.UiColors.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            w.Line("colors[" + CodeWriter.Literal(color.Key) + "] = " + CodeWriter.Literal(color.Value) + ";");
        }

        w.Line("return new global::Tint.Themes.RawTheme(rules, " + CodeWriter.Literal(theme.Name) + ", " + CodeWriter.Literal(theme.Type)
            + ", global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(colors, global::System.StringComparer.Ordinal));");
        w.Close();
        GrammarEmitter.Footer(w, nesting);
        return w.ToString();
    }
}
