using System.Text.Json;
using Acornima;

// Freeze the upstream JavaScript fixtures accepted by the pinned public parser.
// JSX, third-party application bundles, and proposals are separate test contracts.
var root = Path.Combine(args[0], "test/Acornima.Tests/Fixtures.Parser");
var parser = new Parser();
var fixtures = new List<object>();
var rejected = new List<object>();
foreach (var path in Directory.GetFiles(root, "*.js", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var name = Path.GetRelativePath(root, path).Replace('\\', '/');
    if (name.StartsWith("3rdparty/", StringComparison.Ordinal) || name.StartsWith("JSX", StringComparison.Ordinal)) continue;
    var source = File.ReadAllText(path);
    var module = false;
    try { parser.ParseScript(source, name); }
    catch (ParseErrorException)
    {
        try { parser.ParseModule(source, name); module = true; }
        catch (ParseErrorException) { rejected.Add(new { name, source }); continue; }
    }
    fixtures.Add(new { name, source, module });
}
File.WriteAllText(args[1], JsonSerializer.Serialize(fixtures, new JsonSerializerOptions { WriteIndented = true }));
if (args.Length > 2) File.WriteAllText(args[2], JsonSerializer.Serialize(rejected, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Imported {fixtures.Count} valid JavaScript fixtures and {rejected.Count} rejected by the pinned parser in both source modes.");
