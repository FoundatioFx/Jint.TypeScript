using System.Text.Json;
using System.Text.Json.Nodes;
using Jint.Runtime;

namespace Jint.TypeScript.Sample;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        var command = args.FirstOrDefault() ?? "all";
        if (command is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }
        if (args.Length > 2 || command is not ("all" or "validation" or "webhook" or "pricing" or "errors")
            || (args.Length == 2 && command is "all" or "errors"))
        {
            PrintUsage();
            return 2;
        }

        try
        {
            var scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
            var host = new ScriptHost(scripts);
            var services = new HostServices("sample-request-001", message => Console.WriteLine($"  C# host: {message}"));
            var customInput = args.Length == 2 ? File.ReadAllText(args[1]) : null;

            if (command is "all" or "validation")
            {
                Console.WriteLine("ORDER VALIDATION — one prepared script, separate input orders");
                Print(host.ValidateOrder(customInput ?? ReadInput("order-gold.json"), services));
                if (customInput is null) Print(host.ValidateOrder(ReadInput("order-invalid.json"), services));
            }

            if (command is "all" or "webhook")
            {
                Console.WriteLine("WEBHOOK NORMALIZATION — validate an unknown payload and select public metadata");
                Print(host.NormalizeWebhook(customInput ?? ReadInput("ticket-created.json"), services));
            }

            if (command is "all" or "pricing")
            {
                Console.WriteLine("PRICING MODULES — relative imports, discount policies and shipping");
                Print(host.QuoteOrder(customInput ?? ReadInput("order-gold.json"), services));
                if (customInput is null) Print(host.QuoteOrder(ReadInput("order-standard.json"), services));
            }

            if (command is "all" or "errors")
            {
                Console.WriteLine("ORIGINAL TYPESCRIPT ERRORS — an unsupported shipping destination");
                var order = JsonNode.Parse(ReadInput("order-gold.json"))!;
                order["destination"] = "XX";
                try
                {
                    host.QuoteOrder(order.ToJsonString(), services);
                    Console.Error.WriteLine("Expected the pricing rule to reject destination XX.");
                    return 1;
                }
                catch (JavaScriptException error)
                {
                    PrintScriptError(error);
                }
            }

            return 0;
        }
        catch (TypeScriptParseException error)
        {
            Console.Error.WriteLine($"{error.SourceFile}:{error.Position.Line}:{error.Position.Column + 1}: {error.Message}");
        }
        catch (JavaScriptException error)
        {
            PrintScriptError(error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or StatementsCountOverflowException or MemoryLimitExceededException or RecursionDepthOverflowException or TimeoutException)
        {
            Console.Error.WriteLine(error.Message);
        }
        return 1;
    }

    private static string ReadInput(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Inputs", name));

    private static void Print(object? result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        Console.WriteLine();
    }

    private static void PrintScriptError(JavaScriptException error)
    {
        var location = error.Location;
        var file = location.SourceFile;
        if (string.IsNullOrEmpty(file))
        {
            Console.Error.WriteLine(error.Message);
            return;
        }
        if (Uri.TryCreate(file, UriKind.Absolute, out var uri) && uri.IsFile) file = uri.LocalPath;
        Console.Error.WriteLine($"{file}:{location.Start.Line}:{location.Start.Column + 1}: {error.Message}");
    }

    private static void PrintUsage() => Console.WriteLine("""
        Usage: Jint.TypeScript.Sample [all|validation|webhook|pricing|errors] [input.json]
        No arguments runs every example. A custom JSON input is accepted by
        validation, webhook and pricing. TypeScript files are loaded from Scripts
        beside the executable; their prepared code is reused for each input.
        """);
}
