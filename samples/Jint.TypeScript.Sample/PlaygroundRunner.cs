using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint.Native.Json;
using Jint.Runtime;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Jint.TypeScript.Sample;

public sealed record RunRequest(Dictionary<string, string> Files, string EntryFile, JsonElement Input);
public sealed record ScriptDiagnostic(string Code, string Message, string? File = null, int? Line = null,
    int? Column = null, string? Stack = null);
public sealed record RunResponse(bool Success, JsonElement? Result, IReadOnlyList<string> Logs,
    ScriptDiagnostic? Diagnostic, double ElapsedMilliseconds);

/// <summary>Runs a bounded editor snapshot with a fresh engine and virtual module loader for each request.</summary>
public sealed class PlaygroundRunner
{
    private static readonly TypeScriptCompiler Compiler = new(new()
    {
        MaxSourceLength = 100_000, MaxNodeCount = 10_000, MaxTokenCount = 50_000
    });
    private readonly SemaphoreSlim _executionSlots = new(2);

    public async Task<RunResponse> RunAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _executionSlots.WaitAsync(0, cancellationToken))
            return new(false, null, [], new("Busy", "Two scripts are already running. Try again shortly."), 0);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            return await Task.Run(() => ExecuteAsync(request, deadline.Token), CancellationToken.None);
        }
        finally { _executionSlots.Release(); }
    }

    private static async Task<RunResponse> ExecuteAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var logs = new List<string>();
        try
        {
            ArgumentNullException.ThrowIfNull(request.Files);
            if (!request.Files.ContainsKey(request.EntryFile ?? ""))
                throw new ArgumentException("Choose an entry file that exists in the editor.");
            var loader = new TypeScriptModuleLoader(request.Files, Compiler,
                new() { MaxModuleCount = 32, MaxTotalSourceLength = 200_000 }, cancellationToken);
            var engine = new Engine(options =>
            {
                options.UseModules(loader).LimitStatements(50_000).LimitMemory(16_000_000)
                    .LimitExecutionTime(TimeSpan.FromSeconds(2)).ObserveCancellation(cancellationToken);
                options.Constraints.MaxRecursionDepth = 64;
                options.Modules.MaxModuleCount = 32;
            });
            var services = engine.Evaluate("({})").AsObject();
            services.Set("RequestId", "playground-request");
            services.Set("ReceivedAt", DateTimeOffset.UtcNow.ToString("O"));
            engine.SetValue("host", services);
            engine.SetValue("__playgroundLog", new Action<string>(message =>
            {
                if (logs.Count >= 64) throw new InvalidOperationException("Log limit exceeded (64 messages).");
                logs.Add(message.Length > 1000 ? message[..1000] + "…" : message);
            }));
            engine.Execute("host.Log = __playgroundLog; delete globalThis.__playgroundLog;");
            var entry = "./" + string.Join('/', request.EntryFile!.Split('/').Select(Uri.EscapeDataString));
            var exports = await engine.Modules.ImportAsync(entry, cancellationToken);
            if (!exports.Get("run").IsCallable())
                throw new ArgumentException("The entry file must export a function named run(input).");
            engine.SetValue("__playgroundRun", exports.Get("run"));
            engine.SetValue("__playgroundInput", new JsonParser(engine).Parse(
                request.Input.ValueKind == JsonValueKind.Undefined ? "null" : request.Input.GetRawText()));
            // This fixed JS adapter awaits both sync and async exports. User TypeScript is parsed natively by the loader.
            var output = await engine.EvaluateAsync("(async () => JSON.stringify(await __playgroundRun(__playgroundInput)))()",
                cancellationToken: cancellationToken);
            var json = output.IsUndefined() ? "null" : output.AsString();
            if (json.Length > 32_000) throw new InvalidOperationException("Result exceeds the 32,000-character display limit.");
            return new(true, JsonSerializer.Deserialize<JsonElement>(json), logs, null, Elapsed());
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(false, null, logs, Describe(error), Elapsed());
        }
        double Elapsed() => Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
    }

    public static ScriptDiagnostic Describe(Exception error)
    {
        if (JintException.TryGetClrException(error, out var cause)) error = cause;
        if (error is TypeScriptParseException parse)
            return new(parse.Code, parse.Description, DisplayPath(parse.SourceFile), parse.Position.Line, parse.Position.Column + 1);
        if (error is PromiseRejectedException rejected) error = new JavaScriptException(rejected.RejectedValue);
        JintException.TryGetJavaScriptCallStack(error, out var stack);
        if (JintException.TryGetJavaScriptLocation(error, out var location))
            return new(error.GetType().Name, error.Message, DisplayPath(location.SourceFile),
                location.Start.Line, location.Start.Column + 1, stack);
        // A rejected promise retains its error stack, but Jint's rejection exception has no typed location.
        // Only recognize our own virtual file frames, leaving arbitrary stack text as display text.
        if (stack is not null)
        {
            var frame = Regex.Match(stack, @"(file:///__jint_typescript__/[^\r\n)]+):(\d+):(\d+)",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (frame.Success && int.TryParse(frame.Groups[2].Value, out var line) && int.TryParse(frame.Groups[3].Value, out var column))
                return new(error.GetType().Name, error.Message, DisplayPath(frame.Groups[1].Value), line, column, stack);
        }
        return new(error.GetType().Name, error.Message, Stack: stack);
    }

    private static string? DisplayPath(string? source) => source?.StartsWith("file:///__jint_typescript__/", StringComparison.Ordinal) == true
        ? Uri.UnescapeDataString(source["file:///__jint_typescript__/".Length..]) : source;
}
