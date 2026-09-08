using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class ErasureTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("import type Shape from 'missing';")]
    [InlineData("import type * as Types from 'missing';")]
    [InlineData("import type { Shape as Other } from 'missing';")]
    [InlineData("export type { Shape } from 'missing';")]
    [InlineData("export type * from 'missing';")]
    [InlineData("export type * as Types from 'missing';")]
    public void Whole_type_modules_do_not_resolve_or_execute(string declaration)
    {
        var engine = new Engine();
        engine.Modules.Add("main", module => module.AddModule(Compiler.PrepareModule(declaration + " export const value = 42;")));
        Assert.Equal(42, engine.Modules.Import("main").Get("value").AsNumber());
    }

    [Theory]
    [InlineData("import { type Shape } from 'side';")]
    [InlineData("export { type Shape } from 'side';")]
    [InlineData("import { type Shape, value } from 'side';")]
    [InlineData("export { type Shape, value as other } from 'side';")]
    public void Inline_type_specifiers_preserve_module_side_effects(string declaration)
    {
        var engine = new Engine().SetValue("calls", 0);
        engine.Modules.Add("side", module => module.AddModule(Compiler.PrepareModule("calls++; export const value = 42;")));
        engine.Modules.Add("main", module => module.AddModule(Compiler.PrepareModule(declaration + " export const result = calls;")));
        Assert.Equal(1, engine.Modules.Import("main").Get("result").AsNumber());
    }

    [Theory]
    [InlineData("import type Shape from 'missing'; // 😀\r\n")]
    [InlineData("export interface Shape { value: number }\n")]
    [InlineData("export default interface Shape { value: number }\n")]
    public void Erased_modules_keep_an_empty_export_and_valid_locations(string source)
    {
        var module = Compiler.ParseModule(source, "types.ts");
        var export = Assert.IsType<ExportNamedDeclaration>(Assert.Single(module.Body));
        Assert.Empty(export.Specifiers);
        Assert.Equal(source.Length, export.Start);
        Assert.Equal(source.Length, export.End);
        HardeningTests.AssertLocations(module, source, "types.ts");
    }

    [Fact]
    public void Type_bindings_do_not_become_runtime_exports_or_variables()
    {
        var engine = new Engine();
        engine.Modules.Add("main", module => module.AddModule(Compiler.PrepareModule("""
            import type Shape from 'missing';
            export interface Other { value: number }
            export type Value = number;
            export const result = typeof Shape + ':' + typeof Other + ':' + typeof Value;
            """)));
        var exports = engine.Modules.Import("main");
        Assert.True(exports.Get("Shape").IsUndefined());
        Assert.True(exports.Get("Other").IsUndefined());
        Assert.True(exports.Get("Value").IsUndefined());
        Assert.Equal("undefined:undefined:undefined", exports.Get("result").AsString());
    }

    [Fact]
    public void Erased_declarations_keep_following_runtime_error_locations()
    {
        const string source = "// 😀\r\ntype T = { value: number };\r\ninterface Shape { value: T }\r\nthrow new Error('boom');";
        var error = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source, "erased.ts")));
        Assert.Equal(4, error.Location.Start.Line);
        Assert.Equal("erased.ts", error.Location.SourceFile);
    }

    [Theory]
    [InlineData("if (true) type T = number;")]
    [InlineData("while (false) interface I {}")]
    [InlineData("label: type T = number;")]
    [InlineData("function f() { import type T from 'missing'; }")]
    [InlineData("{ export type T = number; }")]
    [InlineData("function f({x}?: {x: number}) {}")]
    [InlineData("const f = ({x}?: {x: number}) => x;")]
    [InlineData("const f = (...x?: number[]) => x;")]
    [InlineData("function f(x?: number = 42) {}")]
    [InlineData("class C { set value(x?: number) {} }")]
    [InlineData("const o = { set value(x?: number) {} };")]
    [InlineData("type T = [number?, string];")]
    [InlineData("type T = [...number[]?];")]
    [InlineData("type T = { readonly method(): number };")]
    [InlineData("type T = { [key?: string]: number };")]
    [InlineData("type T = { value = 1 };")]
    [InlineData("type T = (...xs: number[], x: number) => void;")]
    [InlineData("type T = (x: number, this: T) => void;")]
    [InlineData("type T = (this?: T) => void;")]
    [InlineData("type T = (x = 1) => void;")]
    [InlineData("interface I extends A | B {}")]
    [InlineData("interface I extends {} {}")]
    [InlineData("interface I extends A[] {}")]
    [InlineData("interface I<T extends> {}")]
    [InlineData("type T<X=> = X;")]
    public void Rejects_invalid_contexts_and_forms(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));

    [Theory]
    [InlineData("import type T, { U } from 'missing';")]
    [InlineData("import type { type T } from 'missing';")]
    [InlineData("export type { type T } from 'missing';")]
    [InlineData("import type { 'T' } from 'missing';")]
    [InlineData("import type { T as default } from 'missing';")]
    [InlineData("import { type T as await } from 'missing';")]
    [InlineData("import type * from 'missing';")]
    [InlineData("import type T from 42;")]
    [InlineData("export type *;")]
    [InlineData("export type T;")]
    [InlineData("export type { 'T' as Other };")]
    [InlineData("export type { default };")]
    [InlineData("export { type default as T };")]
    [InlineData("export { type 'T' as Other } from 'missing';")]
    [InlineData("import { type 'T' as Other } from 'missing';")]
    [InlineData("type T = number; export { T };")]
    public void Rejects_invalid_or_implicit_type_module_forms(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseModule(source));

    [Theory]
    [InlineData("type T = readonly number;")]
    [InlineData("type T = T extends U ? X : Y;")]
    [InlineData("type T = (x: unknown) => x is;")]
    [InlineData("declare interface Shape {=}")]
    public void Unsupported_neighboring_grammar_still_fails_explicitly(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));

    [Theory]
    [InlineData("{ value: ", " }")]
    [InlineData("[", "]")]
    [InlineData("() => ", "")]
    [InlineData("<T extends ", ">(x: T) => T")]
    public void New_recursive_forms_respect_type_depth(string open, string close)
    {
        var source = "type T = " + string.Concat(Enumerable.Repeat(open, 100)) + "number" + string.Concat(Enumerable.Repeat(close, 100)) + ";";
        var error = Assert.Throws<TypeScriptParseException>(() => new TypeScriptCompiler(new() { MaxTypeDepth = 8 }).ParseScript(source));
        Assert.Equal("TypeDepthLimit", error.Code);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Wide_erased_types_use_linear_tokens_and_no_type_nodes(int count)
    {
        var properties = string.Concat(Enumerable.Repeat("value: number;", count));
        var source = "type T = {" + properties + "}; interface I {" + properties + "} 42;";
        var compiler = new TypeScriptCompiler(new() { MaxNodeCount = 3, MaxTokenCount = count * 9 + 30 });
        Assert.Single(compiler.ParseScript(source).Body);
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxTokenCount = count }).ParseScript(source)).Code);
    }

    [Theory]
    [InlineData(212419)]
    [InlineData(20260907)]
    public void Module_mutations_keep_locations_or_report_public_errors(int seed)
    {
        string[] sources = [
            "import type { Shape as T } from 'missing'; export const x: T = 42;",
            "import {type T, value as x} from 'side'; export { type T, x };",
            "export type {Shape as T} from 'missing';",
            "export interface I<T> {value: T}; export default 42;",
            "export type * as Types from 'missing';",
            "export default interface Shape {value?: number} // 😀\r\n"
        ];
        string[] insertions = ["type", "as", "from", "interface", "import", "export", "?", "<", ">", "\"", "'", "\\", ";", "{", "}", "\r\n", "😀", "\0"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(source.Length);
            source = i % 2 == 0 ? source.Insert(at, insertions[random.Next(insertions.Length)]) : source.Remove(at, 1);
            try { HardeningTests.AssertLocations(Compiler.ParseModule(source, "mutation.ts"), source, "mutation.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("mutation.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }

    [Theory]
    [InlineData("interface I {", "readonly value?: number;", "}")]
    [InlineData("type T = [", "number,", "];")]
    [InlineData("type T = {", "f(x?: number): number;", "};")]
    public void Repeated_erased_members_do_not_allocate_per_lookahead(string prefix, string member, string suffix)
    {
        var small = prefix + string.Concat(Enumerable.Repeat(member, 100)) + suffix;
        var large = prefix + string.Concat(Enumerable.Repeat(member, 5000)) + suffix;
        var smallBytes = HardeningTests.Allocated(() => Compiler.ParseScript(small));
        var largeBytes = HardeningTests.Allocated(() => Compiler.ParseScript(large));
        Assert.True(largeBytes - smallBytes < 4096, $"Repeated erased members allocated {largeBytes - smallBytes} extra bytes");
    }
}
