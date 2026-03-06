namespace Bifrost.Core;

public static class Confirm
{
    public static bool Prompt(MigrationConfig config, string mode)
    {
        Console.WriteLine();
        Console.WriteLine("  Source  : " + config.Source.Server);
        Console.WriteLine("  Target  : " + config.Target.Server);
        Console.WriteLine("  Mode    : " + mode);
        Console.WriteLine();
        Console.WriteLine("  Databases:");
        foreach (var db in config.Databases)
        {
            var comment = db.Comment != null ? $"  ({db.Comment})" : "";
            Console.WriteLine($"    {db.SourceDatabase} -> {db.TargetDatabase}{comment}");
        }
        Console.WriteLine();
        Console.Write("  Confirm? [y/N] ");
        var input = Console.ReadLine()?.Trim().ToLower();
        Console.WriteLine();
        return input == "y" || input == "yes";
    }
}