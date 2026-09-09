using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class SignatureTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("type T = readonly number;")]
    [InlineData("type T = readonly (number[] | string[]);")]
    [InlineData("type T = readonly (|number[]);")]
    [InlineData("type T = readonly (&number[]);")]
    [InlineData("type T = readonly number[][number];")]
    [InlineData("type T = readonly readonly number[];")]
    [InlineData("type T = unique number;")]
    [InlineData("type T = typeof;")]
    [InlineData("type T = typeof 42;")]
    [InlineData("type T = typeof obj.;")]
    [InlineData("type T = typeof obj.#x;")]
    [InlineData("type T = typeof f<>;")]
    [InlineData("type T = typeof f<<T>()=>T>;")]
    [InlineData("type T = typeof import('m').;")]
    [InlineData("let x: x is number;")]
    [InlineData("type T = x is number;")]
    [InlineData("function f(x: asserts x is number) {}")]
    [InlineData("function f(): x is;")]
    [InlineData("function f(): x\nis number {}")]
    [InlineData("function f(): asserts\nx {}")]
    [InlineData("function f(): asserts x is;")]
    [InlineData("function f(): asserts 42 {}")]
    [InlineData("function f(this) {}")]
    [InlineData("function f(this?: Context) {}")]
    [InlineData("function f(this: Context = {}) {}")]
    [InlineData("function f(...this: Context[]) {}")]
    [InlineData("function f(x, this: Context) {}")]
    [InlineData("function f(this: Context, this: Context) {}")]
    [InlineData("const f = (this: Context) => 42;")]
    [InlineData("const f = async (this: Context) => 42;")]
    [InlineData("const f = <T>(this: Context) => 42;")]
    [InlineData("const [this: Context] = [];")]
    [InlineData("class C {constructor(this: Context) {}}")]
    [InlineData("class C {get x(this: Context) {return 42;}}")]
    [InlineData("class C {set x(this: Context, value: number) {}}")]
    [InlineData("class C {get x(value: number): number;}")]
    [InlineData("class C {get x(): number;}")]
    [InlineData("class C {set x(value: number);}")]
    [InlineData("class C {set x();}")]
    [InlineData("class C {set x(...value: number[]);}")]
    [InlineData("class C {set x(value?: number);}")]
    [InlineData("function f(x=42): number;")]
    [InlineData("class C {f(x=42): number;}")]
    [InlineData("class C {constructor(x=42);}")]
    [InlineData("const f = function(x: number): number;")]
    [InlineData("const o = {f(x: number): number;};")]
    [InlineData("if (true) function f(x: number);")]
    [InlineData("label: function f(x: number);")]
    [InlineData("while (false) function f(x: number);")]
    [InlineData("if (true) declare const x: number;")]
    [InlineData("declare function f(): number {}")]
    [InlineData("declare var x = 42;")]
    [InlineData("declare let x = 42;")]
    [InlineData("declare const x: number = 42;")]
    [InlineData("declare const x = 40 + 2;")]
    [InlineData("declare const x = run();")]
    [InlineData("declare const x = null;")]
    [InlineData("declare const x = -true;")]
    [InlineData("declare const [x]: number[];")]
    [InlineData("declare let let: number;")]
    [InlineData("declare const let: number;")]
    [InlineData("'use strict'; declare const eval: number;")]
    [InlineData("'use strict'; declare const arguments: number;")]
    [InlineData("async function f() {declare const await: number;}")]
    [InlineData("function* f() {declare const yield: number;}")]
    [InlineData("class C {static {declare const arguments: number;}}")]
    [InlineData("declare const \\u0069f: number;")]
    [InlineData("declare class C {}")]
    [InlineData("declare namespace N {}")]
    [InlineData("abstract class C {abstract f(): number;}")]
    public void Rejects_invalid_and_deliberately_unsupported_neighbors(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "signatures.ts"));

    [Fact]
    public void This_parameters_preserve_arity_arguments_receiver_and_strict_directives()
    {
        const string source = """
            function f(this: Context, x: number) {"use strict"; return [this, x, arguments[0], arguments.length, f.length];}
            function g(this: Context, x = 42) {return [this.value, x, arguments.length, g.length];}
            function h(this: Context, ...x: number[]) {return [this.value, x[0], arguments.length, h.length];}
            JSON.stringify([f.call(40,2), g.call({value:40}), h.call({value:40},2)]);
            """;
        Assert.Equal("[[40,2,2,1,1],[40,42,0,0],[40,2,1,0]]", new Engine().Evaluate(Compiler.PrepareScript(source)).AsString());
    }

    [Fact]
    public void Signatures_create_no_runtime_bindings_members_or_computed_key_effects()
    {
        const string source = """
            declare const missing: typeof host;
            declare function host(x: number): number;
            function absent(x: number): number;
            let calls = 0;
            function key() {calls++; return 'f';}
            class C {
                constructor(x: number);
                constructor(x) {this.value = x;}
                [key()](x: number): number;
                [key()](x) {return this.value + x;}
                gone(x: number): number;
                #private(x: number): number;
                #private(x) {return x;}
                run() {return this.#private(42);}
            }
            const c = new C(40);
            calls === 1 && c.f(2) === 42 && c.run() === 42 && !('gone' in c) &&
                typeof missing === 'undefined' && typeof host === 'undefined' && typeof absent === 'undefined';
            """;
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Ambient_names_resolve_to_host_values_without_shadowing_them()
    {
        const string source = "declare const hostValue: number; declare function add(value: number): number; add(hostValue);";
        var engine = new Engine().SetValue("hostValue", 40).SetValue("add", (Func<double, double>)(x => x + 2));
        Assert.Equal(42, engine.Evaluate(Compiler.PrepareScript(source)).AsNumber());
    }

    [Theory]
    [InlineData("function f(x: number); /ok/.test('ok');")]
    [InlineData("declare function f(x: number); /ok/.test('ok');")]
    [InlineData("declare const x: number; /ok/.test('ok');")]
    [InlineData("{function f(x: number);} /ok/.test('ok');")]
    [InlineData("class C {f(x: number);} /ok/.test('ok');")]
    [InlineData("function f(x:number); 'use strict'; (function() {return this === undefined;})();")]
    [InlineData("declare const x: number; 'use strict'; (function() {return this === undefined;})();")]
    [InlineData("function f() {function g(x:number); 'use strict'; return this === undefined;} f();")]
    public void Erasure_preserves_regex_context_and_emitted_directive_prologues(string source) =>
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());

    [Theory]
    [InlineData("'use strict'; function f() {} let f;")]
    [InlineData("'use strict'; {let f; function f() {}}")]
    [InlineData("function f(x,x) {function g(x:number); 'use strict';}")]
    [InlineData("function f(this: Context, x, x) {'use strict';}")]
    [InlineData("function f(this: Context, x=42) {'use strict';}")]
    [InlineData("function f(this: Context, ...x) {'use strict';}")]
    public void Runtime_binding_and_strict_parameter_checks_still_apply(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));

    [Fact]
    public void Modules_export_implementations_and_keep_empty_module_identity()
    {
        var engine = new Engine();
        engine.Modules.Add("overloads", b => b.AddModule(Compiler.PrepareModule("""
            export declare const absent: number;
            export function f(x: number): number;
            export function f(x: string): string;
            export function f(x) {return x;}
            export default function g(x: number): number;
            export default function g(x) {return x;}
            """, "overloads")));
        engine.Modules.Add("consumer", b => b.AddModule(Compiler.PrepareModule("""
            import g, {f} from 'overloads'; export const answer = f(40) + g(2);
            """, "consumer")));
        Assert.Equal(42, engine.Modules.Import("consumer").Get("answer").AsNumber());
        Assert.True(engine.Modules.Import("overloads").Get("absent").IsUndefined());
        var module = Compiler.ParseModule("export declare const x: number; export function f(): number;");
        Assert.IsType<ExportNamedDeclaration>(Assert.Single(module.Body));
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseModule("export const f=1; export function f() {}"));
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseModule("export default 1; export default function f() {}"));
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseModule("function f(): number; export {f};"));
    }

    [Fact]
    public void Remaining_nodes_and_runtime_errors_keep_original_source_locations()
    {
        const string source = "// 😀\r\ndeclare const absent: number;\r\nfunction f(this: Context, x: unknown): x is number;\r\nfunction f(this: Context, x: unknown) {return x.value;}\r\nf(null);";
        var script = Compiler.ParseScript(source, "signatures.ts");
        HardeningTests.AssertLocations(script, source, "signatures.ts");
        var function = Assert.IsType<FunctionDeclaration>(script.Body[0]);
        Assert.Equal("x", Assert.IsType<Identifier>(Assert.Single(function.Params)).Name);
        Assert.Equal(source.LastIndexOf("x: unknown)", StringComparison.Ordinal), function.Params[0].Start);
        var error = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source, "signatures.ts")));
        Assert.Equal(4, error.Location.Start.Line);
        Assert.Equal("signatures.ts", error.Location.SourceFile);
    }

    [Theory]
    [InlineData("type T = readonly ", "number[]")]
    [InlineData("function f(this: ", "number) {}")]
    [InlineData("function f(x: unknown): x is ", "number {}")]
    [InlineData("declare const x: ", "number;")]
    public void New_type_positions_respect_depth_limits(string prefix, string suffix)
    {
        var source = prefix + string.Concat(Enumerable.Repeat("Array<", 100)) + suffix;
        var error = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));
        Assert.Equal("TypeDepthLimit", error.Code);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Repeated_signatures_have_linear_token_cost_and_no_output_statements(int count)
    {
        var compiler = new TypeScriptCompiler(new() {MaxTokenCount = count * 36 + 32});
        Assert.Empty(compiler.ParseScript(string.Concat(Enumerable.Repeat("function f(this: Context, x: unknown): asserts x is number;", count))).Body);
        Assert.Empty(compiler.ParseScript(string.Concat(Enumerable.Repeat("declare const x: readonly (typeof value)[];", count))).Body);
    }

    [Fact]
    public void Query_and_this_types_allocate_no_type_nodes()
    {
        var small = HardeningTests.Allocated(() => Compiler.ParseScript("function f(this: T) {}"));
        var source = "function f(this: " + string.Join(" | ", Enumerable.Repeat("typeof value", 10000)) + ") {}";
        var large = HardeningTests.Allocated(() => Compiler.ParseScript(source));
        Assert.True(large - small < 4096, $"Erased this type allocated {large-small} additional bytes");
        var tokenError = Assert.Throws<TypeScriptParseException>(() => new TypeScriptCompiler(new() {MaxTokenCount = 10}).ParseScript(source));
        Assert.Equal("TokenLimit", tokenError.Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Compiler.ParseScript(source, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Repeated_ambient_names_allocate_no_binding_nodes()
    {
        const string declaration = "declare const host: readonly (typeof value)[];";
        var small = HardeningTests.Allocated(() => Compiler.ParseScript(declaration));
        var source = string.Concat(Enumerable.Repeat(declaration, 10000));
        var large = HardeningTests.Allocated(() => Compiler.ParseScript(source));
        Assert.True(large - small < 4096, $"Ambient erasure allocated {large-small} additional bytes");
        Assert.Empty(new TypeScriptCompiler(new() {MaxNodeCount = 1}).ParseScript(source).Body);
    }

    [Theory]
    [InlineData("declare var let: number; 42;")]
    [InlineData("declare const eval: number; 42;")]
    [InlineData("declare const arguments: number; 42;")]
    [InlineData("declare const await: number; 42;")]
    [InlineData("declare const yield: number; 42;")]
    [InlineData("declare const \\u0068ost: number; 42;")]
    public void Ambient_binding_fast_path_preserves_sloppy_and_escaped_identifiers(string source) =>
        Assert.Equal(42, new Engine().Evaluate(Compiler.PrepareScript(source)).AsNumber());

    [Theory]
    [InlineData(7212124)]
    [InlineData(19719)]
    public void Mutated_signatures_report_public_errors_or_valid_locations(int seed)
    {
        string[] sources = ["function f(this: Context, x: unknown): asserts x is readonly number[] {}",
            "function f<T>(this: Context, {x}: T): x is typeof value; function f(x) {return x;}",
            "class C {constructor(x:number); constructor(x) {} f(this:C, x:unknown): x is number; f(x) {return true;}}",
            "declare const x: typeof obj.value<Array<number>>, y: unique symbol;",
            "const f = (x): asserts x is readonly [number] => {};",
            "declare interface I {f(x:unknown): asserts x is readonly number[]}" ];
        string[] fragments = ["<", ">", "=>", "!", "?.", "?", ":", "=", ",", ";", "{", "}", "(", ")", "[", "]", "/*", "`", "${", "\n", "\0", "'", "\\", "this", "is", "asserts", "typeof", "declare"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(source.Length);
            source = i % 2 == 0 ? source.Insert(at, fragments[random.Next(fragments.Length)]) : source.Remove(at, 1);
            try { HardeningTests.AssertLocations(Compiler.ParseScript(source, "signatures.ts"), source, "signatures.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("signatures.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }
}
