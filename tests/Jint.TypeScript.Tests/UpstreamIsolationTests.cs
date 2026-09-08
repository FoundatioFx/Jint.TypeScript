using Acornima;
using Acornima.Ast;
using Xunit;

namespace Jint.TypeScript.Tests;

public class UpstreamIsolationTests
{
    private static readonly TypeScriptCompiler Compiler = new();
    private static readonly AstToJsonOptions Locations = new() { IncludeRange = true, IncludeLineColumn = true };

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("42n")]
    [InlineData("'escaped\\ntext'")]
    [InlineData("'line\\\r\ncontinuation'")]
    [InlineData("/a+[b-d]/u")]
    public void Literal_locations_end_at_the_current_token_before_trivia_is_consumed(string literal)
    {
        var source = "// 😀\r\nlet value: unknown = " + literal + " /* trailing\r\n comment */;\r\nvalue;";
        var expected = new Parser().ParseScript(source.Replace(": unknown", new string(' ', 9), StringComparison.Ordinal), "literal.ts");
        var actual = Compiler.ParseScript(source, "literal.ts");
        Assert.Equal(expected.ToJson(Locations), actual.ToJson(Locations));
        var initializer = Assert.IsType<VariableDeclaration>(actual.Body[0]).Declarations[0].Init!;
        Assert.Equal(literal, source[initializer.Start..initializer.End]);
    }

    [Fact]
    public void Import_attribute_value_ends_before_trailing_trivia()
    {
        const string source = "import data from 'data' with { type: 'json' /* 😀\r\n comment */ }; export const n: number = 42;";
        var expected = new Parser().ParseModule(source.Replace(": number", new string(' ', 8), StringComparison.Ordinal), "module.ts");
        Assert.Equal(expected.ToJson(Locations), Compiler.ParseModule(source, "module.ts").ToJson(Locations));
    }

    [Fact]
    public void Repeated_lookahead_reuses_erased_identifier_strings()
    {
        var shortSource = string.Join('\n', Enumerable.Repeat("f<T>(0);", 200));
        var longName = "T" + new string('x', 512);
        var longSource = string.Join('\n', Enumerable.Repeat($"f<{longName}>(0);", 200));
        var shortBytes = HardeningTests.Allocated(() => Compiler.ParseScript(shortSource));
        var longBytes = HardeningTests.Allocated(() => Compiler.ParseScript(longSource));
        // The name is retained once by each tokenizer, not reallocated on every
        // lookahead reset. Clearing a copied StringPool also clears its arrays.
        Assert.True(longBytes - shortBytes < 8_192, $"Repeated erased names allocated {longBytes - shortBytes} extra bytes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lookahead_recovery_does_not_carry_source_or_location_state_between_parses(bool module)
    {
        for (var i = 0; i < 10; i++)
        {
            const string invalid = "// earlier\r\nf<T extends>(0);";
            Assert.Throws<TypeScriptParseException>(() => module ? (Node)Compiler.ParseModule(invalid) : Compiler.ParseScript(invalid));
            var typeName = "Type" + new string('x', 200) + i;
            var source = "// 😀\r\n" + $"f<{typeName}>(0);\r\ng?.<{typeName}>(1);";
            var erased = source.Replace($"<{typeName}>", new string(' ', typeName.Length + 2), StringComparison.Ordinal);
            Node expected = module ? new Parser().ParseModule(erased, "next.ts") : new Parser().ParseScript(erased, "next.ts");
            Node actual = module ? Compiler.ParseModule(source, "next.ts") : Compiler.ParseScript(source, "next.ts");
            Assert.Equal(expected.ToJson(Locations), actual.ToJson(Locations));
        }
    }

    [Theory]
    [InlineData(100, 100_000)]
    [InlineData(1000, 1_000_000)]
    public void Distinct_regex_raw_text_does_not_grow_the_identifier_pool(int count, long allocationBudget)
    {
        var source = string.Join('\n', Enumerable.Range(0, count).Select(i => $"const r{i} = /p{i}+[b-d]/u;"));
        var bytes = HardeningTests.Allocated(() => Compiler.ParseScript(source));
        // Budgets allow headroom over the measured pre-audit implementation;
        // putting every raw literal in the string pool exceeds both budgets.
        Assert.True(bytes < allocationBudget, $"Distinct regex literals allocated {bytes} bytes (budget {allocationBudget})");
    }

    [Theory]
    [InlineData("@decorate class C {}")]
    [InlineData("const C = @decorate class {};")]
    public void Restored_upstream_decorator_branch_stays_outside_both_public_source_modes(string source)
    {
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseModule(source));
    }
}
