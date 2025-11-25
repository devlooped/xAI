#:package Spectre.Console@0.*
#:package ConsoleAppFramework@5.*
#:property Nullable=enable
#:property ImportDirectoryBuildProps=false
#:property ImportDirectoryBuildTargets=false

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using Spectre.Console;

ConsoleApp.Run(args, FixProto);

/// <summary>Check and fix imports in .proto files.</summary>
/// <para name="dir">Optional directory, defaults to current directory.</para>
static int FixProto(bool dryRun, [Argument] string? dir = default)
{
    dir ??= Directory.GetCurrentDirectory();
    var regex = ImportExpr();
    var result = 0;
    foreach (var file in Directory.EnumerateFiles(dir, "*.proto", SearchOption.AllDirectories))
    {
        var lines = File.ReadAllLines(file).ToList();
        var changed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (regex.Match(line) is { Success: true } match)
            {
                var path = match.Groups[1].Value;
                var baseDir = Path.GetDirectoryName(file)!;
                if (File.Exists(Path.Combine(baseDir, path)))
                {
                    AnsiConsole.MarkupLine($":check_mark_button: {Path.GetRelativePath(dir, file)} [lime]{path}[/]");
                    continue;
                }

                if (File.Exists(Path.Combine(baseDir, Path.GetFileName(path))))
                {
                    AnsiConsole.MarkupLine($":pencil:{(dryRun ? ":ghost:" : "")} {Path.GetRelativePath(dir, file)}: {path} > {Path.GetFileName(path)}");
                    if (!dryRun)
                    {
                        lines[i] = line.Replace(path, Path.GetFileName(path));
                        changed = true;
                    }
                    continue;
                }

                result = 1;
                AnsiConsole.MarkupLine($":cross_mark: {Path.GetRelativePath(dir, file)}: import not found [yellow]{path}[/]");
            }
        }
        if (changed && !dryRun)
        {
            File.WriteAllLines(file, lines);
        }
    }

    return result;
}

partial class Program
{
    [GeneratedRegex(@"^import\s+""([^""]+\.proto)"";$", RegexOptions.Multiline)]
    private static partial Regex ImportExpr();
}