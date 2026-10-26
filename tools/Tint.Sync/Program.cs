using System.CommandLine;
using Tint.Sync;

var commitOption = new Option<string>("--commit")
{
    Description = "The microsoft/vscode commit, branch or tag to sync from. Resolved to a full SHA through the GitHub API.",
    DefaultValueFactory = _ => "3ac0f056",
};
var rootOption = new Option<DirectoryInfo?>("--root")
{
    Description = "The repository root. Defaults to the nearest directory containing Tint.slnx.",
};
var checkOption = new Option<bool>("--check")
{
    Description = "Only compare the resolved SHA with the commit recorded in grammars/manifest.json: prints 'up to date' and exits 0 when they match, exits 1 otherwise. Downloads nothing.",
};

var sync = new Command("sync", "Download the VS Code grammars and themes into grammars/ and themes/.")
{
    commitOption,
    rootOption,
    checkOption,
};
sync.SetAction(async (result, cancellationToken) =>
{
    var root = result.GetValue(rootOption)?.FullName ?? FindRepositoryRoot();
    if (root is null)
    {
        Console.Error.WriteLine("Could not find Tint.slnx; pass --root.");
        return 1;
    }

    var syncer = new VSCodeSync(result.GetValue(commitOption)!, root);
    var resolved = await syncer.ResolveCommitAsync(cancellationToken);
    Console.WriteLine($"resolved: {resolved}");

    if (result.GetValue(checkOption))
    {
        var recorded = syncer.ReadRecordedCommit();
        if (string.Equals(recorded, resolved, StringComparison.Ordinal))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        Console.WriteLine($"behind: {recorded ?? "(no recorded commit)"} -> {resolved}");
        return 1;
    }

    await syncer.RunAsync(resolved, cancellationToken);
    return 0;
});

var rootCommand = new RootCommand("Tint repository tools.") { sync };
return await rootCommand.Parse(args).InvokeAsync();

static string? FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Tint.slnx")))
        {
            return directory.FullName;
        }
    }

    return null;
}
