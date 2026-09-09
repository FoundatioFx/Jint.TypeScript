using System.Text.Json;

namespace Jint.TypeScript.Sample;

public sealed record PlaygroundExample(string Id, string Name, string Description, string EntryFile,
    Dictionary<string, string> Files, JsonElement Input);

public static class ExampleCatalog
{
    public static IReadOnlyList<PlaygroundExample> Load(string scripts, string inputs)
    {
        var globals = File.ReadAllText(Path.Combine(scripts, "host.d.ts"));
        Dictionary<string, string> Files(params string[] paths) => paths.ToDictionary(path => path,
            path => File.ReadAllText(Path.Combine(scripts, path)), StringComparer.Ordinal);
        PlaygroundExample Example(string id, string name, string description, string entry,
            Dictionary<string, string> files, string input)
        {
            files["host.d.ts"] = globals;
            return new(id, name, description, entry, files,
                JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(inputs, input))));
        }
        return
        [
            Example("pricing", "Order pricing", "Import policies and helpers to quote an order in cents.", "main.ts",
                Files("main.ts", "pricing/quote.ts", "pricing/money.ts", "pricing/discounts.ts", "pricing/contracts.ts"), "order-gold.json"),
            Example("validation", "Order validation", "Return field errors and call the C# logging callback.", "validate-order.ts",
                Files("validate-order.ts"), "order-invalid.json"),
            Example("webhook", "Webhook normalization", "Normalize a webhook using an as const alternative to enums.", "normalize-webhook.ts",
                Files("normalize-webhook.ts"), "ticket-created.json"),
            Example("migration", "JSDoc → TypeScript", "Explore JSDoc IntelliSense, then convert a file to TypeScript with one click.", "migration/main.js",
                Files("migration/main.js", "migration/quote.js", "migration/subtotal.js"), "migration-cart.json"),
            new("imports", "Start with imports", "Add a file, export a function, and import it from main.ts.", "main.ts", new()
            {
                ["main.ts"] = "import { greet } from './greet.ts';\n\ninterface Input { name: string; }\n\nexport function run(input: Input) {\n    host.Log(`Greeting ${input.name}`);\n    return { message: greet(input.name) };\n}\n",
                ["greet.ts"] = "export function greet(name: string): string {\n    return `Hello, ${name}!`;\n}\n",
                ["host.d.ts"] = globals
            }, JsonSerializer.Deserialize<JsonElement>("{\"name\":\"world\"}"))
        ];
    }
}
