using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public partial class SignatureTests
{
    [Theory]
    [InlineData("interface I { get value(): number; set value(value: number); }")]
    [InlineData("type I = { get value(); set value(value); };")]
    [InlineData("interface I<T> { get 'value'(): T; set 42(value: T); }")]
    [InlineData("interface I { get\nvalue(): number; set /* comment */ value(value: number); }")]
    [InlineData("interface I { get: number; set(): number; readonly: boolean; readonly get: number; }")]
    [InlineData("interface I { get get(): number; set set(value: number); }")]
    [InlineData("interface I { get [Symbol.iterator](): number; set [Keys.value](value: number); }")]
    [InlineData("interface I { readonly [key]?: number; [Symbol.iterator](): Iterator<number>; }")]
    [InlineData("interface I { [key]<T>({value}: {value:T}): T; [key]?: number; }")]
    [InlineData("interface I { ['value']: number; [42]: number; [-1]: number; [1n]: number; }")]
    [InlineData("interface I { [Keys['value']]: number; [Keys[other.key]]: number; [this.key]: number; }")]
    [InlineData("interface I { [Symbol.default]: number; [key]: number; [name: string]: number; }")]
    [InlineData("type F = ({value}: {value:number}) => number;")]
    [InlineData("type F = ([value]: [number]) => number;")]
    [InlineData("type F = ({value: renamed, nested: [first,,...rest], ...others}: Input) => number;")]
    [InlineData("type F = ({'value': renamed, 42: other, [key]: computed}: Input) => number;")]
    [InlineData("type F = ({default: value, if: other}: Input) => number;")]
    [InlineData("type F = ({value}?: Input, [first]?: Tuple) => number;")]
    [InlineData("type F = (...[first, ...rest]: number[]) => number;")]
    [InlineData("type F = (...{length}: ArrayLike<number>) => number;")]
    [InlineData("type F = ([...[first, ...rest]]: number[][]) => number;")]
    [InlineData("type F = ([...{length}]: number[]) => number;")]
    [InlineData("type F = ({}, [], {value}, [other],) => number;")]
    [InlineData("type F = <T>(this: Host, {value}: Input<T>, [other]: [T]) => T;")]
    [InlineData("type F = abstract new ({value}: Input) => Host;")]
    [InlineData("interface I { ({value}: Input): number; new ([value]: [number]): Host; f({value}: Input): number; }")]
    [InlineData("type I = { set value({value}: Input); set other([value]: [number]); };")]
    [InlineData("type F = (({value}: Input) => number) | (([value]: [number]) => string);")]
    [InlineData("type F = X extends ({value}: Input) => infer R ? R : never;")]
    [InlineData("type F = ({value}: Input) => T extends U ? X : Y;")]
    public void Type_member_and_binding_forms_erase_completely(string declaration)
    {
        Assert.Empty(new TypeScriptCompiler(new() { MaxNodeCount = 1 }).ParseScript(declaration).Body);
        Assert.Equal(42, new Engine().Evaluate(Compiler.PrepareScript(declaration + " 42;")).AsNumber());
    }

    [Theory]
    [InlineData("interface I { get value(x: number): number; }")]
    [InlineData("interface I { set value(); }")]
    [InlineData("interface I { set value(x: number, y: number); }")]
    [InlineData("interface I { set value(value?: number); }")]
    [InlineData("interface I { set value([value]?: number[]); }")]
    [InlineData("interface I { set value(...values: number[]); }")]
    [InlineData("interface I { get value(this: I): number; }")]
    [InlineData("interface I { set value(this: I); }")]
    [InlineData("interface I { set value(value: number): void; }")]
    [InlineData("interface I { get value<T>(): T; }")]
    [InlineData("interface I { set value<T>(value: T); }")]
    [InlineData("interface I { get value?(): number; }")]
    [InlineData("interface I { readonly get value(): number; }")]
    [InlineData("interface I { get value: number; }")]
    [InlineData("interface I { get value(): number {} }")]
    [InlineData("interface I { get [key: string](): number; }")]
    [InlineData("interface I { []: number; }")]
    [InlineData("interface I { [Symbol.]: number; }")]
    [InlineData("interface I { [Symbol.#key]: number; }")]
    [InlineData("interface I { [key, other]: number; }")]
    [InlineData("interface I { [key()]: number; }")]
    [InlineData("interface I { [++key]: number; }")]
    [InlineData("interface I { [key + other]: number; }")]
    [InlineData("interface I { [key?.value]: number; }")]
    [InlineData("interface I { [-true]: number; }")]
    [InlineData("interface I { [key]: number = 1; }")]
    [InlineData("interface I { readonly [key](): number; }")]
    [InlineData("type F = ({...rest, value}: Input) => number;")]
    [InlineData("type F = ({...rest,}: Input) => number;")]
    [InlineData("type F = ({...{value}}: Input) => number;")]
    [InlineData("type F = ([...rest, value]: Input) => number;")]
    [InlineData("type F = ([...rest,]: Input) => number;")]
    [InlineData("type F = ({value = 1}: Input) => number;")]
    [InlineData("type F = ([value = 1]: Input) => number;")]
    [InlineData("type F = ({value}: Input = {}) => number;")]
    [InlineData("type F = ({[key()]: value}: Input) => number;")]
    [InlineData("type F = ({[key]}: Input) => number;")]
    [InlineData("type F = ({42}: Input) => number;")]
    [InlineData("type F = ({if}: Input) => number;")]
    [InlineData("type F = ({value()}: Input) => number;")]
    [InlineData("type F = ({value?: other}: Input) => number;")]
    [InlineData("type F = ([value.other]: Input) => number;")]
    [InlineData("type F = (...[value]?: Input) => number;")]
    [InlineData("type F = (...[value]: Input, other: number) => number;")]
    [InlineData("type F = ({value}: Input, this: Host) => number;")]
    [InlineData("type F = ({value}: Input) =>;")]
    [InlineData("type F = ([...[value,...rest}]:Input)=>number;")]
    public void Type_member_and_binding_neighbors_fail_with_public_diagnostics(string source)
    {
        var error = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "members.ts"));
        Assert.Equal("members.ts", error.SourceFile);
        Assert.InRange(error.Index, 0, source.Length);
    }

    [Theory]
    [InlineData("({value: number})")]
    [InlineData("([number, string])")]
    [InlineData("({nested: {value: [number, string]}})")]
    [InlineData("({value: `item-${number}`})")]
    [InlineData("({value: Array<Array<number>>})")]
    [InlineData("({[P in keyof T]: T[P]})")]
    [InlineData("({get value(): number; set value(v: number)})")]
    [InlineData("({[Symbol.iterator](): Iterator<number>})")]
    public void Grouped_types_remain_distinct_from_destructured_function_types(string type)
    {
        Assert.Empty(Compiler.ParseScript($"type T = {type};").Body);
        Assert.Equal(42, new Engine().Evaluate(Compiler.PrepareScript($"function f(x: {type}) {{ return 42; }} f(null);")).AsNumber());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Type_members_never_read_keys_declare_parameters_or_emit_accessors(bool staticAnalysis)
    {
        var compiler = new TypeScriptCompiler(new() { StaticAnalysis = staticAnalysis });
        const string source = """
            let reads = 0;
            const keys = { get key() { reads++; throw new Error('type key evaluated'); } };
            const value = 40, other = 2;
            interface Contract {
                get [keys.key](): number;
                set [missing.key]({value}: {value:number});
                [keys[missing.key]]({other}: {other:number}): number;
            }
            type Handler = ({value}: {value:number}, [other]: [number]) => number;
            function f(x: Contract, fn: Handler): number { return value + other + reads; }
            f(null, null);
            """;
        Assert.Equal(42, new Engine().Evaluate(compiler.PrepareScript(source)).AsNumber());
        var module = compiler.PrepareModule(source + " export const answer = value + other + reads;", "members");
        var engine = new Engine();
        engine.Modules.Add("members", b => b.AddModule(module));
        Assert.Equal(42, engine.Modules.Import("members").Get("answer").AsNumber());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Typed_accessors_symbol_methods_and_destructured_callbacks_execute_together(bool staticAnalysis)
    {
        const string source = """
            const applyKey = Symbol('apply');
            interface Rule {
                get value(): number;
                set value(value: number | string);
                [applyKey]({amount}: {amount:number}): number;
                [Symbol.iterator](): Iterator<number>;
            }
            let stored = 0;
            const rule: Rule = {
                get value(): number { return stored; },
                set value(value: number | string) { stored = Number(value); },
                [applyKey]({amount}: {amount:number}): number { return stored + amount; },
                *[Symbol.iterator]() { yield stored; yield 2; }
            };
            type Apply = ({rule, amount}: {rule:Rule;amount:number}) => number;
            const apply: Apply = ({rule, amount}) => rule[applyKey]({amount});
            rule.value = '40';
            JSON.stringify([rule.value, apply({rule, amount:2}), [...rule].reduce((a,b)=>a+b,0)]);
            """;
        var compiler = new TypeScriptCompiler(new() { StaticAnalysis = staticAnalysis });
        Assert.Equal("[40,42,42]", new Engine().Evaluate(compiler.PrepareScript(source)).AsString());
    }

    [Theory]
    [InlineData("const f = (x): {get value(): number; [key]: number} => x; f(42);")]
    [InlineData("const f = (x): ({value}: Input) => number => x; f(42);")]
    [InlineData("const f = (x): ([value]: [number]) => number => x; f(42);")]
    [InlineData("const f = <T extends {[Symbol.iterator](): Iterator<T>}>(x: T): T => x; f<number>(42);")]
    [InlineData("function f<T>(x: T) {return x;} f<({value}: Input) => number>(42);")]
    [InlineData("function f<T>(x: T) {return x;} f<{[Symbol.iterator](): Iterator<T>}>(42);")]
    [InlineData("type I = {get value(): number}; /ok/.test('ok') ? 42 : 0;")]
    [InlineData("type F = ({value}: Input) => number; /ok/.test('ok') ? 42 : 0;")]
    [InlineData("const get = 40, set = 2; type T = {get get(): number;set set(v:number)}; get + set;")]
    public void New_type_grammar_preserves_lookahead_and_runtime_expression_boundaries(string source) =>
        Assert.Equal(42, new Engine().Evaluate(Compiler.PrepareScript(source)).AsNumber());

    [Fact]
    public void Type_members_preserve_original_ast_and_error_locations()
    {
        const string source = "// 😀\r\ninterface I {get [Symbol.iterator](): number;}\r\ntype F = ({value}: I) => number;\r\nfunction f(x: I): number { return x.value; }\r\nf(null);";
        HardeningTests.AssertLocations(Compiler.ParseScript(source, "members.ts"), source, "members.ts");
        var error = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source, "members.ts")));
        Assert.Equal(4, error.Location.Start.Line);
        Assert.Equal("members.ts", error.Location.SourceFile);
        const string invalid = "// 😀\r\ninterface I {\r\n get value(x: number): number;\r\n}";
        var parseError = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(invalid, "invalid.ts"));
        Assert.Equal(3, parseError.Position.Line);
        Assert.Equal(invalid.IndexOf("x: number", StringComparison.Ordinal), parseError.Index);
    }

    [Theory]
    [InlineData("get value():number;set value(v:number);")]
    [InlineData("[Symbol.iterator]():Iterator<number>;[key]:number;")]
    [InlineData("f({value: [first,,...rest], ...other}: Input): number;")]
    [InlineData("set value({nested:{value}}:Input);")]
    public void Erased_type_members_have_constant_allocations_and_linear_token_cost(string member)
    {
        var smallSource = "interface I {" + member + "}";
        var largeSource = "interface I {" + string.Concat(Enumerable.Repeat(member, 10000)) + "}";
        var compiler = new TypeScriptCompiler(new() { MaxNodeCount = 1, MaxTokenCount = 450000 });
        Assert.Empty(compiler.ParseScript(largeSource).Body);
        var small = HardeningTests.Allocated(() => compiler.ParseScript(smallSource));
        var large = HardeningTests.Allocated(() => compiler.ParseScript(largeSource));
        Assert.True(large - small < 4096, $"Erased type members allocated {large - small} additional bytes");
        var error = Assert.Throws<TypeScriptParseException>(() => new TypeScriptCompiler(new() { MaxTokenCount = 30 }).ParseScript(largeSource));
        Assert.Equal("TokenLimit", error.Code);
    }

    [Theory]
    [InlineData("type T=[", "({value:number})", "];")]
    [InlineData("type T=(", "{value}:Input", ")=>number;")]
    public void Type_binding_lookahead_has_linear_token_cost_and_constant_allocations(string prefix, string item, string suffix)
    {
        var smallSource = prefix + item + suffix;
        var largeSource = prefix + string.Join(',', Enumerable.Repeat(item, 10000)) + suffix;
        var compiler = new TypeScriptCompiler(new() { MaxNodeCount = 1, MaxTokenCount = 300000 });
        Assert.Empty(compiler.ParseScript(largeSource).Body);
        var small = HardeningTests.Allocated(() => compiler.ParseScript(smallSource));
        var large = HardeningTests.Allocated(() => compiler.ParseScript(largeSource));
        Assert.True(large - small < 4096, $"Type binding lookahead allocated {large - small} additional bytes");
    }

    [Theory]
    [InlineData("type F=([", "[", "]:Input)=>number;")]
    [InlineData("type F=({", "value:{", "}:Input)=>number;")]
    [InlineData("interface I {[key", "[key", "]:number;}")]
    [InlineData("interface I {get value():", "Array<", "number;}")]
    public void Nested_type_members_and_bindings_respect_type_limits(string prefix, string nested, string suffix)
    {
        var error = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(prefix + string.Concat(Enumerable.Repeat(nested, 1000)) + suffix));
        Assert.Equal("TypeDepthLimit", error.Code);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(200)]
    public void Raised_depth_allows_valid_patterns_beyond_the_inline_lookahead_buffer(int depth)
    {
        var source = "type F=(" + new string('[', depth) + "value" + new string(']', depth) + ":Input)=>number;";
        Assert.Empty(new TypeScriptCompiler(new() { MaxTypeDepth = 512, MaxNodeCount = 1 }).ParseScript(source).Body);
    }

    [Theory]
    [InlineData(19260910)]
    [InlineData(7212124)]
    public void Mutated_type_members_report_public_errors_or_valid_locations(int seed)
    {
        string[] sources = ["interface I {get [Symbol.iterator]():number;set value({value}:Input);}",
            "type F=({value:[first,,...rest],...other}:Input)=>number;",
            "const f=(x):({[Keys.value]:value}:Input)=>number=>x;",
            "type I=({value:`item-${number}`,nested:[number,string]});",
            "interface I {[Keys[other.key]]<T>({value}:Input<T>):T;}"];
        string[] fragments = ["<", ">", "=>", "!", "?.", "?", ":", "=", ",", ";", "{", "}", "(", ")", "[", "]",
            "/*", "`", "${", "\n", "\0", "'", "\\", "this", "get", "set", "readonly", "...", "#key"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(source.Length);
            source = i % 2 == 0 ? source.Insert(at, fragments[random.Next(fragments.Length)]) : source.Remove(at, 1);
            try { HardeningTests.AssertLocations(Compiler.ParseScript(source, "members.ts"), source, "members.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("members.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }
}
