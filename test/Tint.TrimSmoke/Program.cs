using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Tint;
using Tint.Grammars;
using Tint.Themes;

// Tokenizes C# and JSON with the bundled classes and prints the token counts. With "expect-trimmed", also reads the
// trimmed Tint.Grammars.dll and Tint.Themes.dll beside the executable and fails when a class the app does not use
// survived.
string[] csharp =
[
    "using System;",
    "",
    "// Says hello.",
    "public static class Greeter",
    "{",
    "    public static string Greet(string name) => $\"Hello, {name}!\";",
    "}",
];
string[] json =
[
    "{",
    "  \"name\": \"tint\",",
    "  \"values\": [1, 2.5, true, null]",
    "}",
];

var registry = new GrammarRegistry();
registry.Add(TintGrammars.CSharp, TintGrammars.Json);
var theme = Theme.FromSource(TintThemes.DarkPlus);

var failed = false;
failed |= !Tokenize(TintGrammars.CSharp.ScopeName, csharp);
failed |= !Tokenize(TintGrammars.Json.ScopeName, json);

if (args.Contains("expect-trimmed"))
{
    failed |= !CheckTrimmed("Tint.Grammars.dll", "Grammar", ["CSharpGrammar", "JsonGrammar"]);
    failed |= !CheckTrimmed("Tint.Themes.dll", "Theme", ["DarkPlusTheme"]);
}

Console.WriteLine(failed ? "FAILED" : "OK");
return failed ? 1 : 0;

bool Tokenize(string scopeName, string[] lines)
{
    var tokenizer = registry.CreateTokenizer(scopeName, theme);
    var state = StateStack.Initial;
    var tokens = 0;
    var colored = 0;
    foreach (var line in lines)
    {
        foreach (var token in tokenizer.TokenizeLine(line, state, out state))
        {
            tokens++;
            if (token.Scopes.GetScopeNames().Length > 1)
            {
                colored++;
            }
        }
    }

    Console.WriteLine($"{scopeName}: {tokens} tokens, {colored} with a scope beyond the root ({theme.Name})");
    return tokens > lines.Length && colored > 0;
}

// The type names of the trimmed assembly: the classes that end with the suffix must be exactly the expected ones.
static bool CheckTrimmed(string fileName, string suffix, string[] expected)
{
    var path = Path.Combine(AppContext.BaseDirectory, fileName);
    if (!File.Exists(path))
    {
        Console.WriteLine($"{fileName}: not found beside the executable (NativeAOT or single-file publish)");
        return false;
    }

    using var stream = File.OpenRead(path);
    using var reader = new PEReader(stream);
    var metadata = reader.GetMetadataReader();
    var kept = metadata.TypeDefinitions
        .Select(handle => metadata.GetTypeDefinition(handle))
        .Where(type => type.GetDeclaringType().IsNil)
        .Select(type => metadata.GetString(type.Name))
        .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToList();

    Console.WriteLine($"{fileName}: {metadata.TypeDefinitions.Count} types, {suffix} classes kept: {string.Join(", ", kept)}");
    var unexpected = kept.Except(expected, StringComparer.Ordinal).ToList();
    if (unexpected.Count > 0 || !expected.All(kept.Contains))
    {
        Console.WriteLine($"{fileName}: expected only {string.Join(", ", expected)}; unexpected: {string.Join(", ", unexpected)}");
        return false;
    }

    return true;
}
