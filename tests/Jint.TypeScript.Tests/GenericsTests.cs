using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class GenericsTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("f<T>.value")]
    [InlineData("f<T>?.value")]
    [InlineData("f<T><U>")]
    [InlineData("f<T><U>(x)")]
    [InlineData("f<T> < x")]
    [InlineData("f<T> >= x")]
    [InlineData("f<T> > x")]
    [InlineData("f<T> >> x")]
    [InlineData("f<T>!")]
    [InlineData("(f<T>) = x")]
    [InlineData("(f<T>)++")]
    [InlineData("(f<T>) => 0")]
    [InlineData("async(f<T>) => 0")]
    [InlineData("({x: f<T>}) => 0")]
    [InlineData("f<T>(x: T)")]
    [InlineData("f<T>(x) => x")]
    [InlineData("async<T extends U>()")]
    [InlineData("async<T = U>()")]
    [InlineData("async\n<T>(x:T)=>x")]
    [InlineData("const f = <T>(x:T)\n=>x")]
    [InlineData("const f = <T>(x:T)")]
    [InlineData("const f = <T>(x as T)=>x")]
    [InlineData("new <T>() => 42")]
    [InlineData("class C<> {}")]
    [InlineData("class C implements {}")]
    [InlineData("class C implements A | B {}")]
    [InlineData("class C {constructor<T>() {}}")]
    [InlineData("class C {constructor?() {}}")]
    [InlineData("class C {get x<T>() {return 42;}}")]
    [InlineData("class C {get x?() {return 42;}}")]
    [InlineData("class C {set x?(value:number) {}}")]
    [InlineData("class C {readonly static value:number;}")]
    [InlineData("class C {static public value:number;}")]
    [InlineData("class C {value!:number = 42;}")]
    [InlineData("const o = {get x<T>() {return 42;}}")]
    [InlineData("class C {readonly f<T>() {}}")]
    [InlineData("class C {public private value: number;}")]
    [InlineData("class C {public public value: number;}")]
    [InlineData("class C {override value: number;}")]
    [InlineData("class C {public static {}}")]
    [InlineData("class C {value?!: number;}")]
    [InlineData("class C {value!;}")]
    [InlineData("class C {constructor(public value: number) {}}")]
    [InlineData("class C {declare value: number;}")]
    [InlineData("abstract class C {abstract f<T>(x:T):T;}")]
    public void Rejects_invalid_or_unsupported_neighbors(string source) =>
        Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source, "generics.ts"));

    [Fact]
    public void Erasure_preserves_optional_calls_receivers_eval_and_evaluation_order()
    {
        const string source = """
            let calls = 0;
            const obj = {value: 42, method<T>(x?: T) {return this.value;}};
            function argument() {calls++; return 42;}
            const missing = null;
            missing?.method<number>(argument());
            missing?.<number>(argument());
            function localEval() {const local = 42; return eval<string>('local');}
            obj.method<number>(argument()) === 42 && calls === 1 &&
                (obj.method<number>)() === 42 && localEval() === 42;
            """;
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Types_do_not_create_bindings_and_fields_keep_native_define_semantics()
    {
        const string source = """
            let sets = 0;
            class Base {set value(x) {sets++;}}
            class C<T> extends Base implements Shape<T> {
                public value: T;
                private optional?: T;
                readonly definite!: T;
                static count: number = 42;
                f<U>(x: U): U {return x;}
            }
            const c = new C<number>();
            const f = <V>(x: V): V => x;
            sets === 0 && Object.hasOwn(c, 'value') && Object.hasOwn(c, 'optional') &&
                Object.hasOwn(c, 'definite') && c.f<number>(f<number>(C.count)) === 42 &&
                typeof T === 'undefined' && typeof U === 'undefined' && typeof V === 'undefined';
            """;
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Generic_arrow_and_call_nodes_keep_original_ranges_and_error_locations()
    {
        const string source = "// 😀\r\nconst f = <T>(x: T): T => x;\r\nf<number>(null).value;";
        var script = Compiler.ParseScript(source, "generic.ts");
        HardeningTests.AssertLocations(script, source, "generic.ts");
        var arrow = Assert.IsType<ArrowFunctionExpression>(Assert.IsType<VariableDeclaration>(script.Body[0]).Declarations[0].Init);
        Assert.Equal(source.IndexOf('<'), arrow.Start);
        var error = Assert.Throws<JavaScriptException>(() => new Engine().Evaluate(Compiler.PrepareScript(source, "generic.ts")));
        Assert.Equal(3, error.Location.Start.Line);
        Assert.Equal("generic.ts", error.Location.SourceFile);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Wide_arguments_and_repeated_calls_have_linear_token_cost(int count)
    {
        // At most two token passes, plus the leading comparison and terminators.
        var compiler = new TypeScriptCompiler(new() {MaxTokenCount = count * 16 + 32, MaxNodeCount = count * 8});
        compiler.ParseScript("f<" + string.Join(',', Enumerable.Repeat("T", count)) + ">(42);");
        compiler.ParseScript(string.Concat(Enumerable.Repeat("f<T>(42);", count)));
        compiler.ParseScript(string.Join(" < ", Enumerable.Repeat("x", count)) + " < f<T>(42);");
        compiler.ParseScript("1 < " + string.Join(" < ", Enumerable.Repeat("f<T>(42)", count)) + ";");
    }

    [Fact]
    public void Wide_arguments_allocate_no_type_nodes()
    {
        var small = HardeningTests.Allocated(() => Compiler.ParseScript("f<T>(42);"));
        var source = "f<" + string.Join(',', Enumerable.Repeat("T", 10000)) + ">(42);";
        var large = HardeningTests.Allocated(() => Compiler.ParseScript(source));
        Assert.True(large - small < 4096, $"Wide type arguments allocated {large - small} additional bytes");
    }

    [Fact]
    public void Prepared_modules_execute_exported_generic_arrows_and_classes()
    {
        var engine = new Engine();
        engine.Modules.Add("generic-values", builder => builder.AddModule(Compiler.PrepareModule("""
            export default class Box<T> {
                private readonly value: T;
                constructor(value: T) {this.value = value;}
                get<U>(): U {return this.value;}
            }
            export const identity = <T>(value: T): T => value;
            """, "generic-values")));
        engine.Modules.Add("generic-consumer", builder => builder.AddModule(Compiler.PrepareModule("""
            import Box, {identity} from 'generic-values';
            export const answer: number = new Box<number>(identity<number>(42)).get<number>();
            """, "generic-consumer")));
        Assert.Equal(42, engine.Modules.Import("generic-consumer").Get("answer").AsNumber());
    }

    [Theory]
    [InlineData(2124)]
    [InlineData(719)]
    public void Mutated_generics_and_classes_report_public_errors_or_valid_locations(int seed)
    {
        string[] sources = ["const f = <T extends A<number>>(x:T):T => x; f<number>(42);",
            "const f = async<T=number>(x:T):Promise<T> => x; f?.<number>(42);",
            "class C<T> extends Base<T> implements I<T> {public readonly x: T; f<U>(x:U):U {return x;}}",
            "const f = x=>x; f<{x:A<B>}, <T>()=>T>(42);", "const f = x=>x; f<`value${number}`>(42);"];
        string[] fragments = ["<", ">", "<<", ">>>", "=>", "!", "?.", "?", ":", "=", ",", ";", "{", "}", "(", ")", "[", "]", "/*", "`", "${", "\n", "\0", "'", "\\"];
        var random = new Random(seed);
        for (var i = 0; i < 10000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(source.Length);
            source = i % 2 == 0 ? source.Insert(at, fragments[random.Next(fragments.Length)]) : source.Remove(at, 1);
            try { HardeningTests.AssertLocations(Compiler.ParseScript(source, "generics.ts"), source, "generics.ts"); }
            catch (TypeScriptParseException error)
            {
                Assert.InRange(error.Index, 0, source.Length);
                Assert.Equal("generics.ts", error.SourceFile);
            }
            catch (Exception error)
            {
                throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");
            }
        }
    }
}
