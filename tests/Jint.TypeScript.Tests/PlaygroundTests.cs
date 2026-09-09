using System.Text.Json;
using Jint.TypeScript.Sample;
using Xunit;

namespace Jint.TypeScript.Tests;

public class PlaygroundTests
{
    private static RunRequest Request(string source, string input = "{}") => new(
        new() { ["main.ts"] = source }, "main.ts", JsonSerializer.Deserialize<JsonElement>(input));

    [Fact]
    public async Task Every_catalog_example_runs_with_its_actual_files_and_input()
    {
        var runner = new PlaygroundRunner();
        var examples = ExampleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Scripts"), Path.Combine(AppContext.BaseDirectory, "Inputs"));
        foreach (var example in examples)
        {
            var result = await runner.RunAsync(new(example.Files, example.EntryFile, example.Input));
            Assert.True(result.Success, result.Diagnostic?.ToString());
            Assert.NotEmpty(result.Logs);
            if (example.Id == "pricing") Assert.Equal(10625, result.Result!.Value.GetProperty("totalCents").GetInt32());
        }
    }

    [Fact]
    public async Task Runs_async_exports_and_dynamic_imports_from_virtual_files()
    {
        var request = Request("export async function run(input: { value: number }) { const m = await import('./helper.ts'); return m.double(input.value); }", "{\"value\":21}");
        request.Files["helper.ts"] = "export function double(n: number): number { return n * 2; }";
        var result = await new PlaygroundRunner().RunAsync(request);
        Assert.True(result.Success, result.Diagnostic?.ToString());
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }

    [Theory]
    [InlineData("export function run() { while(true) {} }", "StatementsCountOverflowException")]
    [InlineData("export function run() { for(let i=0;i<100;i++) host.Log('message'); }", "InvalidOperationException")]
    [InlineData("export function run() { return 'x'.repeat(33000); }", "InvalidOperationException")]
    [InlineData("export const n = 42;", "ArgumentException")]
    [InlineData("export function run() { throw new Error('broken'); }", "JavaScriptException")]
    public async Task Returns_structured_execution_failures(string source, string code)
    {
        var result = await new PlaygroundRunner().RunAsync(Request(source));
        Assert.False(result.Success);
        Assert.Equal(code, result.Diagnostic!.Code);
    }

    [Fact]
    public async Task Reports_dependency_syntax_and_runtime_locations_without_duplicate_messages()
    {
        var request = Request("import { value } from './helper.ts'; export function run() { return value; }");
        request.Files["helper.ts"] = "\nexport const value: = 42;";
        var result = await new PlaygroundRunner().RunAsync(request);
        Assert.False(result.Success);
        Assert.Equal("helper.ts", result.Diagnostic!.File);
        Assert.Equal(2, result.Diagnostic.Line);
        Assert.Equal("Expected a supported type", result.Diagnostic.Message);
        request.Files["helper.ts"] = "export const value: number = (() => { throw new Error('boom'); })();";
        result = await new PlaygroundRunner().RunAsync(request);
        Assert.False(result.Success);
        Assert.Equal("helper.ts", result.Diagnostic!.File);
        Assert.Contains("helper.ts", result.Diagnostic.Stack);
    }

    [Fact]
    public async Task Each_request_has_fresh_globals_and_source_edits_take_effect()
    {
        var runner = new PlaygroundRunner();
        var request = Request("globalThis.count = (globalThis.count ?? 0) + 1; export function run() { return globalThis.count; }");
        Assert.Equal(1, (await runner.RunAsync(request)).Result!.Value.GetInt32());
        Assert.Equal(1, (await runner.RunAsync(request)).Result!.Value.GetInt32());
        request.Files["main.ts"] = "export function run() { return 42; }";
        Assert.Equal(42, (await runner.RunAsync(request)).Result!.Value.GetInt32());
    }

    [Fact]
    public async Task Enforces_snapshot_limits_and_rejects_missing_entry()
    {
        var runner = new PlaygroundRunner();
        var request = Request("export function run() { return 42; }");
        request.Files["unused.ts"] = new(' ', 100_001);
        var result = await runner.RunAsync(request);
        Assert.False(result.Success);
        Assert.Equal("ModuleGraphLimitException", result.Diagnostic!.Code);
        result = await runner.RunAsync(request with { EntryFile = "missing.ts" });
        Assert.Equal("ArgumentException", result.Diagnostic!.Code);
    }

    [Fact]
    public async Task Cancels_waiting_promises_and_releases_the_execution_slot()
    {
        var runner = new PlaygroundRunner();
        using var cancellation = new CancellationTokenSource();
        var pending = runner.RunAsync(Request("export async function run() { return await new Promise(() => {}); }"), cancellation.Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var result = await pending;
        Assert.False(result.Success);
        Assert.True(result.Diagnostic!.Code.Contains("Cancel", StringComparison.Ordinal));
        Assert.True((await runner.RunAsync(Request("export function run() { return 42; }"))).Success);
    }

    [Fact]
    public async Task Rejects_excess_concurrency_without_queuing_more_work()
    {
        var runner = new PlaygroundRunner();
        using var cancellation = new CancellationTokenSource();
        var request = Request("export async function run() { return await new Promise(() => {}); }");
        var first = runner.RunAsync(request, cancellation.Token);
        var second = runner.RunAsync(request, cancellation.Token);
        var busy = await runner.RunAsync(request);
        Assert.Equal("Busy", busy.Diagnostic!.Code);
        cancellation.Cancel();
        await Task.WhenAll(first, second);
        Assert.True((await runner.RunAsync(Request("export function run() { return 42; }"))).Success);
    }
}
