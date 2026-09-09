using Acornima.Ast;
using Jint;
using Jint.TypeScript;
using Jint.Native.Json;

namespace Jint.TypeScript.Sample;

// Fixture for business-rule regressions against the same modules the playground edits.
internal sealed class ScriptHost
{
    private readonly string _scriptsDirectory;
    private readonly Prepared<Module> _validation;
    private readonly Prepared<Module> _webhook;
    private readonly TypeScriptModuleLoader _loader;

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
        _validation = PrepareModule("validate-order.ts");
        _webhook = PrepareModule("normalize-webhook.ts");
        _loader = new(_scriptsDirectory, compiler);

        Prepared<Module> PrepareModule(string relativePath)
        {
            var path = Path.Combine(_scriptsDirectory, relativePath);
            return compiler.PrepareModule(File.ReadAllText(path), new Uri(path).AbsoluteUri);
        }
    }

    public object? ValidateOrder(string orderJson, HostServices services)
    {
        var engine = CreateEngine(services);
        engine.Modules.Add("validation", builder => builder.AddModule(_validation));
        return engine.Invoke(engine.Modules.Import("validation").Get("run"), new JsonParser(engine).Parse(orderJson)).ToObject();
    }

    public object? NormalizeWebhook(string webhookJson, HostServices services)
    {
        var engine = CreateEngine(services);
        engine.Modules.Add("webhook", builder => builder.AddModule(_webhook));
        return engine.Invoke(engine.Modules.Import("webhook").Get("run"), new JsonParser(engine).Parse(webhookJson)).ToObject();
    }

    public object? QuoteOrder(string orderJson, HostServices services)
    {
        var engine = CreateEngine(services);
        var exports = engine.Modules.Import("./pricing/quote.ts");
        var order = new JsonParser(engine).Parse(orderJson);
        return engine.Invoke(exports.Get("quoteOrder"), order).ToObject();
    }

    private Engine CreateEngine(HostServices services) => new Engine(options =>
    {
        options.UseModules(_loader)
            .LimitStatements(50_000)
            .LimitMemory(16_000_000)
            .LimitExecutionTime(TimeSpan.FromSeconds(2));
        options.Constraints.MaxRecursionDepth = 64;
    }).SetValue("host", services);
}
