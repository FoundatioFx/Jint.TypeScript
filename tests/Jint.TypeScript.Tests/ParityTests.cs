using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class ParityTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("(value as number) => value;")]
    [InlineData("(value!) => value;")]
    [InlineData("async (value as number) => value;")]
    [InlineData("async (value!) => value;")]
    [InlineData("({x: value as number}) => value;")]
    [InlineData("([value!]) => value;")]
    [InlineData("(...value as number[]) => value;")]
    [InlineData("((value = 42) as number) => value;")]
    [InlineData("(value satisfies number) => value;")]
    [InlineData("async!(value) => value;")]
    [InlineData("const value = 42 satisfies const;")]
    [InlineData("const value = 42 as;")]
    [InlineData("const value = x as number!;")]
    [InlineData("const value = x++!;")]
    [InlineData("(a?.b)! = 42;")]
    [InlineData("(42 as number) = 1;")]
    [InlineData("(42!)++;")]
    [InlineData("const value = x as A<B;")]
    [InlineData("const value = x as number<number>;")]
    [InlineData("const value = x as number ?? y || z;")]
    [InlineData("const value = x + 2 as number ** 2;")]
    [InlineData("const value = x as num\\u0062er;")]
    [InlineData("type T = { [P in keyof X]: X[P]; extra: number };")]
    [InlineData("type T = { value: number; [P in keyof X]: X[P] };")]
    [InlineData("interface I { [P in keyof X]: X[P] }")]
    [InlineData("type T = { +[P in X]: number };")]
    [InlineData("type T = { [P in X]+: number };")]
    [InlineData("type T = { [P in X]?: number, };")]
    [InlineData("type T = `value${}`;")]
    [InlineData("type T = `value${number + string}`;")]
    [InlineData("type T = `bad\\u{110000}`;")]
    [InlineData("type T = `bad${number}\\xZ1`;")]
    [InlineData("type T = Shape[ ];[")]
    [InlineData("function f<>(x) {}")]
    [InlineData("function f<T extends>(x) {}")]
    [InlineData("function f<T = >(x) {}")]
    [InlineData("function f<T>(x: T = 42);")]
    public void Rejects_invalid_new_syntax(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));

    [Theory]
    [InlineData("value as number + 1")]
    [InlineData("value satisfies number + 1")]
    [InlineData("value! + 1")]
    public void Assertions_preserve_evaluation_order_and_run_once(string expression)
    {
        var source = "let calls = 0; function get() { calls++; return 41; } const result = " +
            expression.Replace("value", "get()", StringComparison.Ordinal) + "; result === 42 && calls === 1;";
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Assertions_preserve_throw_locations_and_official_nodes()
    {
        const string source = "// 😀\r\ntype T = {value: number};\r\nconst value = null;\r\n(value as T)!.value;";
        var script = Compiler.ParseScript(source, "assert.ts");
        HardeningTests.AssertLocations(script, source, "assert.ts");
        Assert.All(script.Body, node => Assert.Same(typeof(Script).Assembly, node.GetType().Assembly));
        var error = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source, "assert.ts")));
        Assert.Equal(4, error.Location.Start.Line);
        Assert.Equal("assert.ts", error.Location.SourceFile);
    }

    [Fact]
    public void Assertion_erasure_preserves_direct_eval_and_receiver_binding()
    {
        const string source = """
            function f() { const local = 42; return (eval as Callable)('local'); }
            const obj = { value: 42, method() { return this.value; } };
            f() === 42 && (obj.method as Callable)() === 42 && obj.method!() === 42;
            """;
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Generic_functions_have_no_runtime_type_bindings()
    {
        const string source = "function f<T>(x: T): T { return typeof T + ':' + x; } f(42);";
        Assert.Equal("undefined:42", new Engine().Evaluate(Compiler.PrepareScript(source)).AsString());
    }

    [Theory]
    [InlineData("keyof ", "")]
    [InlineData("{[P in T]:", "}")]
    [InlineData("`value${", "}`")]
    public void New_types_enforce_depth_limits(string prefix, string suffix)
    {
        var source = "type T = " + string.Concat(Enumerable.Repeat(prefix, 100)) + "number" + string.Concat(Enumerable.Repeat(suffix, 100)) + ";";
        var compiler = new TypeScriptCompiler(new() { MaxTypeDepth = 8 });
        Assert.Equal("TypeDepthLimit", Assert.Throws<TypeScriptParseException>(() => compiler.ParseScript(source)).Code);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Repeated_assertions_are_iterative_and_use_linear_token_budget(int count)
    {
        var source = "42" + string.Concat(Enumerable.Repeat(" as number", count)) + ";";
        var compiler = new TypeScriptCompiler(new() { MaxTokenCount = count * 3 + 10, MaxNodeCount = 3 });
        Assert.Single(compiler.ParseScript(source).Body);
        var bang = "42" + new string('!', count) + ";";
        Assert.Single(compiler.ParseScript(bang).Body);
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxTokenCount = count }).ParseScript(source)).Code);
    }

    [Theory]
    [InlineData("42", " as number", ";")]
    [InlineData("42", "!", ";")]
    [InlineData("type T = `", "value${number}", "`;")]
    public void Repeated_erased_syntax_has_bounded_allocations(string prefix, string fragment, string suffix)
    {
        var small = prefix + string.Concat(Enumerable.Repeat(fragment, 100)) + suffix;
        var large = prefix + string.Concat(Enumerable.Repeat(fragment, 5000)) + suffix;
        var before = HardeningTests.Allocated(() => Compiler.ParseScript(small));
        var after = HardeningTests.Allocated(() => Compiler.ParseScript(large));
        Assert.True(after - before < 4096, $"Repeated syntax allocated {after - before} additional bytes");
    }

    [Theory]
    [InlineData(572124)]
    [InlineData(20260907)]
    public void New_syntax_mutations_keep_locations_or_report_public_errors(int seed)
    {
        string[] sources = [
            "const value = (42 as number)! + 2;",
            "function f<T extends {value: number}>(x: T) {return x!.value as number;}",
            "type T = {[P in keyof Shape as `get${P}`]-?: Shape[P]};",
            "type T = `value${number | string}`;",
            "const f = (x = 42 as number) => x;",
            "let x; (x as number) = 42;",
            "const value = (x?.method as Callable)!?.();",
            "type T = keyof Shape[keyof Shape];"
        ];
        string[] fragments = ["as", "satisfies", "keyof", "in", "!", "<", ">", "?", ":", "[", "]", "{", "}", "`", "${", "**", "\r\n", "😀", "\0", "'", "\\"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(source.Length);
            source = i % 2 == 0 ? source.Insert(at, fragments[random.Next(fragments.Length)]) : source.Remove(at, 1);
            try { HardeningTests.AssertLocations(Compiler.ParseScript(source, "parity.ts"), source, "parity.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("parity.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }
}
