using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class CompilerTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("const x: number = 40; x + 2", 42)]
    [InlineData("let x: string | number = 1; { let y: number = 2; x += y; } x", 3)]
    [InlineData("function add(x: number, y: number = 2): number { return x + y; } add(40)", 42)]
    [InlineData("function sum(...values: number[]): number { return values.reduce((a, b) => a + b, 0); } sum(20, 22)", 42)]
    [InlineData("let xs: Array<Array<number>> = [[42]]; xs[0][0]", 42)]
    [InlineData("let x: (string | number)[] = [42]; x[0]", 42)]
    [InlineData("let x: My.Namespace.Result<string, number> = 42; x", 42)]
    [InlineData("const { x }: Result = { x: 42 }; x", 42)]
    [InlineData("function f({ x }: Result, [y]: number[]): number { return x + y; } f({x: 40}, [2])", 42)]
    [InlineData("const o = { f(x: number): number { return x + 1; } }; o.f(41)", 42)]
    [InlineData("class C { f(x: number): number { return x + 1; } } new C().f(41)", 42)]
    [InlineData("let total: number = 0; for (let i: number = 0; i < 3; i++) total += i; total", 3)]
    [InlineData("let total: number = 0; for (const x: number of [20, 22]) total += x; total", 42)]
    public void Executes_supported_annotations(string source, double expected) =>
        Assert.Equal(expected, new Engine().Evaluate(Compiler.PrepareScript(source, "rules.ts")).AsNumber());

    [Theory]
    [InlineData("function f(value) { return !value; } f(false)", true)]
    [InlineData("const o = { value: 1 }; o.value === 1", true)]
    [InlineData("const a = 1, b = 2, c = 3; a < b < c", true)]
    [InlineData("const a = 1, b = 2; (a < b ? a : b) === 1", true)]
    [InlineData("let as = 2; let satisfies = 3; as + satisfies === 5", true)]
    [InlineData("const a = 64; a >> 2 === 16 && a >>> 3 === 8 && a << 1 === 128", true)]
    [InlineData("const x: number = 1; `/not a regex/:${x}` === '/not a regex/:1'", true)]
    public void Preserves_javascript_semantics(string source, bool expected) =>
        Assert.Equal(expected, new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Regex_literals_have_Jint_metadata(bool staticAnalysis)
    {
        var compiler = new TypeScriptCompiler(new() { StaticAnalysis = staticAnalysis });
        var source = "const count: number = 2;\n /a+/u.test('aaa') && /[\\/]/.test('/') && count / 2 === 1";
        Assert.True(new Engine().Evaluate(compiler.PrepareScript(source, "regex.ts")).AsBoolean());
    }

    [Fact]
    public void Produces_official_nodes_with_original_locations()
    {
        const string source = "// 😀\r\nconst x:\r\n Array<number> = [42];\r\nx[0]";
        var script = Compiler.ParseScript(source, "positions.ts");
        Assert.Same(typeof(Script).Assembly, script.GetType().Assembly);
        Assert.Equal(source.Length, script.End);
        var declaration = Assert.IsType<VariableDeclaration>(script.Body[0]);
        var id = Assert.IsType<Identifier>(declaration.Declarations[0].Id);
        Assert.Equal(source.IndexOf("x:", StringComparison.Ordinal), id.Start);
        Assert.Equal(id.Start + 1, id.End);
        Assert.Equal(2, id.Location.Start.Line);
        Assert.Equal(6, id.Location.Start.Column);
        Assert.Equal("positions.ts", id.Location.SourceFile);
        var last = script.Body[1];
        Assert.Equal(4, last.Location.Start.Line);
        Assert.Equal(0, last.Location.Start.Column);
    }

    [Fact]
    public void Reports_original_runtime_location()
    {
        var prepared = Compiler.PrepareScript("const x:\n Array<\n number> = [];\nthrow new Error('boom');", "runtime.ts");
        var exception = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(prepared));
        Assert.Equal(4, exception.Location.Start.Line);
        Assert.Equal("runtime.ts", exception.Location.SourceFile);
    }

    [Theory]
    [InlineData("const x: string number = 'bad';")]
    [InlineData("const x: Array<> = [];")]
    [InlineData("const x: Array<Array<number> = [];")]
    [InlineData("const x: number | = 1;")]
    [InlineData("const x: { value number } = { value: 1 };")]
    [InlineData("interface X { value: }")]
    [InlineData("type X = ;")]
    [InlineData("enum X { A }")]
    [InlineData("namespace X { export const a = 1; }")]
    [InlineData("class X { constructor(public x: number) {} }")]
    [InlineData("function f(...x?: number[]) {}")]
    [InlineData("const x = 1 as ;")]
    [InlineData("const x = 1 satisfies ;")]
    [InlineData("const x = value!~;")]
    [InlineData("const x: keyof = 1;")]
    [InlineData("const x: number<string> = 1;")]
    [InlineData("const x: string<boolean> = 1;")]
    [InlineData("function f<T extends>(x: T): T { return x; }")]
    [InlineData("const x = <div />;")]
    [InlineData("@decorate class C {}")]
    [InlineData("const C = @decorate class {};")]
    public void Rejects_unsupported_or_malformed_types(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "invalid.ts"));

    [Fact]
    public void Reports_syntax_error_in_original_source()
    {
        const string source = "const x:\n Array<\n number> = ;";
        var error = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "invalid.ts"));
        Assert.Equal(source.IndexOf(';'), error.Index);
        Assert.Equal(3, error.Position.Line);
        Assert.Equal("invalid.ts", error.SourceFile);
    }

    [Theory]
    [InlineData("/(a/")]
    [InlineData("/a/z")]
    [InlineData("/a/gg")]
    [InlineData("/\\p{NotAProperty}/u")]
    public void Regex_errors_use_original_offsets(string literal)
    {
        var source = "// 😀\r\nconst x: number = 1;\r\n  " + literal;
        var javascript = source.Replace(": number", "        ", StringComparison.Ordinal);
        var expected = Assert.Throws<Acornima.SyntaxErrorException>(() => new Acornima.Parser().ParseScript(javascript, "regex.ts"));
        var actual = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "regex.ts"));
        Assert.Equal(expected.Error.Index, actual.Index);
        Assert.Equal(expected.Error.Position, actual.Position);
        Assert.Equal("regex.ts", actual.SourceFile);
    }

    [Theory]
    [InlineData("'use strict'; function f(x: number = 1) { 'use strict'; }")]
    [InlineData("'use strict'; let x: number = 012;")]
    public void Preserves_javascript_early_errors(string source)
    {
        Assert.Throws<TypeScriptParseException>(() => Compiler.PrepareScript(source));
    }

    [Fact]
    public void Typed_constants_remain_immutable() =>
        Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript("const x: number = 1; x = 2;")));

    [Fact]
    public void Compiler_can_be_shared_and_recovers_after_failures()
    {
        Parallel.For(0, 32, i =>
        {
            Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript("let x: T<U = 1;"));
            var prepared = Compiler.PrepareScript($"const x: number = {i}; /ok/.test('ok') ? x : -1;");
            Assert.Equal(i, new Engine().Evaluate(prepared).AsNumber());
        });
    }

    [Fact]
    public void Executes_prepared_modules_with_import_aliases()
    {
        var engine = new Engine();
        engine.Modules.Add("values", builder => builder.AddModule(Compiler.PrepareModule("export const foo: number = 40;", "values.ts")));
        engine.Modules.Add("main", builder => builder.AddModule(Compiler.PrepareModule("import { foo as bar } from 'values'; export const result: number = bar + 2;", "main.ts")));
        Assert.Equal(42, engine.Modules.Import("main").Get("result").AsNumber());
    }

    [Theory]
    [InlineData("eval('const x: number = 1')")]
    [InlineData("new Function('x: number', 'return x')")]
    public void Runtime_strings_remain_javascript(string source) =>
        Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source)));

    [Fact]
    public void Prepared_script_is_reusable_across_engines()
    {
        var script = Compiler.PrepareScript("const x: number = input; /ok/.test('ok') ? x + 1 : 0");
        Assert.Equal(2, new Engine().SetValue("input", 1).Evaluate(script).AsNumber());
        Assert.Equal(42, new Engine().SetValue("input", 41).Evaluate(script).AsNumber());
    }

    [Fact]
    public void Enforces_limits_before_and_during_parsing()
    {
        Assert.Equal("SourceLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxSourceLength = 10 }).ParseScript(new string(' ', 11))).Code);
        Assert.Equal("NodeLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxNodeCount = 3 }).ParseScript("let a = 1, b = 2;")).Code);
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxTokenCount = 8 }).ParseScript("let x: A | B | C | D | E = 1;")).Code);
        Assert.Equal("TypeDepthLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxTypeDepth = 2 }).ParseScript("let x: A<B<C<number>>> = 1;")).Code);
        Assert.Equal("SyntaxDepthLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxSyntaxDepth = 10 }).ParseScript(new string('(', 20) + "1" + new string(')', 20))).Code);
        Assert.Throws<OperationCanceledException>(() => Compiler.ParseScript("let x: number = 1", cancellationToken: new CancellationToken(true)));
    }
}
