using System.Text.Json;
using System.Text.Json.Nodes;
using Jint.Runtime;
using Jint.TypeScript.Sample;
using Xunit;

namespace Jint.TypeScript.Tests;

public class SampleTests
{
    private static readonly string Scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
    private static readonly ScriptHost Host = new(Scripts);
    private static HostServices Services => new("test-request", _ => { });

    [Theory]
    [InlineData("order-gold.json")]
    [InlineData("order-standard.json")]
    public void Validates_real_orders_and_calls_the_CSharp_host(string input)
    {
        var messages = new List<string>();
        var result = Json(Host.ValidateOrder(Input(input), new("request", messages.Add)));
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
        Assert.Single(messages);
        Assert.Contains(result.GetProperty("orderId").GetString()!, messages[0]);
    }

    [Fact]
    public void Returns_all_validation_issues_for_the_invalid_fixture()
    {
        var result = Json(Host.ValidateOrder(Input("order-invalid.json"), Services));
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(["customer.email", "lines[0].quantity", "lines[0].unitPriceCents"],
            result.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("field").GetString()));
    }

    [Theory]
    [InlineData("quantity", 0)]
    [InlineData("quantity", 101)]
    [InlineData("quantity", 1.5)]
    [InlineData("unitPriceCents", -1)]
    [InlineData("unitPriceCents", 1.5)]
    [InlineData("unitPriceCents", 9007199254740992d)]
    public void Both_validation_and_pricing_reject_invalid_line_values(string field, double value)
    {
        var order = Order();
        order["lines"]![0]![field] = value;
        Assert.False(Json(Host.ValidateOrder(order.ToJsonString(), Services)).GetProperty("valid").GetBoolean());
        Assert.Throws<JavaScriptException>(() => Host.QuoteOrder(order.ToJsonString(), Services));
    }

    [Fact]
    public void Rejects_empty_orders_and_checks_accumulated_amounts()
    {
        var order = Order();
        order["id"] = "";
        order["lines"] = new JsonArray();
        var result = Json(Host.ValidateOrder(order.ToJsonString(), Services));
        Assert.Equal(2, result.GetProperty("issues").GetArrayLength());
        Assert.Throws<JavaScriptException>(() => Host.QuoteOrder(order.ToJsonString(), Services));

        order = Order();
        order["lines"]![0]!["unitPriceCents"] = 9007199254740991L;
        Assert.Throws<JavaScriptException>(() => Host.QuoteOrder(order.ToJsonString(), Services));
    }

    [Theory]
    [InlineData("gold", "SAVE15", "Coupon", 1875)]
    [InlineData("gold", "WELCOME5", "Gold customer", 1250)]
    [InlineData("gold", null, "Gold customer", 1250)]
    [InlineData("standard", " welcome5 ", "Coupon", 625)]
    [InlineData("standard", "unknown", "None", 0)]
    [InlineData("standard", null, "None", 0)]
    public void Selects_the_best_discount_without_stacking(string tier, string? coupon, string name, int cents)
    {
        var order = Order();
        order["customer"]!["tier"] = tier;
        order["coupon"] = coupon;
        var quote = Json(Host.QuoteOrder(order.ToJsonString(), Services));
        Assert.Equal(12500, quote.GetProperty("subtotalCents").GetDouble());
        Assert.Equal(name, quote.GetProperty("discount").GetProperty("name").GetString());
        Assert.Equal(cents, quote.GetProperty("discount").GetProperty("amountCents").GetDouble());
        Assert.Equal(12500 - cents, quote.GetProperty("totalCents").GetDouble());
    }

    [Theory]
    [InlineData(9999, "standard", 595)]
    [InlineData(10000, "standard", 0)]
    [InlineData(10000, "gold", 595)]
    public void Free_shipping_uses_the_discounted_subtotal(int price, string tier, int shipping)
    {
        var order = Order();
        order["coupon"] = null;
        order["customer"]!["tier"] = tier;
        order["lines"] = new JsonArray(new JsonObject { ["sku"] = "ONE", ["quantity"] = 1, ["unitPriceCents"] = price });
        var quote = Json(Host.QuoteOrder(order.ToJsonString(), Services));
        Assert.Equal(shipping, quote.GetProperty("shippingCents").GetDouble());
    }

    [Theory]
    [InlineData("US", 0, 10625)]
    [InlineData("CA", 1295, 11920)]
    [InlineData("GB", 1995, 12620)]
    public void Prices_supported_shipping_destinations(string destination, int shipping, int total)
    {
        var order = Order();
        order["destination"] = destination;
        var quote = Json(Host.QuoteOrder(order.ToJsonString(), Services));
        Assert.Equal(shipping, quote.GetProperty("shippingCents").GetDouble());
        Assert.Equal(total, quote.GetProperty("totalCents").GetDouble());
    }

    [Theory]
    [InlineData(101, 15, 681)]
    [InlineData(110, 17, 688)]
    public void Rounds_discount_amounts_to_whole_cents(int price, int discount, int total)
    {
        var order = Order();
        order["lines"] = new JsonArray(new JsonObject { ["sku"] = "ONE", ["quantity"] = 1, ["unitPriceCents"] = price });
        var quote = Json(Host.QuoteOrder(order.ToJsonString(), Services));
        Assert.Equal(discount, quote.GetProperty("discount").GetProperty("amountCents").GetDouble());
        Assert.Equal(total, quote.GetProperty("totalCents").GetDouble());
    }

    [Fact]
    public void Normalizes_an_unknown_webhook_without_forwarding_private_metadata()
    {
        var services = Services;
        var result = Json(Host.NormalizeWebhook(Input("ticket-created.json"), services));
        Assert.Equal("evt-2048", result.GetProperty("externalId").GetString());
        Assert.Equal("Checkout unavailable", result.GetProperty("title").GetString());
        Assert.Equal("high", result.GetProperty("severity").GetString());
        Assert.Equal(services.RequestId, result.GetProperty("requestId").GetString());
        Assert.Equal(services.ReceivedAt, result.GetProperty("receivedAt").GetString());
        Assert.Equal(["environment", "service", "region"], result.GetProperty("tags").EnumerateObject().Select(tag => tag.Name));
        Assert.DoesNotContain("alex@example.com", result.GetRawText());
        Assert.DoesNotContain("demo-value-that-must-not-be-forwarded", result.GetRawText());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"type\":\"unknown\",\"id\":\"event\"}")]
    [InlineData("{\"type\":\"ticket.created\",\"id\":1}")]
    [InlineData("{\"type\":\"ticket.created\",\"id\":\"event\",\"data\":[]}")]
    [InlineData("{\"type\":\"ticket.created\",\"id\":\"event\",\"data\":{\"subject\":42,\"priority\":\"urgent\"}}")]
    public void Rejects_malformed_webhooks_with_a_public_runtime_error(string json) =>
        Assert.Throws<JavaScriptException>(() => Host.NormalizeWebhook(json, Services));

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"region\":42,\"service\":\"api\",\"private\":\"secret\"}")]
    public void Optional_metadata_and_normal_priority_are_handled(string metadata)
    {
        var input = JsonNode.Parse(Input("ticket-created.json"))!;
        input["metadata"] = JsonNode.Parse(metadata);
        input["data"]!["priority"] = "low";
        var result = Json(Host.NormalizeWebhook(input.ToJsonString(), Services));
        Assert.Equal("normal", result.GetProperty("severity").GetString());
        Assert.False(result.GetProperty("tags").TryGetProperty("private", out _));
        Assert.False(result.GetProperty("tags").TryGetProperty("region", out _));
    }

    [Fact]
    public void Module_errors_point_to_the_original_TypeScript_file()
    {
        var order = Order();
        order["destination"] = "XX";
        var error = Assert.Throws<JavaScriptException>(() => Host.QuoteOrder(order.ToJsonString(), Services));
        var path = Path.Combine(Scripts, "pricing", "quote.ts");
        Assert.Equal(path, new Uri(error.Location.SourceFile!).LocalPath);
        var line = File.ReadAllLines(path)[error.Location.Start.Line - 1];
        Assert.Contains("throw new Error(`Unsupported destination:", line);
        Assert.Contains("Unsupported destination: XX", error.Message);
    }

    [Fact]
    public void Prepared_code_can_be_reused_concurrently_with_independent_engines()
    {
        var input = Input("ticket-created.json");
        Parallel.For(0, 16, i =>
        {
            var result = Json(Host.NormalizeWebhook(input, new($"request-{i}", _ => { })));
            Assert.Equal($"request-{i}", result.GetProperty("requestId").GetString());
            Assert.Equal(10625, Json(Host.QuoteOrder(Input("order-gold.json"), Services)).GetProperty("totalCents").GetDouble());
        });
    }

    [Fact]
    public void Loads_real_files_once_and_resolves_modules_from_paths_with_spaces_and_Unicode()
    {
        WithScriptCopy(directory =>
        {
            // Neither editor globals nor an erased type-only import is needed at runtime.
            File.Delete(Path.Combine(directory, "host.d.ts"));
            File.Delete(Path.Combine(directory, "pricing", "contracts.ts"));
            var host = new ScriptHost(directory);
            Assert.Equal(10625, Json(host.QuoteOrder(Input("order-gold.json"), Services)).GetProperty("totalCents").GetDouble());

            File.WriteAllText(Path.Combine(directory, "validate-order.ts"), "42;");
            Assert.True(Json(host.ValidateOrder(Input("order-gold.json"), Services)).GetProperty("valid").GetBoolean());
            Assert.Equal(42d, new ScriptHost(directory).ValidateOrder(Input("order-gold.json"), Services));
        });
    }

    [Fact]
    public void Each_execution_has_fresh_globals_and_execution_limits()
    {
        WithScriptCopy(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "validate-order.ts"),
                "globalThis.runs = (globalThis.runs ?? 0) + 1; globalThis.runs;");
            var host = new ScriptHost(directory);
            Assert.Equal(1d, host.ValidateOrder("{}", Services));
            Assert.Equal(1d, host.ValidateOrder("{}", Services));

            File.WriteAllText(Path.Combine(directory, "validate-order.ts"), "while (true) {}");
            host = new ScriptHost(directory);
            Assert.Throws<StatementsCountOverflowException>(() => host.ValidateOrder("{}", Services));
        });
    }

    private static string Input(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Inputs", name));
    private static JsonNode Order() => JsonNode.Parse(Input("order-gold.json"))!;
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    private static void WithScriptCopy(Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory("jint sample # café-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Scripts, "*.ts", SearchOption.AllDirectories))
            {
                var target = Path.Combine(directory, Path.GetRelativePath(Scripts, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            action(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
