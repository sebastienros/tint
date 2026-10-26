using System.Diagnostics;
namespace Tint.Differential;

/// <summary>
/// Settles a difference with vscode-textmate itself (<c>tools/Tint.CorpusCheck/referee/tokenize.mjs</c>, Node and
/// <c>npm install</c> in that directory): tokenizes the file up to the line and reports whether Tint and TextMateSharp agree with it on
/// scopes and on Dark+ styles.
/// </summary>
public sealed class Referee(RepositoryData repository, IEngine tint, IEngine reference)
{
    private readonly string _script = Path.Combine(repository.Root, "tools", "Tint.CorpusCheck", "referee", "tokenize.mjs");

    /// <summary>Whether Node and the referee's packages are there.</summary>
    public bool IsAvailable => File.Exists(Path.Combine(Path.GetDirectoryName(_script)!, "node_modules", "vscode-textmate", "package.json"));

    /// <summary>
    /// The verdict for a file's first mismatch: the first line (from 1 to <paramref name="lastLine"/>) where Tint differs
    /// from vscode-textmate, and the same for TextMateSharp; 0 when it agrees on every line.
    /// </summary>
    public (int TintLine, int ReferenceLine, string? Error) Judge(string scope, string path, int lastLine)
    {
        var expected = Run(scope, path, lastLine, out var error);
        if (expected is null)
        {
            return (0, 0, error);
        }

        return (FirstDifference(tint, scope, path, lastLine, expected), FirstDifference(reference, scope, path, lastLine, expected), null);
    }

    private static int FirstDifference(IEngine engine, string scope, string path, int lastLine, (List<string> Tokens, List<StyleToken> Styles)[] expected)
    {
        var tokenizer = engine.CreateTokenizer(scope, TimeSpan.Zero)!;
        var lines = Lines.Read(path);
        for (var i = 0; i < Math.Min(lastLine, lines.Length); i++)
        {
            var output = tokenizer.Tokenize(lines[i]);
            if (output.Exception is not null)
            {
                return i + 1;
            }

            var actual = output.Tokens.Select(t => $"{t.Start}-{t.End} {t.Scopes}");
            if (i >= expected.Length || !actual.SequenceEqual(expected[i].Tokens)
                || !output.Styles.AsSpan().SequenceEqual(Comparison.NormalizeStyles([.. expected[i].Styles], lines[i].Length)))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private (List<string> Tokens, List<StyleToken> Styles)[]? Run(string scope, string path, int lastLine, out string? error)
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { _script, repository.Root, scope, path, "1", lastLine.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill();
            error = "the referee timed out";
            return null;
        }

        if (process.ExitCode != 0)
        {
            error = stderr.Result.Split('\n').FirstOrDefault(l => l.Length > 0) ?? $"exit code {process.ExitCode}";
            return null;
        }

        var lines = new List<(List<string> Tokens, List<StyleToken> Styles)>();
        foreach (var line in stdout.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('L') && int.TryParse(line.AsSpan(1), out _))
            {
                lines.Add(([], []));
            }
            else if (line.StartsWith("S ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ');
                lines[^1].Styles.Add(new StyleToken(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2], parts[3], int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture)));
            }
            else
            {
                lines[^1].Tokens.Add(line);
            }
        }

        error = null;
        return [.. lines];
    }
}
