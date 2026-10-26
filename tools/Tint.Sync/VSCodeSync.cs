using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tint.Sync;

/// <summary>
/// Downloads the grammars and themes contributed by the extensions of a <c>microsoft/vscode</c> commit and writes
/// <c>grammars/manifest.json</c>, <c>themes/manifest.json</c> and <c>THIRD-PARTY-NOTICES.txt</c>.
/// </summary>
internal sealed class VSCodeSync
{
    private const string Repository = "microsoft/vscode";

    private static readonly JsonDocumentOptions s_lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _commit;
    private readonly string _root;
    private readonly HttpClient _http;

    public VSCodeSync(string commit, string root)
    {
        _commit = commit;
        _root = root;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tint.Sync", "1.0"));
    }

    /// <summary>
    /// Resolves the configured commit, branch or tag to its full SHA through the GitHub API.
    /// </summary>
    public async Task<string> ResolveCommitAsync(CancellationToken cancellationToken)
    {
        var commit = await GetApiAsync($"https://api.github.com/repos/{Repository}/commits/{Uri.EscapeDataString(_commit)}", cancellationToken);
        return (string)commit["sha"]!;
    }

    /// <summary>
    /// Reads the <c>commit</c> recorded by the previous sync in <c>grammars/manifest.json</c>, or <see langword="null"/>
    /// when there is none.
    /// </summary>
    public string? ReadRecordedCommit()
    {
        var manifest = Path.Combine(_root, "grammars", "manifest.json");
        if (!File.Exists(manifest))
        {
            return null;
        }

        return (string?)JsonNode.Parse(File.ReadAllBytes(manifest), documentOptions: s_lenient)?["commit"];
    }

    public async Task RunAsync(string commitHash, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing {Repository}@{commitHash}");

        var files = await ListFilesAsync(commitHash, cancellationToken);
        var extensionNames = files
            .Select(path => path.Split('/'))
            .Where(parts => parts.Length == 3 && parts[0] == "extensions" && parts[2] == "package.json")
            .Select(parts => parts[1])
            .Order(StringComparer.Ordinal)
            .ToList();

        var extensions = await ParallelMapAsync(extensionNames, async (name, ct) =>
        {
            var packageJson = await DownloadAsync(commitHash, $"extensions/{name}/package.json", ct);
            return new Extension(name, packageJson, JsonNode.Parse(packageJson, documentOptions: s_lenient)!);
        }, cancellationToken);

        var languages = CollectLanguages(extensions);
        var contributing = extensions.Where(e => e.Grammars.Count > 0 || e.Themes.Count > 0).ToList();

        var grammarsDirectory = ResetDirectory("grammars");
        var themesDirectory = ResetDirectory("themes");

        var grammarEntries = new List<GrammarEntry>();
        var themeEntries = new List<ThemeEntry>();
        var registrations = new List<Registration>();
        var downloadedBytes = 0L;

        foreach (var extension in contributing)
        {
            var targets = new List<string>();
            if (extension.Grammars.Count > 0)
            {
                targets.Add(Path.Combine(grammarsDirectory, extension.Name));
            }

            if (extension.Themes.Count > 0)
            {
                targets.Add(Path.Combine(themesDirectory, extension.Name));
            }

            var metadata = new Dictionary<string, byte[]> { ["package.json"] = extension.PackageJson };
            foreach (var optional in new[] { "package.nls.json", "cgmanifest.json" })
            {
                if (files.Contains($"extensions/{extension.Name}/{optional}"))
                {
                    metadata[optional] = await DownloadAsync(commitHash, $"extensions/{extension.Name}/{optional}", cancellationToken);
                }
            }

            foreach (var target in targets)
            {
                foreach (var (fileName, content) in metadata)
                {
                    WriteFile(Path.Combine(target, fileName), content);
                }
            }

            var extensionRegistrations = metadata.TryGetValue("cgmanifest.json", out var cgmanifest)
                ? ParseRegistrations(extension.Name, cgmanifest)
                : [];
            registrations.AddRange(extensionRegistrations);

            var nls = metadata.TryGetValue("package.nls.json", out var nlsBytes)
                ? JsonNode.Parse(nlsBytes, documentOptions: s_lenient)?.AsObject()
                : null;

            var grammarFiles = extension.Grammars
                .Select(g => NormalizePath((string)g["path"]!))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var grammarContents = await ParallelMapAsync(grammarFiles, (path, ct) => DownloadAsync(commitHash, $"extensions/{extension.Name}/{path}", ct), cancellationToken);
            var grammarsByPath = grammarFiles.Zip(grammarContents).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);

            foreach (var (path, content) in grammarsByPath)
            {
                WriteFile(Path.Combine(grammarsDirectory, extension.Name, path), content);
                downloadedBytes += content.Length;
            }

            foreach (var grammar in extension.Grammars)
            {
                var path = NormalizePath((string)grammar["path"]!);
                var language = (string?)grammar["language"];
                languages.TryGetValue(language ?? "", out var languageInfo);
                grammarEntries.Add(new GrammarEntry(
                    ScopeName: (string)grammar["scopeName"]!,
                    Language: language,
                    Path: $"{extension.Name}/{path}",
                    Format: IsPlist(path) ? "plist" : "json",
                    EmbeddedLanguages: grammar["embeddedLanguages"],
                    InjectTo: grammar["injectTo"],
                    TokenTypes: grammar["tokenTypes"],
                    BalancedBracketScopes: grammar["balancedBracketScopes"],
                    UnbalancedBracketScopes: grammar["unbalancedBracketScopes"],
                    Aliases: languageInfo?.Aliases,
                    Extensions: languageInfo?.Extensions,
                    Upstream: MatchUpstream(extensionRegistrations, grammarsByPath[path])));
            }

            foreach (var theme in extension.Themes)
            {
                var path = NormalizePath((string)theme["path"]!);
                var content = await DownloadAsync(commitHash, $"extensions/{extension.Name}/{path}", cancellationToken);
                var fileName = path.Split('/')[^1];
                WriteFile(Path.Combine(themesDirectory, extension.Name, fileName), content);
                downloadedBytes += content.Length;

                var include = (string?)JsonNode.Parse(content, documentOptions: s_lenient)?["include"];
                themeEntries.Add(new ThemeEntry(
                    Label: Localize((string?)theme["label"] ?? (string?)theme["id"] ?? fileName, nls),
                    UiTheme: (string?)theme["uiTheme"],
                    Path: $"{extension.Name}/{fileName}",
                    Include: include));
            }
        }

