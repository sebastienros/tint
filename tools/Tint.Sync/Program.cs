using System.CommandLine;
using Tint.Sync;

var commitOption = new Option<string>("--commit")
{
    Description = "The microsoft/vscode commit to sync from.",
    DefaultValueFactory = _ => "3ac0f056",
};
var rootOption = new Option<DirectoryInfo?>("--root")
{
    Description = "The repository root. Defaults to the nearest directory containing Tint.slnx.",
};

var sync = new Command("sync", "Download the VS Code grammars and themes into grammars/ and themes/.")
{
    commitOption,
    rootOption,
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
    await syncer.RunAsync(cancellationToken);
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
