using System.Text;

namespace Tint.Generator;

// Shared with tools/Tint.Sync, which writes the bundles' class names with these rules.
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
