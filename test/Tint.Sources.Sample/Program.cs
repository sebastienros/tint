using System.Text;
using Tint;
using Tint.Grammars;
using Tint.Themes;

namespace Tint.Sources.Sample;

// The JSON grammar bound by path from the package folder; the item list includes the file.
[TextMateGrammar("$(TintGrammarsSourcesPath)json/syntaxes/JSON.tmLanguage.json", LanguageId = "json", FileExtensions = [".json", ".jsonc"])]
public partial class JsonGrammar;

public static class Program
{
    public static readonly string[] CSharpLines =
    [
        "using System;",
        "",
        "// Says hello.",
        "public static class Greeter",
        "{",
        "    public static string Greet(string name) => $\"Hello, {name}!\";",
        "}",
    ];

    public static readonly string[] JsonLines =
    [
        "{",
        "  \"name\": \"tint\", // comment",
        "  \"values\": [1, 2.5, true, null]",
        "}",
    ];

    public static void Main() => Console.Write(RenderGenerated());

    // The generated classes: CSharpGrammar and DarkPlusTheme from the items, JsonGrammar from the attribute.
    public static string RenderGenerated() => Render(CSharpGrammar.Instance, JsonGrammar.Instance, DarkPlusTheme.Instance);

    // One line per token: the text, the foreground color and the scopes.
    public static string Render(IGrammarSource csharp, IGrammarSource json, IThemeSource themeSource)
    {
        var registry = new GrammarRegistry();
        registry.Add(csharp, json);
        var theme = Theme.FromSource(themeSource);
        var output = new StringBuilder();
        output.Append("theme: ").Append(themeSource.Name).Append('\n');
        Tokenize(registry, theme, csharp.ScopeName, CSharpLines, output);
        Tokenize(registry, theme, json.ScopeName, JsonLines, output);
        return output.ToString();
    }

    private static void Tokenize(GrammarRegistry registry, Theme theme, string scopeName, string[] lines, StringBuilder output)
    {
        output.Append("== ").Append(scopeName).Append('\n');
        var tokenizer = registry.CreateTokenizer(scopeName, theme);
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            output.Append("> ").Append(line).Append('\n');
            foreach (var token in tokenizer.TokenizeLine(line, state, out state))
            {
                output.Append("  '").Append(line, token.Start, token.Length).Append("' ")
                    .Append(theme.ColorMap.GetColor(token.Foreground)).Append(' ')
                    .AppendJoin(' ', token.Scopes.GetScopeNames()).Append('\n');
            }
        }
    }
}
