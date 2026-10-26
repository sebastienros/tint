using System.Runtime.CompilerServices;

namespace Tint.Tests;

internal static class Fixtures
{
    public static string VSCodeTextMate { get; } = Path.Combine(SourceDirectory(), "Fixtures", "vscode-textmate");

    public static string Samples { get; } = Path.Combine(SourceDirectory(), "Fixtures", "samples");

    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", ".."));

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
}
