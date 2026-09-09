using Acornima.Ast;
using Jint.Native.Json;

namespace Jint.TypeScript.Sample;

/// <summary>A small host for these examples, with prepared code shared across executions.</summary>
public sealed class ScriptHost
{
    private readonly string _scriptsDirectory;
    private readonly Prepared<Script> _validation;
    private readonly Prepared<Script> _webhook;
    private readonly (string Key, Prepared<Module> Code)[] _modules;
    private readonly string _quoteModuleKey;

    public ScriptHost(string scriptsDirectory)
    {
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
        var compiler = new TypeScriptCompiler(new()
        {
            MaxSourceLength = 100_000,
            MaxNodeCount = 10_000,
            MaxTokenCount = 50_000
        });

        // File I/O and TypeScript preparation happen once, when the host starts.
        _validation = PrepareScript("validate-order.ts");
        _webhook = PrepareScript("normalize-webhook.ts");
        _modules = [PrepareModule("pricing/money.ts"), PrepareModule("pricing/discounts.ts"), PrepareModule("pricing/quote.ts")];
        _quoteModuleKey = _modules[^1].Key;

        Prepared<Script> PrepareScript(string relativePath)
        {
            var path = Path.Combine(_scriptsDirectory, relativePath);
            return compiler.PrepareScript(File.ReadAllText(path), path);
        }

        (string, Prepared<Module>) PrepareModule(string relativePath)
        {
            var path = Path.Combine(_scriptsDirectory, relativePath);
            // Jint resolves relative imports against this URI. Preparation and
            // registration must use the same resolved key, including the .ts suffix.
            var key = new Uri(path).AbsoluteUri;
            return (key, compiler.PrepareModule(File.ReadAllText(path), key));
        }
    }

    public object? ValidateOrder(string orderJson, HostServices services)
    {
        var engine = CreateEngine(services);
        engine.SetValue("order", new JsonParser(engine).Parse(orderJson));
        return engine.Evaluate(_validation).ToObject();
    }

    public object? NormalizeWebhook(string webhookJson, HostServices services)
    {
        var engine = CreateEngine(services);
        engine.SetValue("incomingWebhook", new JsonParser(engine).Parse(webhookJson));
        return engine.Evaluate(_webhook).ToObject();
    }

    public object? QuoteOrder(string orderJson, HostServices services)
    {
        var engine = CreateEngine(services);
        foreach (var (key, code) in _modules)
            engine.Modules.Add(key, builder => builder.AddModule(code));

        var exports = engine.Modules.Import(_quoteModuleKey);
        var order = new JsonParser(engine).Parse(orderJson);
        return engine.Invoke(exports.Get("quoteOrder"), order).ToObject();
    }

    private Engine CreateEngine(HostServices services) => new Engine(options =>
    {
        options.UseModules(_scriptsDirectory)
            .LimitStatements(50_000)
            .LimitMemory(16_000_000)
            .LimitExecutionTime(TimeSpan.FromSeconds(2));
        options.Constraints.MaxRecursionDepth = 64;
    }).SetValue("host", services);
}
