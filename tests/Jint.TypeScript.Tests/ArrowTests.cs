using Xunit;

namespace Jint.TypeScript.Tests;

public class ArrowTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("const f = (x: number): number => x + 1; f(41)")]
    [InlineData("const f = (x: number = 41) => x + 1; f()")]
    [InlineData("const f = (x): number => x + 1; f(41)")]
    [InlineData("const f = (): number => 42; f()")]
    [InlineData("const f = (...xs: number[]): number => xs[0]; f(42)")]
    [InlineData("const f = ({x}: Result): number => x; f({x: 42})")]
    [InlineData("const f = ([x]: number[]): number => x; f([42])")]
    [InlineData("const f = (x: number, y = 2): number => { return x + y; }; f(40)")]
    [InlineData("const f = (x: Array<Array<number>>): number => x[0][0]; f([[42]])")]
    [InlineData("const f = (x: number = ((y: number) => y)(42)) => x; f()")]
    [InlineData("const f = (x: number = /a/.test('a') ? 42 : 0) => x; f()")]
    [InlineData("const f = (x: number): number => true ? (x) : 0; f(42)")]
    [InlineData("const f = false ? (x: number): number => 1 : (x: number): number => x; f(42)")]
    public void Executes_typed_arrows(string source) =>
        Assert.Equal(42, new Engine().Evaluate(Compiler.PrepareScript(source)).AsNumber());

    [Theory]
    [InlineData("const f = async (x: number): Promise<number> => x + 1; f(41)")]
    [InlineData("const f = async (x): Promise<number> => x + 1; f(41)")]
    [InlineData("const f = async (): Promise<number> => 42; f()")]
    [InlineData("const f = async (x: number = 41) => x + 1; f()")]
    public void Executes_async_typed_arrows(string source)
    {
        var engine = new Engine();
        engine.Evaluate(Compiler.PrepareScript(source)).UnwrapIfPromise();
        Assert.Equal(42, engine.Evaluate("f(41)").UnwrapIfPromise().AsNumber());
    }

    [Theory]
    [InlineData("const a = 40, b = 2; true ? (a) : b", 40)]
    [InlineData("const a = 40, b = 2; false ? (a) : b + 40", 42)]
    [InlineData("const a = 40, b = 2; false ? (a) : b / 2", 1)]
    [InlineData("const async = x => x; async(42)", 42)]
    [InlineData("const f = (x = true ? 40 : 0, y = 2) => x + y; f()", 42)]
    public void Preserves_parentheses_conditionals_and_calls(string source, double expected) =>
        Assert.Equal(expected, new Engine().Evaluate(Compiler.PrepareScript(source)).AsNumber());

    [Theory]
    [InlineData("(x: number)")]
    [InlineData("async(x: number)")]
    [InlineData("const f = (x: number)\n=> x;")]
    [InlineData("const f = (x: number): number\n=> x;")]
    [InlineData("const f = (x = 1: number) => x;")]
    [InlineData("const f = (...x: number[] = []) => x;")]
    [InlineData("const f = (...x: number[], y: number) => y;")]
    [InlineData("const f = (x?: number = 1) => x;")]
    [InlineData("const f = (x: number, x: number) => x;")]
    public void Rejects_invalid_typed_arrows(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));
}
