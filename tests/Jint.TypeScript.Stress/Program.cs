using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Jint.TypeScript;

// Isolate each adversarial input: stack exhaustion must fail this runner without
// killing a test host or hanging CI. No source is executed by Jint in these probes.
string[] cases = ["deep-types", "raised-type-limit", "deep-syntax", "raised-syntax-limit",
    "deep-arrows", "deep-preparation", "max-preparation-depth", "long-union", "long-comparisons",
    "long-identifier", "unterminated-string", "unterminated-template", "unterminated-comment", "unterminated-regex",
    "deep-objects", "deep-tuples", "deep-functions", "deep-constraints", "wide-interface", "many-type-imports",
    "deep-keyof", "deep-mapped", "deep-template-types", "long-assertions", "long-non-null", "wide-template-type",
    "deep-type-arguments", "wide-type-arguments", "deep-generic-arrows", "many-generic-calls", "comparisons-before-generic",
    "deep-readonly", "deep-predicates", "deep-queries", "deep-this", "many-overloads", "many-method-overloads", "many-ambient"];
if (args.Length == 1)
{
    Run(args[0]);
    return;
}
Console.WriteLine(RuntimeInformation.FrameworkDescription);
foreach (var name in cases)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
    // Normally launched through an apphost; also support `dotnet Stress.dll`.
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(name);
    using var child = Process.Start(start)!;
    var stdout = child.StandardOutput.ReadToEndAsync();
    var stderr = child.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try { await child.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException)
    {
        child.Kill(entireProcessTree: true);
        throw new Exception($"Stress case {name} exceeded 20 seconds");
    }
    var output = await stdout + await stderr;
    if (child.ExitCode != 0) throw new Exception($"Stress case {name} exited {child.ExitCode}: {output[..Math.Min(output.Length, 4000)]}");
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Passed {cases.Length} isolated stress cases");

