using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jint;
using Jint.TypeScript;

// Dependency-free release microbenchmark. Medians across nine batches after warmup;
// allocation counts cover the current thread. This is evidence for these inputs, not a universal speed claim.
var compiler = new TypeScriptCompiler();
var parserOptions = Engine.PrepareScript(string.Empty).ParserOptions!;
var results = new List<object>();
foreach (var count in new[] { 1, 100, 1000 })
{
    var js = string.Join('\n', Enumerable.Range(0, count).Select(i => $"function f{i}(x, y = 2) {{ const r = x + y; return r > 0 ? r : 0; }}"));
    var ts = string.Join('\n', Enumerable.Range(0, count).Select(i => $"function f{i}(x: number, y: number = 2): number {{ const r: number = x + y; return r > 0 ? r : 0; }}"));
    var iterations = Math.Max(10, 5000 / count);
    Measure("Official JS parse", count, js.Length, iterations, () => new Acornima.Parser(parserOptions).ParseScript(js));
    Measure("Owned JS parse", count, js.Length, iterations, () => compiler.ParseScript(js));
    Measure("Owned TS parse", count, ts.Length, iterations, () => compiler.ParseScript(ts));
    Measure("Official JS prepare", count, js.Length, iterations, () => Engine.PrepareScript(js));
    Measure("Owned TS prepare", count, ts.Length, iterations, () => compiler.PrepareScript(ts));
}
var arrowJs = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"const f{i} = (x) => x + 1;"));
var arrowTs = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"const f{i} = (x: number): number => x + 1;"));
Measure("Official JS arrows", 100, arrowJs.Length, 100, () => new Acornima.Parser(parserOptions).ParseScript(arrowJs));
Measure("Owned TS arrows", 100, arrowTs.Length, 100, () => compiler.ParseScript(arrowTs));
foreach (var count in new[] { 100, 1000 })
{
    var js = string.Join('\n', Enumerable.Range(0, count).Select(i => $"const f{i} = (x) => x < 1 ? (x) : 1;"));
    var ts = string.Join('\n', Enumerable.Range(0, count).Select(i => $"const f{i} = (x): number => x < 1 ? (x) : 1;"));
    Measure("Official JS comparisons/arrows", count, js.Length, 20, () => new Acornima.Parser(parserOptions).ParseScript(js));
    Measure("Owned TS return-only arrows", count, ts.Length, 20, () => compiler.ParseScript(ts));
}
foreach (var count in new[] { 100, 1000, 10000 })
{
    var union = "let x: " + string.Join(" | ", Enumerable.Repeat("T", count)) + " = 42;";
    var comparisons = string.Join(" < ", Enumerable.Repeat("x", count)) + ";";
    Measure("Owned TS erased union", count, union.Length, 20, () => compiler.ParseScript(union));
    Measure("Official JS comparison chain", count, comparisons.Length, 20, () => new Acornima.Parser(parserOptions).ParseScript(comparisons));
    Measure("Owned JS comparison chain", count, comparisons.Length, 20, () => compiler.ParseScript(comparisons));
    var malformed = union.Replace(" = 42;", " | = 42;", StringComparison.Ordinal);
    Measure("Owned TS late type rejection", count, malformed.Length, 20, () =>
    {
        try { compiler.ParseScript(malformed); }
        catch (TypeScriptParseException error) { return error; }
        throw new Exception("Malformed benchmark source was accepted");
    });
}
foreach (var count in new[] { 100, 1000 })
{
    var js = string.Join('\n', Enumerable.Range(0, count).Select(i => $"const r{i} = /a+[b-d]/u;"));
    Measure("Official JS regex", count, js.Length, 20, () => new Acornima.Parser(parserOptions).ParseScript(js));
    Measure("Owned JS regex", count, js.Length, 20, () => compiler.ParseScript(js));
    var unique = string.Join('\n', Enumerable.Range(0, count).Select(i => $"const r{i} = /p{i}+[b-d]/u;"));
    Measure("Official JS unique regex", count, unique.Length, 20, () => new Acornima.Parser(parserOptions).ParseScript(unique));
    Measure("Owned JS unique regex", count, unique.Length, 20, () => compiler.ParseScript(unique));
}
foreach (var count in new[] { 100, 1000, 10000 })
{
    var shape = "interface Shape {" + string.Concat(Enumerable.Repeat("readonly value?: number;", count)) + "}";
    var tuple = "type Tuple = [" + string.Join(",", Enumerable.Repeat("number", count)) + "];";
    var signatures = "type Callbacks = {" + string.Concat(Enumerable.Repeat("f(x?: number): number;", count)) + "};";
    Measure("Owned TS erased interface", count, shape.Length, 20, () => compiler.ParseScript(shape));
    Measure("Owned TS erased tuple", count, tuple.Length, 20, () => compiler.ParseScript(tuple));
    Measure("Owned TS erased signatures", count, signatures.Length, 20, () => compiler.ParseScript(signatures));
}
foreach (var count in new[] { 1, 100, 1000 })
{
    var source = "interface Input {value: number}\ntype Callback = (value: number) => number;\n" +
        string.Join('\n', Enumerable.Range(0, count).Select(i =>
            $"function f{i}(input?: Input, callback: Callback = x => x): number {{ return callback(input?.value ?? 42); }}"));
    Measure("Owned TS richer script", count, source.Length, Math.Max(10, 5000 / count), () => compiler.ParseScript(source));
}
var prepared = compiler.PrepareScript("let total: number = 0; for (let i = 0; i < 100; i++) total += i; total");
foreach (var count in new[] { 1, 100, 1000 })
{
    var signatures = string.Join('\n', Enumerable.Range(0, count).Select(i =>
        $"function f{i}(this: Context, x: unknown): x is readonly number[]; function f{i}(this: Context, x: unknown): x is readonly number[] {{return true;}}"));
    var implementations = string.Join('\n', Enumerable.Range(0, count).Select(i =>
        $"function f{i}(this: Context, x: unknown): x is readonly number[] {{return true;}}"));
    var js = string.Join('\n', Enumerable.Range(0, count).Select(i => $"function f{i}(x) {{return true;}}"));
    Measure("Official JS predicate equivalent", count, js.Length, Math.Max(10, 5000 / count), () => new Acornima.Parser(parserOptions).ParseScript(js));
    Measure("Owned TS this/predicate implementations", count, implementations.Length, Math.Max(10, 5000 / count), () => compiler.ParseScript(implementations));
    Measure("Owned TS overload implementations", count, signatures.Length, Math.Max(10, 5000 / count), () => compiler.ParseScript(signatures));
}
foreach (var count in new[] { 100, 1000, 10000 })
{
    var ambient = string.Concat(Enumerable.Repeat("declare const host: readonly (typeof value)[];", count));
    var overloads = string.Concat(Enumerable.Repeat("function f(this: Context, x: unknown): asserts x is number;", count));
    var query = "type T = " + string.Join(" | ", Enumerable.Repeat("typeof obj.value", count)) + ";";
    Measure("Owned TS ambient declarations", count, ambient.Length, 20, () => compiler.ParseScript(ambient));
    Measure("Owned TS erased overloads", count, overloads.Length, 20, () => compiler.ParseScript(overloads));
    Measure("Owned TS type query union", count, query.Length, 20, () => compiler.ParseScript(query));
}
foreach (var count in new[] { 100, 1000, 10000 })
{
    var assertions = "42" + string.Concat(Enumerable.Repeat(" as number", count)) + ";";
    var bangs = "42" + new string('!', count) + ";";
    var template = "type T = `" + string.Concat(Enumerable.Repeat("value${number}", count)) + "`;";
    var mapped = "type T = {[P in " + string.Join(" | ", Enumerable.Repeat("K", count)) + "]: Shape[P]};";
    Measure("Owned TS assertion chain", count, assertions.Length, 20, () => compiler.ParseScript(assertions));
    Measure("Owned TS non-null chain", count, bangs.Length, 20, () => compiler.ParseScript(bangs));
    Measure("Owned TS template type", count, template.Length, 20, () => compiler.ParseScript(template));
    Measure("Owned TS mapped key union", count, mapped.Length, 20, () => compiler.ParseScript(mapped));
}
foreach (var count in new[] { 1, 100, 1000 })
{
    var source = string.Join('\n', Enumerable.Range(0, count).Select(i =>
        $"function f{i}<T extends {{value: number}}>(input: T): number {{ return input!.value as number + 2; }}"));
    Measure("Owned TS generic/assert script", count, source.Length, Math.Max(10, 5000 / count), () => compiler.ParseScript(source));
}
foreach (var count in new[] { 1, 100, 1000 })
{
    var callsJs = "const f = x => x;" + string.Concat(Enumerable.Repeat("f(42);", count));
    var callsTs = "const f = <T>(x: T): T => x;" + string.Concat(Enumerable.Repeat("f<number>(42);", count));
    Measure("Official JS generic-call equivalent", count, callsJs.Length, Math.Max(20, 5000 / count), () => new Acornima.Parser(parserOptions).ParseScript(callsJs));
    Measure("Owned TS generic calls", count, callsTs.Length, Math.Max(20, 5000 / count), () => compiler.ParseScript(callsTs));
    var classesJs = string.Join('\n', Enumerable.Range(0, count).Select(i => $"class C{i} {{value = 42; f(x) {{return x;}}}}"));
    var classesTs = string.Join('\n', Enumerable.Range(0, count).Select(i => $"class C{i}<T> {{public readonly value: T = 42; f<U>(x: U): U {{return x;}}}}"));
    Measure("Official JS class equivalent", count, classesJs.Length, Math.Max(10, 3000 / count), () => new Acornima.Parser(parserOptions).ParseScript(classesJs));
    Measure("Owned TS generic classes", count, classesTs.Length, Math.Max(10, 3000 / count), () => compiler.ParseScript(classesTs));
}
foreach (var count in new[] {100, 1000, 10000})
{
    var source = "f<" + string.Join(',', Enumerable.Repeat("T", count)) + ">(42);";
    Measure("Owned TS wide type arguments", count, source.Length, 20, () => compiler.ParseScript(source));
}
Measure("Cached TS execute (new Engine)", 1, 0, 1000, () => new Engine().Evaluate(prepared));
foreach (var count in new[] {100, 1000, 10000})
{
    var conditional = string.Concat(Enumerable.Repeat("type T=X extends infer U extends string?U:never;", count));
    var imports = string.Concat(Enumerable.Repeat("type T=typeof import('missing').factory<Array<number>>;", count));
    var variance = "interface I<" + string.Join(',', Enumerable.Repeat("in out T", count)) + "> {}";
    Measure("Owned TS conditional/infer types", count, conditional.Length, 20, () => compiler.ParseScript(conditional));
    Measure("Owned TS import types", count, imports.Length, 20, () => compiler.ParseScript(imports));
    Measure("Owned TS variance parameters", count, variance.Length, 20, () => compiler.ParseScript(variance));
}
foreach (var count in new[] {1, 100, 1000})
{
    var source = string.Join('\n', Enumerable.Range(0, count).Select(i =>
        $"function f{i}<const T extends Shape>(x: T): T extends infer U ? U : never {{return x;}}"));
    Measure("Owned TS const/conditional functions", count, source.Length, Math.Max(10, 5000/count), () => compiler.ParseScript(source));
}
foreach (var count in new[] {4, 16, 48})
{
    var source = "type T=" + string.Concat(Enumerable.Repeat("infer T extends ", count)) + "number;";
    Measure("Owned TS nested infer constraints", count, source.Length, 100, () => compiler.ParseScript(source));
}
// A representative hosting workload: typed rule evaluation over host input,
// with a cached prepared script. It does not claim to be a production trace.
const string ruleTypes = """
    type Field<T> = T extends {amount: infer A extends number} ? A : never;
    type Host = import('host-types').Order;
    interface Rule<in T, out R> {evaluate(value: T): R}
    """;
