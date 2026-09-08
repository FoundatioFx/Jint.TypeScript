using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Xunit;

namespace Jint.TypeScript.Tests;

public class TypeScriptReferenceTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/typescript.json")));
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("source").GetString()!,
                row.GetProperty("javascript").GetString()!, row.TryGetProperty("module", out var module) && module.GetBoolean()];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Matches_current_TypeScript_emission(string name, string source, string javascript, bool module)
    {
        var compiler = new TypeScriptCompiler();
        var parser = new Parser();
        Node expected = module ? parser.ParseModule(javascript, name) : parser.ParseScript(javascript, name);
        Node actual = module ? compiler.ParseModule(source, name) : compiler.ParseScript(source, name);
        Assert.Equal(expected.ToJson(), actual.ToJson());
        HardeningTests.AssertLocations(actual, source, name);
        if (!module)
        {
            var expectedValue = new Engine().Evaluate(javascript).UnwrapIfPromise();
            var actualValue = new Engine().Evaluate(compiler.PrepareScript(source)).UnwrapIfPromise();
            Assert.Equal(expectedValue.ToString(), actualValue.ToString());
        }
    }
}
