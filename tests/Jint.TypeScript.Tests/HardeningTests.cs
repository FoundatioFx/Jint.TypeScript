using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Xunit;

namespace Jint.TypeScript.Tests;

public class HardeningTests
{
    private static readonly TypeScriptCompiler Compiler = new();
    private static readonly TypeScriptCompiler BabelScriptCompiler = new(new() { AllowReturnOutsideFunction = false });

    public static IEnumerable<object[]> InvalidFixtures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/invalid-typescript.json")));
        foreach (var test in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [test.GetProperty("name").GetString()!, test.GetProperty("source").GetString()!];
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void Rejects_Babel_rejected_type_mutations(string name, string source)
    {
        var error = Assert.Throws<TypeScriptParseException>(() => BabelScriptCompiler.ParseScript(source, name));
        Assert.InRange(error.Index, 0, source.Length);
        Assert.Equal(name, error.SourceFile);
    }

    [Theory]
    [InlineData("f<typeof>(41)")]
    [InlineData("f<T>(x: T)")]
    [InlineData("f<T extends U>(41)")]
    [InlineData("f<>()")]
    [InlineData("f?.<T>")]
    public void Rejects_unsupported_or_invalid_generic_expressions(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));

    [Theory]
    [InlineData("a < b > c")]
    [InlineData("a < b >> c")]
    [InlineData("a < b >>> c")]
    [InlineData("a << b > c")]
    [InlineData("a < b >= c")]
    [InlineData("a <= b > c")]
    [InlineData("a < b > +c")]
    [InlineData("a < b > -c")]
    [InlineData("a < b > !c")]
    [InlineData("(a < b) > /x/.test(c)")]
    [InlineData("(a < b) > (c)")]
    [InlineData("a < (b > c)")]
    [InlineData("a < b && c > d")]
    [InlineData("a < b ? (c) : d > e")]
    [InlineData("a < (b < c) && d < e")]
    [InlineData("a < b; tag`<T>(x)`;")]
    [InlineData("a < /<>/.test(b)")]
    [InlineData("a < /<T>(x)/.test(b)")]
    [InlineData("a < b / c > (d)")]
    [InlineData("a < b * c > (d)")]
    [InlineData("a < b && c > (d)")]
    [InlineData("a < `text<T>(x)`")]
    [InlineData("a < `${b < c}`")]
    [InlineData("a < 'text<T>(x)'")]
    public void Preserves_unambiguous_comparisons_and_shifts(string source)
    {
        var expected = new Parser().ParseScript(source);
        var actual = Compiler.ParseScript(source);
        Assert.Equal(expected.ToJson(new AstToJsonOptions { IncludeRange = true, IncludeLineColumn = true }),
            actual.ToJson(new AstToJsonOptions { IncludeRange = true, IncludeLineColumn = true }));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Preparation_settings_preserve_behavior(bool staticAnalysis, bool foldConstants)
    {
        var compiler = new TypeScriptCompiler(new() { StaticAnalysis = staticAnalysis, FoldConstants = foldConstants });
        const string source = "const f = (x: number): number => /ok/u.test('ok') ? x + 2 * 3 : 0; f(36);";
        var prepared = compiler.PrepareScript(source);
        Parallel.For(0, 32, _ => Assert.Equal(42, new Engine().Evaluate(prepared).AsNumber()));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Comparison_lookahead_consumes_linear_token_budget(int count)
    {
        // A rescanned suffix for each '<' would exceed this linear budget.
        var source = string.Join(" < ", Enumerable.Repeat("x", count)) + ";";
        new TypeScriptCompiler(new() { MaxTokenCount = count * 5, MaxNodeCount = count * 3 }).ParseScript(source);
    }

    [Fact]
    public void Speculation_cannot_bypass_remaining_token_budget()
    {
        var compiler = new TypeScriptCompiler(new() { MaxTokenCount = 24 });
        var source = "let a = 1; let b = 2; a < " + string.Join(" < ", Enumerable.Repeat("x", 30));
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() => compiler.ParseScript(source)).Code);
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() => compiler.ParseScript(
            "let a = 1; let b = 2; const f = (x): A<B<C<D<E<F<G<H<I>>>>>>> > > => x;")).Code);
    }

    [Fact]
    public void Type_erasure_does_not_allocate_an_AST_per_type()
    {
        var simple = "let value: T = 42;";
        var union = "let value: " + string.Join(" | ", Enumerable.Repeat("T", 2000)) + " = 42;";
        var simpleBytes = Allocated(() => Compiler.ParseScript(simple));
        var unionBytes = Allocated(() => Compiler.ParseScript(union));
        Assert.True(unionBytes - simpleBytes < 4096, $"Repeated erased names allocated {unionBytes - simpleBytes} extra bytes");
    }

    internal static long Allocated(Func<object> action)
    {
        for (var i = 0; i < 10; i++) GC.KeepAlive(action());
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) GC.KeepAlive(action());
        return (GC.GetAllocatedBytesForCurrentThread() - start) / 10;
    }

    [Theory]
    [InlineData(21243889)]
    [InlineData(2124)]
    [InlineData(3889)]
    [InlineData(20260907)]
    public void Seeded_mutations_either_produce_valid_locations_or_a_parse_error(int seed)
    {
        string[] seeds = [
            "const f = (x: Array<number>): number => x[0]; f([42]);",
            "let x: A<B<C>> | null = null; x;",
            "function f({x}: Result = {x: 42}): number { return x; }",
            "const f = async (x): Promise<number> => x;",
            "const x: string = `😀${/a/u.test('a')}`;",
            "const a = 1; const b = 2; a < b ? (a) : b;",
            "type T<X = number> = {value: X, f(x?: X): X}; const x: T = 42;",
            "interface I<T> extends A<T> { readonly value?: [T, string] }",
            "const f = (x?: {value: number}): (y: number) => number => y => y;"
        ];
        string[] insertions = ["<", ">", "?", ":", ";", "=", "=>", "|", "&", "!", "[", "]", "(", ")", "{", "}", "/*", "*/", "\r\n", "\u2028", "😀", "\0", "\\", "\"", "'", "`", "number", "as", "...", "?"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = seeds[random.Next(seeds.Length)];
            var at = random.Next(source.Length + 1);
            source = i % 2 == 0 ? source.Insert(at, insertions[random.Next(insertions.Length)])
                : source.Remove(Math.Min(at, source.Length - 1), 1);
            try { AssertLocations(Compiler.ParseScript(source, "fuzz.ts"), source, "fuzz.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("fuzz.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }

    internal static void AssertLocations(Node root, string source, string sourceFile)
    {
        var positions = new Position[source.Length + 1];
        var line = 1;
        var column = 0;
        for (var i = 0; i <= source.Length; i++)
        {
            positions[i] = Position.From(line, column);
            if (i == source.Length) break;
            var ch = source[i];
            if (ch == '\r' || ch is '\u2028' or '\u2029' || (ch == '\n' && (i == 0 || source[i - 1] != '\r')))
            { line++; column = 0; }
            else if (ch != '\n') column++;
        }
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            Assert.InRange(node.Start, 0, source.Length);
            Assert.InRange(node.End, node.Start, source.Length);
            Assert.Equal(positions[node.Start], node.Location.Start);
            Assert.Equal(positions[node.End], node.Location.End);
            Assert.Equal(sourceFile, node.Location.SourceFile);
            foreach (var child in node.ChildNodes)
            {
                Assert.InRange(child.Start, node.Start, node.End);
                Assert.InRange(child.End, child.Start, node.End);
                pending.Push(child);
            }
        }
    }
}
