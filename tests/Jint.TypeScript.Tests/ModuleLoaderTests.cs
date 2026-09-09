using Jint.Runtime.Modules;
using Xunit;

namespace Jint.TypeScript.Tests;

public class ModuleLoaderTests
{
    private static object? Import(TypeScriptModuleLoader loader, string entry = "./main.ts", string export = "answer") =>
        new Engine(options => options.UseModules(loader)).Modules.Import(entry).Get(export).ToObject();

    [Fact]
    public void Loads_nested_imports_shared_dependencies_and_erases_type_only_dependencies()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = "import type { Missing } from './missing.ts'; import { a } from './lib/a.ts'; import { b } from './lib/b.ts'; export const answer: number = a + b;",
            ["lib/a.ts"] = "import { n } from '../value.ts'; export const a: number = n;",
            ["lib/b.ts"] = "import { n } from '../value.ts'; export const b: number = n;",
            ["value.ts"] = "globalThis.loads = (globalThis.loads ?? 0) + 1; export const n: number = 21 / globalThis.loads;"
        };
        var loader = new TypeScriptModuleLoader(files);
        files["value.ts"] = "invalid changed source";
        Parallel.For(0, 12, _ => Assert.Equal(42d, Import(loader)));
    }

    [Fact]
    public void Supports_cycles_reexports_and_inline_type_import_side_effects()
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string>
        {
            ["main.ts"] = "import { type T } from './side.ts'; import { get } from './cycle.ts'; export const answer: number = get() + globalThis.side; export function base(): number { return 40; }",
            ["cycle.ts"] = "import { base } from './main.ts'; export function get(): number { return base(); }",
            ["side.ts"] = "export type T = string; globalThis.side = 2;"
        });
        Assert.Equal(42d, Import(loader));
    }

    [Theory]
    [InlineData("main.ts")]
    [InlineData("main.mts")]
    [InlineData("main.js")]
    [InlineData("main.mjs")]
    [InlineData("folder with # café/main.ts")]
    public void Supports_documented_extensions_and_encoded_names(string file)
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string> { [file] = "export const answer = 42;" });
        Assert.Equal(42d, Import(loader, "./" + string.Join('/', file.Split('/').Select(Uri.EscapeDataString))));
    }

    [Theory]
    [InlineData("../outside.ts")]
    [InlineData("/absolute.ts")]
    [InlineData("a//b.ts")]
    [InlineData("a/./b.ts")]
    [InlineData("a/../b.ts")]
    [InlineData("a\\b.ts")]
    [InlineData("file:///x.ts")]
    [InlineData("bad\nname.ts")]
    public void Rejects_invalid_virtual_names(string file) => Assert.Throws<ArgumentException>(() =>
        new TypeScriptModuleLoader(new Dictionary<string, string> { [file] = "" }));

    [Theory]
    [InlineData("./host.d.ts")]
    [InlineData("./host.d.mts")]
    [InlineData("./data.json")]
    public void Gives_guidance_for_unsupported_module_requests(string specifier)
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string>());
        Assert.Throws<NotSupportedException>(() => loader.Resolve(null, new(specifier, [])));
    }

    [Fact]
    public void Supports_registered_host_modules_and_rejects_unregistered_packages()
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string>
        { ["main.ts"] = "import { value } from 'host'; export const answer: number = value + 2;" });
        var engine = new Engine(options => options.UseModules(loader));
        engine.Modules.Add("host", builder => builder.ExportValue("value", 40));
        Assert.Equal(42d, engine.Modules.Import("./main.ts").Get("answer").ToObject());
        Assert.Throws<NotSupportedException>(() => loader.LoadModule(engine, loader.Resolve(null, new("some-package", []))));
    }

    [Theory]
    [InlineData("../outside.ts")]
    [InlineData("https://example.com/remote.ts")]
    [InlineData("file:///outside.ts")]
    public void Restricts_resolution_to_the_configured_root(string specifier)
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string>());
        Assert.ThrowsAny<Exception>(() => loader.Resolve(null, new(specifier, [])));
    }

    [Fact]
    public void Rejects_attributes_and_preserves_missing_file_identity()
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string>());
        Assert.Throws<NotSupportedException>(() => loader.Resolve(null, new("./main.ts", [new("type", "json")])));
        var resolved = loader.Resolve(null, new("./missing.ts", []));
        var error = Assert.Throws<FileNotFoundException>(() => loader.LoadModule(new Engine(), resolved));
        Assert.Contains("missing.ts", error.Message);
    }

    [Fact]
    public void Preserves_original_dependency_parse_diagnostics()
    {
        var loader = new TypeScriptModuleLoader(new Dictionary<string, string> { ["broken.ts"] = "\nexport const n: = 1;" });
        var resolved = loader.Resolve(null, new("./broken.ts", []));
        var error = Assert.Throws<TypeScriptParseException>(() => loader.LoadModule(new Engine(), resolved));
        Assert.Equal(2, error.Position.Line);
        Assert.EndsWith("broken.ts", error.SourceFile);
        Assert.Equal("Expected a supported type", error.Description);
        Assert.Equal(1, error.Message.Split(error.Description).Length - 1);
    }

    [Fact]
    public void Bounds_the_entire_virtual_snapshot_including_unused_declarations()
    {
        Assert.Throws<ModuleGraphLimitException>(() => new TypeScriptModuleLoader(
            new Dictionary<string, string> { ["a.ts"] = "", ["host.d.ts"] = "" }, options: new() { MaxModuleCount = 1 }));
        Assert.Throws<ModuleGraphLimitException>(() => new TypeScriptModuleLoader(
            new Dictionary<string, string> { ["a.ts"] = "123", ["b.ts"] = "456" }, options: new() { MaxTotalSourceLength = 5 }));
        Assert.Throws<ModuleGraphLimitException>(() => new TypeScriptModuleLoader(
            new Dictionary<string, string> { ["a.ts"] = "1234" }, new(new() { MaxSourceLength = 3 })));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeScriptModuleLoader(
            new Dictionary<string, string>(), options: new() { MaxTotalSourceLength = 0 }));
    }

    [Fact]
    public void Cancellation_applies_to_construction_loading_and_cached_results()
    {
        using var cancellation = new CancellationTokenSource();
        var files = new Dictionary<string, string> { ["main.ts"] = "export const answer = 42;" };
        var loader = new TypeScriptModuleLoader(files, cancellationToken: cancellation.Token);
        Assert.Equal(42d, Import(loader));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => loader.Resolve(null, new("./main.ts", [])));
        Assert.Throws<OperationCanceledException>(() => new TypeScriptModuleLoader(files, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Directory_loader_reuses_prepared_code_and_recreation_observes_edits()
    {
        var directory = Directory.CreateTempSubdirectory("loader # café ").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "main.ts"), "import { value } from './helper.ts'; export const answer: number = value + 2;");
            File.WriteAllText(Path.Combine(directory, "helper.ts"), "export const value: number = 40;");
            var loader = new TypeScriptModuleLoader(directory);
            Assert.Equal(42d, Import(loader));
            File.WriteAllText(Path.Combine(directory, "helper.ts"), "export const value: number = 50;");
            Assert.Equal(42d, Import(loader));
            Assert.Equal(52d, Import(new(directory)));
            File.Delete(Path.Combine(directory, "main.ts"));
            File.Delete(Path.Combine(directory, "helper.ts"));
            Assert.Equal(42d, Import(loader));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Directory_limits_bound_reads_and_count_cached_sources_across_engines()
    {
        var directory = Directory.CreateTempSubdirectory("loader-limits-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "a.ts"), "export const answer = 42;");
            File.WriteAllText(Path.Combine(directory, "b.ts"), "export const answer = 42;");
            var loader = new TypeScriptModuleLoader(directory, options: new() { MaxTotalSourceLength = 30 });
            Assert.Equal(42d, Import(loader, "./a.ts"));
            var b = loader.Resolve(null, new("./b.ts", []));
            Assert.Throws<ModuleGraphLimitException>(() => loader.LoadModule(new Engine(), b));
            loader = new(directory, options: new() { MaxModuleCount = 1 });
            Assert.Equal(42d, Import(loader, "./a.ts"));
            Assert.Throws<ModuleGraphLimitException>(() => loader.LoadModule(new Engine(), b));
            loader = new(directory, new(new() { MaxSourceLength = 5 }));
            Assert.Throws<ModuleGraphLimitException>(() => loader.LoadModule(new Engine(), b));
        }
        finally { Directory.Delete(directory, true); }
    }
}
