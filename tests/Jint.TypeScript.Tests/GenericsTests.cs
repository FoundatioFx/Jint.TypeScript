using Acornima.Ast;
using Jint.Runtime;
using Xunit;

namespace Jint.TypeScript.Tests;

public class GenericsTests
{
    private static readonly TypeScriptCompiler Compiler = new();

    [Theory]
    [InlineData("class B {value=42;} class C extends B {declare value:number;} new C().value;")]
    [InlineData("class B {value=42;} abstract class C extends B {abstract value:number;} new C().value;")]
    [InlineData("class B {static value=42;} class C extends B {declare static value:number;} C.value;")]
    [InlineData("abstract class C {abstract f(x:number):number;} class D extends C {f(x) {return x;}} new D().f(42);")]
    [InlineData("abstract class C {abstract get value():number; abstract set value(x:number);} class D extends C {get value() {return 42;}} new D().value;")]
    [InlineData("abstract class C {abstract f<const T>(this:C,x:T):T; f(x) {return this.value+x;} value=40;} new C().f(2);")]
    [InlineData("function build() {abstract class C {abstract f():number; value=42;} return C;} new (build())().value;")]
    [InlineData("let calls=0; function key(){calls++;return 'x';} abstract class C {abstract [key()]():number; declare [key()]:number; abstract get [key()]():number; value=42;} new C().value+calls;")]
    [InlineData("abstract class C {abstract constructor(x:number); value=42;} new C().value;")]
    [InlineData("type T=abstract new<const T>(x:T)=>T; const f=x=>x; f<T>(42);")]
    [InlineData("type T=X extends abstract new()=>infer U ? U:never; 42;")]
    [InlineData("class C {abstract=40; declare=2;} const c=new C(); c.abstract+c.declare;")]
    [InlineData("class C {abstract(){return 40;} declare(){return 2;}} const c=new C(); c.abstract()+c.declare();")]
    [InlineData("const abstract=40; abstract\nclass C {value=2;} abstract+new C().value;")]
    [InlineData("abstract class C {f(){class C {abstract=42;} return new C().abstract;} abstract x:number;} new C().f();")]
    public void Class_erasure_preserves_inheritance_and_executable_members(string source)
    {
        foreach (var analysis in new[] {true,false})
            Assert.Equal(42, new Engine().Evaluate(new TypeScriptCompiler(new() {StaticAnalysis=analysis}).PrepareScript(source)).AsNumber());
    }

    [Theory]
    [InlineData("class C {abstract x:number;}")]
    [InlineData("abstract class C {f(){class D {abstract x:number;}}}")]
    [InlineData("abstract class C {declare x=1;}")]
    [InlineData("abstract class C {abstract x=1;}")]
    [InlineData("abstract class C {abstract f() {}}")]
    [InlineData("abstract class C {abstract get x() {return 1;}}")]
    [InlineData("abstract class C {abstract set x(v) {}}")]
    [InlineData("class C {declare f():void;}")]
    [InlineData("class C {declare get x():number;}")]
    [InlineData("abstract class C {abstract static x:number;}")]
    [InlineData("abstract class C {static abstract x:number;}")]
    [InlineData("class C {declare override x:number;}")]
    [InlineData("abstract class C extends B {override abstract f():number;}")]
    [InlineData("abstract class C {abstract readonly f():number;}")]
    [InlineData("abstract class C {abstract abstract x:number;}")]
    [InlineData("class C {declare declare x:number;}")]
    [InlineData("abstract class C {abstract #x:number;}")]
    [InlineData("class C {declare #x:number;}")]
    [InlineData("abstract class C {abstract get x(v:number):number;}")]
    [InlineData("abstract class C {abstract set x();}")]
    [InlineData("abstract class C {abstract set x(a,b);}")]
    [InlineData("abstract class C {abstract set x(...args:number[]);}")]
    [InlineData("abstract class C {abstract set x(v?:number);}")]
    [InlineData("abstract class C {abstract f(x=1):number;}")]
    [InlineData("abstract class C {abstract async x:number;}")]
    [InlineData("abstract class C {abstract *x:number;}")]
    [InlineData("abstract class C {abstract get x:number;}")]
    [InlineData("class C {declare constructor:number;}")]
    [InlineData("class C {declare static prototype:number;}")]
    [InlineData("abstract class C {abstract constructor:number;}")]
    [InlineData("if(true) abstract class C {}")]
    [InlineData("label: abstract class C {}")]
    [InlineData("while(false) abstract class C {}")]
    [InlineData("const C=abstract class {};")]
    [InlineData("abstract class C {} let C;")]
    [InlineData("type T=abstract ()=>object;")]
    [InlineData("type T=abstract new()=>;")]
    [InlineData("type T=number | abstract new()=>object;")]
    public void Class_erasure_rejects_invalid_or_deliberately_unsupported_neighbors(string source) =>
        Assert.Throws<TypeScriptParseException>(()=>Compiler.ParseScript(source));

    [Theory]
    [InlineData("export abstract class C {abstract x:number; value=42;}","C")]
    [InlineData("export default abstract class {abstract get x():number; value=42;}","default")]
    [InlineData("export default abstract class C {abstract f():number; value=42;}","default")]
    public void Abstract_class_exports_keep_runtime_bindings(string source, string exportName)
    {
        var engine = new Engine();
        engine.Modules.Add("classes",b=>b.AddModule(Compiler.PrepareModule(source,"classes")));
        engine.SetValue("C",engine.Modules.Import("classes").Get(exportName));
        Assert.Equal(42,engine.Evaluate("new C().value;").AsNumber());
        Assert.Throws<TypeScriptParseException>(()=>Compiler.ParseModule(source+(exportName=="default"?" export default 42;":" export {C};")));
    }