const string ruleBody = """
    function discount<const T extends Host>(order: T): number {
        const amount: Field<T> = order.amount;
        return order.vip && amount >= 100 ? amount * 0.1 : 0;
    }
    discount(input);
    """;
const string ruleJs = """
    function discount(order) {
        const amount = order.amount;
        return order.vip && amount >= 100 ? amount * 0.1 : 0;
    }
    discount(input);
    """;
var ruleSource = ruleTypes + ruleBody;
var rulePrepared = compiler.PrepareScript(ruleSource, "discount.ts");
var ruleEngine = new Engine().SetValue("input", new {amount = 200, vip = true});
if (ruleEngine.Evaluate(rulePrepared).AsNumber() != 20) throw new Exception("Rule benchmark produced an incorrect discount");
Measure("Official JS rule prepare", 1, ruleJs.Length, 1000, () => Engine.PrepareScript(ruleJs));
Measure("Owned TS rule parse", 1, ruleSource.Length, 1000, () => compiler.ParseScript(ruleSource));
Measure("Owned TS rule prepare", 1, ruleSource.Length, 1000, () => compiler.PrepareScript(ruleSource));
Measure("Cached TS rule execute", 1, ruleSource.Length, 1000, () => ruleEngine.Evaluate(rulePrepared));
foreach (var count in new[] {100,1000,10000})
{
    var fields="class C {"+string.Concat(Enumerable.Repeat("declare value:number;",count))+"}";
    var abstractFields="abstract class C {"+string.Concat(Enumerable.Repeat("abstract value:number;",count))+"}";
    var methods="abstract class C {"+string.Concat(Enumerable.Repeat("abstract f(x:number):number;",count))+"}";
    var computed="class C {"+string.Concat(Enumerable.Repeat("declare [key()]:number;",count))+"}";
    Measure("Owned TS declare fields",count,fields.Length,20,()=>compiler.ParseScript(fields));
    Measure("Owned TS abstract fields",count,abstractFields.Length,20,()=>compiler.ParseScript(abstractFields));
    Measure("Owned TS abstract methods",count,methods.Length,20,()=>compiler.ParseScript(methods));
    Measure("Owned TS erased computed fields",count,computed.Length,20,()=>compiler.ParseScript(computed));
}
foreach (var count in new[] {1,100,1000})
{
    var source=string.Join('\n',Enumerable.Range(0,count).Select(i=>$"abstract class C{i}<T> {{abstract f(x:T):T; declare host:Host; value=42; g(x:number):number {{return x+this.value;}}}}"));
    Measure("Owned TS mixed abstract classes",count,source.Length,Math.Max(10,3000/count),()=>compiler.ParseScript(source));
}
foreach (var count in new[] {100, 1000, 10000})
{
    var accessors = "interface I {" + string.Concat(Enumerable.Repeat("get value():number;set value(v:number);", count)) + "}";
    var computed = "interface I {" + string.Concat(Enumerable.Repeat("[Symbol.iterator]():Iterator<number>;[key]:number;", count)) + "}";
    var bindings = "interface I {" + string.Concat(Enumerable.Repeat("f({value:[first,,...rest],...other}:Input):number;", count)) + "}";
    var functions = "type F=(" + string.Join(',', Enumerable.Repeat("{value}:Input", count)) + ")=>number;";
    Measure("Owned TS type accessors", count, accessors.Length, 20, () => compiler.ParseScript(accessors));
    Measure("Owned TS computed type members", count, computed.Length, 20, () => compiler.ParseScript(computed));
    Measure("Owned TS destructured type members", count, bindings.Length, 20, () => compiler.ParseScript(bindings));
    Measure("Owned TS destructured function types", count, functions.Length, 20, () => compiler.ParseScript(functions));
}
var report = new { utc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription,
    jint = typeof(Engine).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    acornima = typeof(Acornima.Parser).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), processorCount = Environment.ProcessorCount, results };
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

void Measure(string operation, int units, int characters, int iterations, Func<object> action)
{
    for (var i = 0; i < Math.Max(100, iterations / 2); i++) GC.KeepAlive(action());
    var timings = new double[9];
    var allocations = new long[9];
    for (var batch = 0; batch < timings.Length; batch++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) GC.KeepAlive(action());
        timings[batch] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
        allocations[batch] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations;
    }
    Array.Sort(timings);
    Array.Sort(allocations);
    var microseconds = Math.Round(timings[4], 3);
    var allocatedBytes = allocations[4];
    results.Add(new { operation, units, characters, iterations, medianMicroseconds = microseconds, allocatedBytes });
    Console.WriteLine($"{operation,-32} {units,5}: {microseconds,12:F3} us {allocatedBytes,12} B");
}
