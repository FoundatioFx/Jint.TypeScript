# TypeScript hosting sample

A console app that reads real `.ts` and JSON files from disk and executes them with Jint. The TypeScript is parsed in C#; running the app requires no Node.js, TypeScript compiler or generated JavaScript.

From the repository root:

```powershell
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0
```

The repository selects the .NET 10 SDK. Use `-f net8.0` to run with an installed .NET 8 runtime. The root NuGet configuration supplies the required Jint preview feed.

## Examples

| Example | TypeScript | Behavior |
| --- | --- | --- |
| Order validation | [validate-order.ts](Scripts/validate-order.ts) | Checks business rules and returns all field errors; calls a C# logging method. |
| Webhook normalization | [normalize-webhook.ts](Scripts/normalize-webhook.ts) | Narrows an unknown payload with runtime checks, normalizes priority and copies only selected metadata. |
| Pricing policies | [quote.ts](Scripts/pricing/quote.ts), [discounts.ts](Scripts/pricing/discounts.ts), [money.ts](Scripts/pricing/money.ts) | Uses relative module imports, abstract classes and type-only imports to select a discount and calculate shipping. |
| Original error locations | [quote.ts](Scripts/pricing/quote.ts) | Rejects an unsupported destination and reports the original `.ts` filename, line and column. This expected error is handled; the example exits successfully. |

The default run validates a good order and an invalid one, normalizes a webhook, quotes two orders, and demonstrates the error. Run one example or supply your own JSON:

```powershell
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0 -- validation
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0 -- webhook
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0 -- pricing
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0 -- errors
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0 -- pricing ./my-order.json
```

See [Inputs](Inputs) for the JSON shapes. The order examples expect that shape and demonstrate business validation; the webhook example explicitly validates an unknown external payload. The sample's pricing rules use integer cents, reject unsafe amounts and quantities, choose the largest discount without stacking, and apply the US free-shipping threshold **after** the discount. They do not calculate taxes or currency conversion.

Expected results with the included inputs:

- `ORD-1001` passes validation. Its 12,500-cent subtotal receives the 1,875-cent coupon discount and free shipping: **10,625 cents** total.
- `ORD-1003` reports three issues: email, quantity and price.
- `ORD-1002` receives no discount: 6,400 cents plus 595 cents shipping, **6,995 cents** total.
- `evt-2048` becomes a high-severity ticket with the environment/service/region tags. The demo token and customer email are omitted from the output.

## C# integration

[Program.cs](Program.cs) reads input files, runs the examples and prints their returned objects. [ScriptHost.cs](ScriptHost.cs) contains the integration to copy into a host application:

1. Read each TypeScript source and call `PrepareScript` or `PrepareModule` once at startup.
2. Reuse those prepared values for subsequent inputs. Create a fresh engine for each execution so globals and module instances stay isolated.
3. Expose [HostServices](HostServices.cs) with `SetValue`. It provides a request ID, timestamp and logging method. Parse JSON data into JavaScript objects with Jint's public `JsonParser`.
4. Register every runtime TypeScript module using `AddModule(prepared)`. Use the same absolute file URI during preparation and registration so relative imports resolve correctly. The ordinary file loader does not compile additional TypeScript dependencies for you.
5. Evaluate the prepared script, or import the prepared module and invoke an exported function. Convert the result to a .NET object with `ToObject()`.

The host sets parser limits and Jint statement, allocation, recursion and execution-time limits. Host methods still run as ordinary C# and should have their own appropriate bounds. This is a small hosting example, not an isolation boundary for arbitrary CLR services.

The default paths are relative to the executable, not the current directory. Build and publish copy `Scripts` and `Inputs` beside it. Edit the source `.ts` files here and run again to rebuild/copy them. A `ScriptHost` keeps its prepared snapshot; recreate it to load source changes. There is no global source cache, filesystem watcher or shared engine.

## Editor support and verification

[host.d.ts](Scripts/host.d.ts) describes the C# host and input shapes for autocomplete and type checking. Its property casing matches the C# object. [contracts.ts](Scripts/pricing/contracts.ts) supplies module types through `import type`. Neither file is loaded by `ScriptHost` at runtime.

Optional type checking uses the repository's pinned development tools and emits no JavaScript:

```powershell
npm ci --ignore-scripts --prefix eng/reference-checks
node eng/reference-checks/node_modules/typescript/bin/tsc --project samples/Jint.TypeScript.Sample/tsconfig.json
```

[SampleTests](../../tests/Jint.TypeScript.Tests/SampleTests.cs) execute these actual files and verify rule boundaries, malformed payloads, selected metadata, relative imports, original error locations, cached-code isolation and execution limits. CI runs those tests and the console app on .NET 8 and .NET 10, and separately type-checks the TypeScript.