        grammarEntries.Sort((a, b) => string.CompareOrdinal(a.ScopeName + "\0" + a.Path + "\0" + a.Language, b.ScopeName + "\0" + b.Path + "\0" + b.Language));
        themeEntries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        WriteFile(Path.Combine(grammarsDirectory, "manifest.json"), WriteGrammarManifest(commitHash, grammarEntries));
        WriteFile(Path.Combine(themesDirectory, "manifest.json"), WriteThemeManifest(commitHash, themeEntries));
        WriteFile(Path.Combine(_root, "THIRD-PARTY-NOTICES.txt"), Encoding.UTF8.GetBytes(WriteNotices(commitHash, registrations)));

        var uniqueGrammars = grammarEntries.Select(g => g.Path).Distinct(StringComparer.Ordinal).ToList();
        Console.WriteLine($"Extensions scanned:      {extensions.Count}");
        Console.WriteLine($"Contributing extensions: {contributing.Count} ({contributing.Count(e => e.Grammars.Count > 0)} grammar, {contributing.Count(e => e.Themes.Count > 0)} theme)");
        Console.WriteLine($"Grammar registrations:   {grammarEntries.Count} ({grammarEntries.Select(g => g.ScopeName).Distinct(StringComparer.Ordinal).Count()} scope names)");
        Console.WriteLine($"Grammar files:           {uniqueGrammars.Count} ({uniqueGrammars.Count(p => !IsPlist(p))} JSON, {uniqueGrammars.Count(IsPlist)} plist)");
        Console.WriteLine($"Themes:                  {themeEntries.Count}");
        Console.WriteLine($"Upstream registrations:  {registrations.Count}");
        Console.WriteLine($"Grammar and theme bytes: {downloadedBytes:N0}");
    }

    private async Task<HashSet<string>> ListFilesAsync(string commitHash, CancellationToken cancellationToken)
    {
        var tree = await GetApiAsync($"https://api.github.com/repos/{Repository}/git/trees/{commitHash}?recursive=1", cancellationToken);
        if ((bool?)tree["truncated"] == true)
        {
            throw new InvalidOperationException("The GitHub tree listing is truncated.");
        }

        return tree["tree"]!.AsArray()
            .Where(entry => (string?)entry!["type"] == "blob")
            .Select(entry => (string)entry!["path"]!)
            .Where(path => path.StartsWith("extensions/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<JsonNode> GetApiAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return JsonNode.Parse(content)!;
    }

    private async Task<byte[]> DownloadAsync(string commitHash, string path, CancellationToken cancellationToken)
    {
        var url = $"https://raw.githubusercontent.com/{Repository}/{commitHash}/{Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal)}";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _http.GetByteArrayAsync(url, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    private static async Task<List<TResult>> ParallelMapAsync<T, TResult>(IReadOnlyList<T> items, Func<T, CancellationToken, Task<TResult>> map, CancellationToken cancellationToken)
    {
        var results = new TResult[items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (index, ct) =>
        {
            results[index] = await map(items[index], ct);
        });
        return [.. results];
    }

    private static Dictionary<string, LanguageInfo> CollectLanguages(IEnumerable<Extension> extensions)
    {
        var languages = new Dictionary<string, LanguageInfo>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            foreach (var language in extension.Contributes("languages"))
            {
                var id = (string?)language["id"];
                if (id is null)
                {
                    continue;
                }

                if (!languages.TryGetValue(id, out var info))
                {
                    info = new LanguageInfo([], []);
                    languages[id] = info;
                }

                AddDistinct(info.Aliases, language["aliases"]);
                AddDistinct(info.Extensions, language["extensions"]);
            }
        }

        return languages;
    }

    private static void AddDistinct(List<string> target, JsonNode? values)
    {
        if (values is not JsonArray array)
        {
            return;
        }

        foreach (var value in array)
        {
            if (value?.GetValueKind() == JsonValueKind.String && !target.Contains((string)value!))
            {
                target.Add((string)value!);
            }
        }
    }

    private static List<Registration> ParseRegistrations(string extension, byte[] cgmanifest)
    {
        var registrations = new List<Registration>();
        foreach (var registration in JsonNode.Parse(cgmanifest, documentOptions: s_lenient)?["registrations"]?.AsArray() ?? [])
        {
            var component = registration!["component"]!;
            var type = (string?)component["type"] ?? "other";
            var details = component[type];
            var licenseDetail = registration["licenseDetail"] is JsonArray lines
                ? string.Join("\n", Flatten(lines))
                : null;
            registrations.Add(new Registration(
                Extension: extension,
                Name: (string?)details?["name"] ?? extension,
                RepositoryUrl: (string?)details?["repositoryUrl"] ?? (string?)details?["downloadUrl"],
                CommitHash: (string?)details?["commitHash"],
                License: (string?)registration["license"],
                LicenseDetail: licenseDetail,
                Version: (string?)registration["version"] ?? (string?)details?["version"]));
        }

        return registrations;
    }

    // Some cgmanifest files nest the licenseDetail lines in an extra array.
    private static IEnumerable<string> Flatten(JsonArray lines)
    {
        foreach (var line in lines)
        {
            if (line is JsonArray nested)
            {
                foreach (var inner in Flatten(nested))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return (string?)line ?? "";
            }
        }
    }

    /// <summary>
    /// Picks the cgmanifest registration a grammar was converted from: the one whose repository URL appears in the
    /// grammar's header (<c>information_for_contributors</c>, <c>version</c>), else the extension's only registration.
    /// </summary>
    private static Registration? MatchUpstream(List<Registration> registrations, byte[] grammar)
    {
        if (registrations.Count == 0)
        {
            return null;
        }

        var header = Encoding.UTF8.GetString(grammar, 0, Math.Min(grammar.Length, 4096));
        var matches = registrations
            .Where(r => r.RepositoryUrl is not null && header.Contains(TrimRepositoryUrl(r.RepositoryUrl) + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count >= 1)
        {
            return matches.MaxBy(r => TrimRepositoryUrl(r.RepositoryUrl!).Length);
        }

        return registrations.Count == 1 ? registrations[0] : null;
    }

    private static string TrimRepositoryUrl(string url)
    {
        url = url.TrimEnd('/');
        return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? url[..^4] : url;
    }

    private static string Localize(string value, JsonObject? nls)
    {
        if (value.Length > 2 && value[0] == '%' && value[^1] == '%' && nls is not null)
        {
            var localized = nls[value[1..^1]];
            if (localized?.GetValueKind() == JsonValueKind.String)
            {
                return (string)localized!;
            }

            if (localized?["message"] is { } message)
            {
                return (string)message!;
            }
        }

        return value;
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        return path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
    }

    private static bool IsPlist(string path)
        => !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private string ResetDirectory(string name)
    {
        var directory = Path.Combine(_root, name);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteFile(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static byte[] WriteGrammarManifest(string commitHash, List<GrammarEntry> grammars)
    {
        return WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("source", $"https://github.com/{Repository}/tree/{commitHash}/extensions");
            writer.WriteString("commit", commitHash);
            writer.WriteStartArray("grammars");
            foreach (var grammar in grammars)
            {
                writer.WriteStartObject();
                writer.WriteString("scopeName", grammar.ScopeName);
                WriteNullableString(writer, "language", grammar.Language);
                writer.WriteString("path", grammar.Path);
                writer.WriteString("format", grammar.Format);
                WriteNode(writer, "embeddedLanguages", grammar.EmbeddedLanguages);
                WriteNode(writer, "injectTo", grammar.InjectTo);
                WriteNode(writer, "tokenTypes", grammar.TokenTypes);
                WriteNode(writer, "balancedBracketScopes", grammar.BalancedBracketScopes);
                WriteNode(writer, "unbalancedBracketScopes", grammar.UnbalancedBracketScopes);
                WriteStrings(writer, "aliases", grammar.Aliases);
                WriteStrings(writer, "extensions", grammar.Extensions);
                if (grammar.Upstream is { } upstream)
                {
                    writer.WriteStartObject("upstream");
                    WriteNullableString(writer, "repositoryUrl", upstream.RepositoryUrl);
                    WriteNullableString(writer, "commitHash", upstream.CommitHash);
                    WriteNullableString(writer, "license", upstream.License);
                    WriteNullableString(writer, "version", upstream.Version);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNull("upstream");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static byte[] WriteThemeManifest(string commitHash, List<ThemeEntry> themes)
    {
        return WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("source", $"https://github.com/{Repository}/tree/{commitHash}/extensions");
            writer.WriteString("commit", commitHash);
            writer.WriteStartArray("themes");
            foreach (var theme in themes)
            {
                writer.WriteStartObject();
                writer.WriteString("label", theme.Label);
                WriteNullableString(writer, "uiTheme", theme.UiTheme);
                writer.WriteString("path", theme.Path);
                if (theme.Include is not null)
                {
                    writer.WriteString("include", theme.Include);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static string WriteNotices(string commitHash, List<Registration> registrations)
    {
        var builder = new StringBuilder();
        builder.Append("Tint third-party notices\n");
        builder.Append("========================\n\n");
        builder.Append("Tint bundles TextMate grammars and color themes taken from the extensions of Visual Studio Code\n");
        builder.Append($"(https://github.com/{Repository}, commit {commitHash}), which is licensed under the MIT License.\n");
        builder.Append("Each grammar or theme keeps the license of the project it was converted from, listed below from the\n");
        builder.Append("extensions' cgmanifest.json files. Grammars and themes without an entry below are authored in the\n");
        builder.Append("Visual Studio Code repository and are covered by its MIT License.\n\n");
        builder.Append("This file is generated by tools/Tint.Sync; do not edit it by hand.\n");

        var distinct = registrations
            .GroupBy(r => (r.Name, r.RepositoryUrl, r.CommitHash))
            .Select(group => (Registration: group.First(), Extensions: group.Select(r => r.Extension).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()))
            .OrderBy(entry => entry.Registration.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Registration.CommitHash, StringComparer.Ordinal);

        foreach (var (registration, extensions) in distinct)
        {
            builder.Append("\n---------------------------------------------------------\n\n");
            builder.Append($"{registration.Name} {registration.Version ?? "(no version)"} - {registration.License ?? "(license not declared)"}\n");
            if (registration.RepositoryUrl is not null)
            {
                builder.Append(registration.RepositoryUrl);
                if (registration.CommitHash is not null)
                {
                    builder.Append($" @ {registration.CommitHash}");
                }

                builder.Append('\n');
            }

            builder.Append($"Used by: {string.Join(", ", extensions)}\n\n");
            if (!string.IsNullOrWhiteSpace(registration.LicenseDetail))
            {
                builder.Append(registration.LicenseDetail.TrimEnd());
                builder.Append('\n');
            }
            else
            {
                builder.Append($"See the license of {registration.RepositoryUrl ?? registration.Name}.\n");
            }
        }

        return builder.ToString();
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, s_writerOptions))
        {
            write(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNode(Utf8JsonWriter writer, string name, JsonNode? node)
    {
        writer.WritePropertyName(name);
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            node.WriteTo(writer);
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, List<string>? values)
    {
        writer.WritePropertyName(name);
        if (values is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private sealed record Extension(string Name, byte[] PackageJson, JsonNode Package)
    {
        public List<JsonNode> Grammars { get; } = Contributes(Package, "grammars");

        public List<JsonNode> Themes { get; } = Contributes(Package, "themes");

        public List<JsonNode> Contributes(string point) => Contributes(Package, point);

        private static List<JsonNode> Contributes(JsonNode package, string point)
            => package["contributes"]?[point] is JsonArray array ? array.Where(n => n is not null).Select(n => n!).ToList() : [];
    }

    private sealed record LanguageInfo(List<string> Aliases, List<string> Extensions);

    private sealed record Registration(string Extension, string Name, string? RepositoryUrl, string? CommitHash, string? License, string? LicenseDetail, string? Version);

    private sealed record GrammarEntry(
        string ScopeName,
        string? Language,
        string Path,
        string Format,
        JsonNode? EmbeddedLanguages,
        JsonNode? InjectTo,
        JsonNode? TokenTypes,
        JsonNode? BalancedBracketScopes,
        JsonNode? UnbalancedBracketScopes,
        List<string>? Aliases,
        List<string>? Extensions,
        Registration? Upstream);

    private sealed record ThemeEntry(string Label, string? UiTheme, string Path, string? Include);
}
