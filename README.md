# Jint.TypeScript

**TypeScript scripting for .NET applications.** Let users customize business rules, validate data, and automate workflows with TypeScript, then run their scripts inside your application with [Jint](https://github.com/sebastienros/jint).

- **A better editing experience.** Use interfaces, generics, and type annotations for IntelliSense, navigation, and type checking in your editor.
- **Native .NET execution.** Parse TypeScript in C# with no Node.js runtime or JavaScript transpiler in the execution path.
- **Reusable scripts and modules.** Prepare scripts once for repeated execution and load imports from a directory or virtual files.
- **Incremental adoption.** Run existing JavaScript scripts and add TypeScript annotations as you go.

## See it in action

The [Monaco playground](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/samples/Jint.TypeScript.Sample/README.md) includes working pricing, validation, and webhook examples. Edit multiple files with TypeScript IntelliSense, import helpers, and run scripts against JSON input with results and host logs. There's also a JSDoc migration example that converts supported annotations and switches editor modes in one action.

![TypeScript IntelliSense for the imported Quote interface, with execution results and host logs in the playground](https://raw.githubusercontent.com/FoundatioFx/Jint.TypeScript/main/docs/images/playground-intellisense.png)

## Use it in your application

[Install the preview package](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/ci-packages.md#consume-a-build), then prepare a rule and pass in your application data:

```csharp
using Jint;
using Jint.TypeScript;

var compiler = new TypeScriptCompiler();
var rule = compiler.PrepareScript("""
    interface Order { subtotal: number; preferredCustomer: boolean; }
    declare const order: Order;

    order.preferredCustomer || order.subtotal >= 100 ? 0 : 5;
    """, "shipping.ts");

var order = new { subtotal = 75, preferredCustomer = false };
var shippingCost = new Engine().SetValue("order", order).Evaluate(rule).AsNumber(); // 5
```

Reuse `rule` for subsequent orders without parsing it again. See the [usage guide](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md) for module loading, JavaScript migration, diagnostics, and supported syntax.

**Experimental:** targets .NET 8 and .NET 10 with Jint 5 previews. Supports erasable TypeScript syntax; types are checked by your editor or `tsc`, not at runtime. Features requiring generated runtime code, such as enums and constructor parameter properties, are unsupported. See [limitations and enum alternatives](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md#unsupported-syntax-and-enum-alternatives).

Built on [Acornima](https://github.com/adams85/acornima), with a focused parser fork for TypeScript support. Read [how it differs from Acornima](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/acornima-comparison.md) and the [performance measurements](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/performance.md).

Apache-2.0, with imported components under their respective licenses. See [LICENSE](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/LICENSE.txt) and [third-party notices](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/THIRD-PARTY-NOTICES.txt).
