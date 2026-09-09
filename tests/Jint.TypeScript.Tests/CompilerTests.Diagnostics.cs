using Xunit;

namespace Jint.TypeScript.Tests;

public partial class CompilerTests
{
    [Theory]
    [InlineData("enum Status { Ready, Done }")]
    [InlineData("enum Status { Ready = 'ready', Done = 'done' }")]
    [InlineData("enum Status { Ready = 1 << 2, Done = Ready + 1 }")]
    [InlineData("const enum Status { Ready }")]
    [InlineData("const\r\nenum Status { Ready }")]
    [InlineData("const /* comment */ enum Status { Ready }")]
    [InlineData("declare enum Status { Ready }")]
    [InlineData("declare const enum Status { Ready }")]
    [InlineData("export enum Status { Ready }", true)]
    [InlineData("export const enum Status { Ready }", true)]
    [InlineData("export declare enum Status { Ready }", true)]
    [InlineData("export declare const enum Status { Ready }", true)]
    [InlineData("export /* comment */ enum Status { Ready }", true)]
    [InlineData("function f() { enum Status { Ready } }")]
    [InlineData("{ const enum Status { Ready } }")]
    [InlineData("switch (0) { case 0: enum Status { Ready } }")]
    [InlineData("class C { static { enum Status { Ready } } }")]
    [InlineData("const f = () => { enum Status { Ready } };")]
    [InlineData("enum 状態 { 準備 = 'ready' }")]
    [InlineData("const message = '😀'; enum Status { Ready }")]
    public void Enum_diagnostics_explain_the_limitation_and_alternative(string source, bool moduleOnly = false)
    {
        foreach (var module in moduleOnly ? new[] { true } : new[] { false, true })
        {
            var error = AssertUnsupportedDiagnostic(source, "UnsupportedEnum", "enum", module);
            Assert.Contains("does not generate or inline enum values", error.Description);
            Assert.Contains("'as const' and a union type", error.Description);
        }
    }

    [Theory]
    [InlineData("namespace Rules { export const value = 42; }", "namespace")]
    [InlineData("namespace Rules.Pricing {}", "namespace")]
    [InlineData("module Rules {}", "module")]
    [InlineData("declare namespace Rules {}", "namespace")]
    [InlineData("declare module 'host' {}", "module")]
    [InlineData("export namespace Rules {}", "namespace", true)]
    [InlineData("export declare module Rules {}", "module", true)]
    [InlineData("function f() { namespace Rules {} }", "namespace")]
    public void Namespace_diagnostics_recommend_ES_modules(string source, string token, bool moduleOnly = false)
    {
        foreach (var module in moduleOnly ? new[] { true } : new[] { false, true })
            Assert.Contains("ES module imports and exports", AssertUnsupportedDiagnostic(source, "UnsupportedNamespace", token, module).Description);
    }

    [Theory]
    [InlineData("declare class Host { run(): void; }")]
    [InlineData("declare abstract class Host { abstract run(): void; }")]
    [InlineData("export declare class Host { run(): void; }", true)]
    [InlineData("export declare abstract class Host { run(): void; }", true)]
    public void Ambient_class_diagnostics_recommend_supported_declarations(string source, bool moduleOnly = false)
    {
        foreach (var module in moduleOnly ? new[] { true } : new[] { false, true })
            Assert.Contains("interface and a 'declare const'", AssertUnsupportedDiagnostic(source, "UnsupportedAmbientClass", "class", module).Description);
    }

    [Theory]
    [InlineData("class C { constructor(public value: number) {} }", "public")]
    [InlineData("class C { constructor(private value: number) {} }", "private")]
    [InlineData("class C { constructor(protected value: number) {} }", "protected")]
    [InlineData("class C { constructor(readonly value: number) {} }", "readonly")]
    [InlineData("class C { constructor(public readonly value = 42) {} }", "public")]
    [InlineData("class C { constructor(value: number, readonly other?: number) {} }", "readonly")]
    [InlineData("class C extends Base { constructor(protected override value: number) { super(); } }", "protected")]
    [InlineData("class C { 'constructor'(public value: number) {} }", "public")]
    [InlineData("class C { constructor(public value: number); constructor(value: number) {} }", "public")]
    [InlineData("class C { constructor(readonly /*\n comment */ value: number) {} }", "readonly")]
    [InlineData("class C { constructor(label = '😀', public value: number) {} }", "public")]
    [InlineData("function f() { return class { constructor(private value: number) {} }; }", "private")]
    public void Parameter_property_diagnostics_point_to_the_modifier(string source, string token)
    {
        foreach (var module in new[] { false, true })
            Assert.Contains("'this.name = name'", AssertUnsupportedDiagnostic(source, "UnsupportedParameterProperty", token, module).Description);
    }

    [Theory]
    [InlineData("import { value } from 'values';", "import")]
    [InlineData("import type { Value } from 'values';", "import")]
    [InlineData("import 'values';", "import")]
    [InlineData("export const value: number = 42;", "export")]
    [InlineData("export interface Value { n: number }", "export")]
    public void Module_declarations_in_script_mode_explain_which_API_to_use(string source, string token)
    {
        Assert.Contains("ParseModule or PrepareModule", AssertUnsupportedDiagnostic(source, "ModuleSyntaxInScript", token, module: false).Description);
        Compiler.PrepareModule(source, "module.ts");
    }

