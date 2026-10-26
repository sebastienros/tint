using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tint.Differential;

/// <summary>A grammar of <c>grammars/manifest.json</c>: where its file is and what VS Code's package.json says about it.</summary>
public sealed record ManifestGrammar(string ScopeName, string? Language, string Path, string Format, IReadOnlyList<string> InjectTo, IReadOnlyList<string> Extensions, string? UpstreamVersion);

/// <summary>The checkout's grammar and theme files, as synced by <c>tools/Tint.Sync</c>.</summary>
public sealed class RepositoryData
{
    private RepositoryData(string root, IReadOnlyList<ManifestGrammar> grammars, string vscodeCommit)
    {
        Root = root;
        Grammars = grammars;
        VSCodeCommit = vscodeCommit;
    }

    /// <summary>The repository root (the directory holding <c>Tint.slnx</c>).</summary>
    public string Root { get; }

    /// <summary>Every synced grammar, in manifest order.</summary>
    public IReadOnlyList<ManifestGrammar> Grammars { get; }

    /// <summary>The microsoft/vscode commit the grammars come from.</summary>
    public string VSCodeCommit { get; }

    /// <summary>The full path of a grammar file.</summary>
    public string GrammarPath(ManifestGrammar grammar) => System.IO.Path.Combine(Root, "grammars", grammar.Path);

    /// <summary>The directory of the default VS Code themes.</summary>
    public string ThemesDirectory => System.IO.Path.Combine(Root, "themes", "theme-defaults");

    /// <summary>Reads the manifest of the checkout at <paramref name="root"/>, or of the nearest one above the current directory.</summary>
    public static RepositoryData Load(string? root = null)
    {
        root ??= FindRoot() ?? throw new InvalidOperationException("Could not find Tint.slnx; pass --repo.");
        var manifest = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(root, "grammars", "manifest.json")))!;
        var grammars = new List<ManifestGrammar>();
        foreach (var node in manifest["grammars"]!.AsArray())
        {
            grammars.Add(new ManifestGrammar(
                (string)node!["scopeName"]!,
                (string?)node["language"],
                (string)node["path"]!,
                (string)node["format"]!,
                Strings(node["injectTo"]),
                Strings(node["extensions"]),
                (string?)node["upstream"]?["version"]));
        }

        return new RepositoryData(root, grammars, (string)manifest["commit"]!);
    }

    /// <summary>The nearest directory holding <c>Tint.slnx</c>, from the current directory up.</summary>
    public static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Tint.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>The <c>version</c> header of a JSON grammar file, or null.</summary>
    public static string? ReadVersionHeader(Stream json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string[] Strings(JsonNode? node) => node is JsonArray array ? [.. array.Select(n => (string)n!)] : [];
}
