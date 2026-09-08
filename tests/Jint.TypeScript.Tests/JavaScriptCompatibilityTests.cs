using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Xunit;

namespace Jint.TypeScript.Tests;

public class JavaScriptCompatibilityTests
{
    public static IEnumerable<object[]> InvalidFixtures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/invalid-javascript.json")));
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("source").GetString()!];
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void Rejects_upstream_negative_fixtures_in_both_modes(string name, string source)
    {
        var compiler = new TypeScriptCompiler(new() { AllowReturnOutsideFunction = false });
        if (name == "invalid-syntax/migrated_0268.js")
        {
            // This invalid JS fixture is now a supported TypeScript literal annotation.
            Assert.Equal("class A {a:0}", source);
            Assert.Equal(new Parser().ParseScript("class A {a;}").ToJson(), compiler.ParseScript(source, name).ToJson());
            Assert.Equal(new Parser().ParseModule("class A {a;}").ToJson(), compiler.ParseModule(source, name).ToJson());
            return;
        }
        Assert.Throws<TypeScriptParseException>(() => compiler.ParseScript(source, name));
        Assert.Throws<TypeScriptParseException>(() => compiler.ParseModule(source, name));
    }

    public static IEnumerable<object[]> Fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures/javascript.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("source").GetString()!, row.GetProperty("module").GetBoolean()];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Matches_official_javascript_AST_including_all_locations(string name, string source, bool module)
    {
        var parser = new Parser();
        var compiler = new TypeScriptCompiler(new() { AllowReturnOutsideFunction = false });
        Node expected = module ? parser.ParseModule(source, name) : parser.ParseScript(source, name);
        Node actual = module ? compiler.ParseModule(source, name) : compiler.ParseScript(source, name);
        var options = new AstToJsonOptions { IncludeRange = true, IncludeLineColumn = true };
        Assert.Equal(expected.ToJson(options), actual.ToJson(options));
    }
}