    [Theory]
    [InlineData("const o = { enum: 42, namespace: 0 }; o.enum === 42;")]
    [InlineData("// enum Status { Ready }\nconst text = 'const enum Status { Ready }'; text.length > 0;")]
    [InlineData("const text = `namespace Rules { enum Status { Ready } }`; /enum Status/.test(text);")]
    [InlineData("let namespace = 41; namespace++; namespace === 42;")]
    [InlineData("function module(value: number) { return value; } module(42) === 42;")]
    [InlineData("namespace: { break namespace; } true;")]
    [InlineData("let declare = 0; declare\nclass C {}\nnew C() instanceof C;")]
    [InlineData("class C { constructor(readonly: number, override = 2) { this.value = readonly + override; } } new C(40).value === 42;")]
    [InlineData("class C { constructor(fn = function(readonly: number) { return readonly; }) { this.value = fn(42); } } new C().value === 42;")]
    [InlineData("class C { constructor(publicValue: number) { this.value = publicValue; } } new C(42).value === 42;")]
    [InlineData("class C { constructor() { let readonly = 42; this.value = readonly; } } new C().value === 42;")]
    public void Diagnostic_keywords_do_not_change_valid_JavaScript_or_TypeScript(string source) =>
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());

    [Theory]
    [InlineData("enum + 1;")]
    [InlineData("enum Status = {};")]
    [InlineData("const enum = 42;")]
    [InlineData("namespace('missing'); @bad")]
    [InlineData("const o = { enum: };")]
    [InlineData("class C { method(public value: number) {} }")]
    [InlineData("class C { constructor() { function f(private value: number) {} } }")]
    public void Unrelated_invalid_syntax_keeps_its_original_diagnostic(string source)
    {
        var error = Assert.Throws<TypeScriptParseException>(() => Compiler.ParseScript(source));
        Assert.DoesNotContain(error.Code, new[] { "UnsupportedEnum", "UnsupportedNamespace", "UnsupportedParameterProperty" });
    }

    [Fact]
    public void Dynamic_import_expressions_keep_script_mode_behavior() =>
        Compiler.ParseScript("import /* comment */ ('./values.ts');");

    [Fact]
    public void Enum_alternative_preserves_named_values_and_executes()
    {
        const string source = """
            const Status = { Ready: "ready", Done: "done" } as const;
            type Status = typeof Status[keyof typeof Status];
            function isDone(status: Status): boolean { return status === Status.Done; }
            isDone(Status.Done) && !isDone(Status.Ready);
            """;
        Assert.True(new Engine().Evaluate(Compiler.PrepareScript(source)).AsBoolean());
    }

    [Fact]
    public void Ordinary_const_names_do_not_pay_for_enum_lookahead()
    {
        foreach (var name in new[] { "enabled", "enumValue", "enumeration", "enum状況", "enum𐐀" })
        {
            // Match length and Unicode category: upstream's astral-identifier path itself
            // allocates more on .NET 8, independently of TypeScript diagnostic lookahead.
            var controlName = "base" + name[4..];
            var controlSource = $"const {controlName} = 42; {controlName};";
            var control = HardeningTests.Allocated(() => Compiler.ParseScript(controlSource));
            var source = $"const {name} = 42; {name};";
            var bytes = HardeningTests.Allocated(() => Compiler.ParseScript(source));
            Assert.True(bytes - control < 64, $"Enum lookahead added {bytes - control} bytes for {name}");
        }
    }

    [Fact]
    public void Unsupported_diagnostic_lookahead_keeps_source_token_and_cancellation_limits()
    {
        var small = new TypeScriptCompiler(new() { MaxTokenCount = 2 });
        Assert.Equal("TokenLimit", Assert.Throws<TypeScriptParseException>(() => small.ParseModule("export const enum Status { Ready }")).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Compiler.ParseScript("enum Status { Ready }", cancellationToken: cancellation.Token));
        Assert.Equal("SourceLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxSourceLength = 10 }).ParseScript("enum Status { Ready }")).Code);
    }

    private static TypeScriptParseException AssertUnsupportedDiagnostic(string source, string code, string token, bool module)
    {
        source = "// 😀 original source\r\n  " + source;
        var index = source.IndexOf(token, StringComparison.Ordinal);
        TypeScriptParseException? previous = null;
        foreach (var prepare in new[] { false, true })
        {
            var error = Assert.Throws<TypeScriptParseException>(() =>
            {
                if (module)
                {
                    if (prepare) Compiler.PrepareModule(source, "rules.ts");
                    else Compiler.ParseModule(source, "rules.ts");
                }
                else if (prepare) Compiler.PrepareScript(source, "rules.ts");
                else Compiler.ParseScript(source, "rules.ts");
            });
            Assert.Equal(code, error.Code);
            Assert.Equal(index, error.Index);
            Assert.Equal(source.AsSpan(0, index).Count('\n') + 1, error.Position.Line);
            Assert.Equal(index - source.LastIndexOf('\n', index) - 1, error.Position.Column);
            Assert.Equal("rules.ts", error.SourceFile);
            Assert.DoesNotContain("rules.ts", error.Description);
            if (previous is not null) Assert.Equal(previous.Description, error.Description);
            previous = error;
        }
        return previous!;
    }
}