static void Run(string name)
{
    var compiler = new TypeScriptCompiler();
    switch (name)
    {
        case "deep-types":
        case "raised-type-limit":
            if (name == "raised-type-limit") compiler = new(new() { MaxTypeDepth = 100000 });
            Expect("TypeDepthLimit", () => compiler.ParseScript("let value: " + string.Concat(Enumerable.Repeat("T<", 50000)) + "number;"));
            break;
        case "deep-readonly":
        case "deep-predicates":
        case "deep-queries":
        case "deep-this":
            var signaturePrefix = name switch
            {
                "deep-readonly" => "readonly ", "deep-predicates" => "() => asserts x is ",
                "deep-queries" => "typeof f<", _ => "Array<"
            };
            Expect("TypeDepthLimit", () => new TypeScriptCompiler(new() {MaxTypeDepth = 4096}).ParseScript(
                (name == "deep-this" ? "function f(this: " : "type T = ") +
                string.Concat(Enumerable.Repeat(signaturePrefix, 30000)) + "number;"));
            break;
        case "many-overloads":
            compiler.ParseScript(string.Concat(Enumerable.Repeat("function f(x: T): T;", 20000)));
            break;
        case "many-method-overloads":
            compiler.ParseScript("class C {" + string.Concat(Enumerable.Repeat("f(x: T): T;", 20000)) + "}");
            break;
        case "many-ambient":
            compiler.ParseScript(string.Concat(Enumerable.Repeat("declare const x: number;", 30000)));
            break;
        case "deep-syntax":
        case "raised-syntax-limit":
            if (name == "raised-syntax-limit") compiler = new(new() { MaxSyntaxDepth = 100000 });
            Expect("SyntaxDepthLimit", () => compiler.ParseScript(new string('(', 50000) + "0" + new string(')', 50000)));
            break;
        case "deep-arrows":
            Expect("SyntaxDepthLimit", () => compiler.ParseScript(string.Concat(Enumerable.Repeat("(x: number) => ", 10000)) + "x"));
            break;
        case "deep-preparation":
            Expect("AstDepthLimit", () => compiler.PrepareScript("x" + string.Concat(Enumerable.Repeat(".x", 30000))));
            break;
        case "max-preparation-depth":
            new TypeScriptCompiler(new() { MaxAstDepth = 256 }).PrepareScript("x" + string.Concat(Enumerable.Repeat(".x", 250)));
            break;
        case "long-union":
            compiler.ParseScript("let x: " + string.Join(" | ", Enumerable.Repeat("T", 100000)) + " = 1;");
            break;
        case "long-comparisons":
            compiler.ParseScript(string.Join(" < ", Enumerable.Repeat("x", 30000)));
            break;
        case "long-identifier":
            compiler.ParseScript(new string('x', 1_000_000));
            break;
        case "deep-type-arguments":
            Expect("TypeDepthLimit", () => new TypeScriptCompiler(new() {MaxTypeDepth = 4096}).ParseScript(
                "f<" + string.Concat(Enumerable.Repeat("T<", 30000)) + "number" + new string('>', 30001) + "(42);"));
            break;
        case "wide-type-arguments":
            compiler.ParseScript("f<" + string.Join(',', Enumerable.Repeat("T", 100000)) + ">(42);");
            break;
        case "deep-generic-arrows":
            Expect("SyntaxDepthLimit", () => compiler.ParseScript(string.Concat(Enumerable.Repeat("<T>(x: T) => ", 30000)) + "42;"));
            break;
        case "many-generic-calls":
            compiler.ParseScript(string.Concat(Enumerable.Repeat("f<T>(42);", 20000)));
            break;
        case "comparisons-before-generic":
            compiler.ParseScript(string.Join(" < ", Enumerable.Repeat("x", 30000)) + " < f<T>(42);");
            break;
        case "deep-objects":
        case "deep-tuples":
        case "deep-functions":
        case "deep-constraints":
            var prefix = name switch { "deep-objects" => "{ x: ", "deep-tuples" => "[", "deep-functions" => "() => ", _ => "<T extends " };
            Expect("TypeDepthLimit", () => new TypeScriptCompiler(new() { MaxTypeDepth = 4096 }).ParseScript(
                "type T = " + string.Concat(Enumerable.Repeat(prefix, 30000)) + "number;"));
            break;
        case "wide-interface":
            compiler.ParseScript("interface I {" + string.Concat(Enumerable.Repeat("value:number;", 50000)) + "}");
            break;
        case "many-type-imports":
            compiler.ParseModule(string.Concat(Enumerable.Repeat("import type T from 'missing';", 20000)));
            break;
        case "deep-keyof":
        case "deep-mapped":
        case "deep-template-types":
            var typePrefix = name switch { "deep-keyof" => "keyof ", "deep-mapped" => "{[P in T]:", _ => "`value${" };
            Expect("TypeDepthLimit", () => new TypeScriptCompiler(new() { MaxTypeDepth = 4096 }).ParseScript(
                "type T = " + string.Concat(Enumerable.Repeat(typePrefix, 30000)) + "number;"));
            break;
        case "long-assertions":
            compiler.PrepareScript("42" + string.Concat(Enumerable.Repeat(" as T", 100000)) + ";");
            break;
        case "long-non-null":
            compiler.PrepareScript("42" + new string('!', 100000) + ";");
            break;
        case "wide-template-type":
            compiler.ParseScript("type T = `" + string.Concat(Enumerable.Repeat("value${number}", 30000)) + "`;");
            break;
        case "unterminated-string":
            Expect(null, () => compiler.ParseScript("'" + new string('x', 999999)));
            break;
        case "unterminated-template":
            Expect(null, () => compiler.ParseScript("`" + new string('x', 999999)));
            break;
        case "unterminated-comment":
            Expect(null, () => compiler.ParseScript("/*" + new string('x', 999998)));
            break;
        case "unterminated-regex":
            Expect(null, () => compiler.ParseScript("/[" + new string('x', 999998)));
            break;
        default: throw new ArgumentException(name);
    }
}

static void Expect(string? code, Action parse)
{
    try { parse(); }
    catch (TypeScriptParseException error) when (code is null || error.Code == code) { return; }
    throw new Exception($"Expected parse error {code}");
}