    [Fact]
    public void Erased_fields_have_no_descriptors_and_preserve_original_locations()
    {
        const string source="// 😀\r\nabstract class C<const T> {\r\n abstract value:T; declare other:import('missing').Host; normal:number;\r\n abstract f(x:T):T; f(x) {return x.value;}\r\n}\r\nnew C().f(null);";
        var script=Compiler.ParseScript(source,"classes.ts");
        HardeningTests.AssertLocations(script,source,"classes.ts");
        var declaration=Assert.IsType<ClassDeclaration>(script.Body[0]);
        Assert.Equal(source.IndexOf("abstract class",StringComparison.Ordinal),declaration.Start);
        Assert.Equal(2,declaration.Body.Body.Count);
        var error=Assert.Throws<JavaScriptException>(()=>new Engine().Evaluate(Compiler.PrepareScript(source,"classes.ts")));
        Assert.Equal(4,error.Location.Start.Line);
        Assert.Equal("classes.ts",error.Location.SourceFile);
        Assert.Equal("normal",new Engine().Evaluate(Compiler.PrepareScript("abstract class C {abstract x:number; declare y:number; normal:number;} Object.keys(new C()).join(',');")).AsString());
    }

    [Theory]
    [InlineData("declare value:number;")]
    [InlineData("abstract readonly value:T extends U ? X:Y;")]
    [InlineData("declare value?:import('missing').Shape;")]
    [InlineData("abstract value!:number;")]
    public void Ordinary_erased_class_fields_have_constant_allocation_and_linear_token_cost(string member)
    {
        var small="abstract class C<T> {"+member+"}";
        var large="abstract class C<T> {"+string.Concat(Enumerable.Repeat(member,10000))+"}";
        var bytes=HardeningTests.Allocated(()=>Compiler.ParseScript(large))-HardeningTests.Allocated(()=>Compiler.ParseScript(small));
        Assert.True(bytes<4096,$"Erased fields allocated {bytes} extra bytes");
        var compiler=new TypeScriptCompiler(new() {MaxNodeCount=4,MaxTokenCount=400000});
        Assert.Empty(Assert.IsType<ClassDeclaration>(compiler.ParseScript(large).Body[0]).Body.Body);
        Assert.Equal("TokenLimit",Assert.Throws<TypeScriptParseException>(()=>new TypeScriptCompiler(new(){MaxTokenCount=32}).ParseScript(large)).Code);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>Compiler.ParseScript(large,cancellationToken:cancel.Token));
    }

    [Fact]
    public void Erased_computed_keys_and_signature_parameters_still_count_temporary_nodes()
    {
        var compiler=new TypeScriptCompiler(new(){MaxNodeCount=16});
        Assert.Equal("NodeLimit",Assert.Throws<TypeScriptParseException>(()=>compiler.ParseScript("abstract class C {"+string.Concat(Enumerable.Repeat("abstract [key()]():number;",20))+"}")).Code);
        Assert.Equal("NodeLimit",Assert.Throws<TypeScriptParseException>(()=>compiler.ParseScript("abstract class C {abstract f("+string.Join(',',Enumerable.Range(0,20).Select(i=>$"x{i}:number"))+"):number;}")).Code);
        Assert.Equal("TypeDepthLimit",Assert.Throws<TypeScriptParseException>(()=>new TypeScriptCompiler(new(){MaxTypeDepth=8}).ParseScript("type T="+string.Concat(Enumerable.Repeat("abstract new()=>",100))+"number;")).Code);
    }

    [Theory]
    [InlineData(19240909)]
    [InlineData(7212069)]
    public void Mutated_erased_classes_keep_public_errors_and_valid_locations(int seed)
    {
        string[] sources=["abstract class C {abstract f<const T>(x:T):T; declare x:number;}",
            "abstract class C extends B {abstract override get x():number; abstract set x(v:number);}",
            "abstract class C {abstract [key()]():number; declare static value:number;}",
            "class C {declare x?:number; #x=1; f(){return this.#x;}}", "type T=abstract new<const T>(x:T)=>T;"];
        string[] fragments=["abstract","declare","readonly","static","override","class","get","set","new","#x","<",">","[",
            "]","{","}","(",")",":",";","!","?","=","=>","\n","\0","\\","'","/*","`","${"];
        var random=new Random(seed);
        for(var i=0;i<10000;i++)
        {
            var source=sources[random.Next(sources.Length)];var at=random.Next(source.Length);
            source=i%2==0?source.Insert(at,fragments[random.Next(fragments.Length)]):source.Remove(at,1);
            try {HardeningTests.AssertLocations(Compiler.ParseScript(source,"classes.ts"),source,"classes.ts");}
            catch(TypeScriptParseException error){Assert.InRange(error.Index,0,source.Length);Assert.Equal("classes.ts",error.SourceFile);}
            catch(Exception error){throw new Xunit.Sdk.XunitException($"Seed {seed}, mutation {i}: {System.Text.Json.JsonSerializer.Serialize(source)}\n{error}");}
        }
    }

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
    [InlineData("class C {declare value: number = 1;}")]
    [InlineData("abstract class C {abstract f<T>(x:T):T {return x;}}")]
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
