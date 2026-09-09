# Jint.TypeScript

Run TypeScript scripts and modules in .NET with Jint. The native C# parser is based on [Acornima](https://github.com/adams85/acornima). It erases supported type syntax while preserving original source locations for errors. Library execution requires no Node.js runtime.

**Experimental:** supports an erasable subset of TypeScript on .NET 8 and .NET 10, using Jint 5 preview packages.

Try the [Monaco playground](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/samples/Jint.TypeScript.Sample/README.md): edit files with IntelliSense, import helpers, and run pricing, validation and webhook examples with JSON input, results and host logs. It includes an `as const` enum alternative and [JSDoc → TypeScript conversion](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/samples/Jint.TypeScript.Sample/README.md#convert-existing-jsdoc-scripts) with undo.

![TypeScript IntelliSense for the imported Quote interface, with execution results and host logs in the playground](https://raw.githubusercontent.com/FoundatioFx/Jint.TypeScript/main/docs/images/playground-intellisense.png)

From the cloned repository root, with the .NET 10 SDK and Node 22.12+ installed:

```powershell
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0
```

Open [localhost:5178](http://localhost:5178).

## Get started

Preview CI packages are available from Foundatio Feedz. From your application's project directory, add both feeds and install a preview:

```powershell
dotnet nuget add source https://f.feedz.io/foundatio/foundatio/nuget/index.json --name Foundatio
dotnet nuget add source https://f.feedz.io/sebastienros/jint/nuget/index.json --name Jint-preview
dotnet add package Jint.TypeScript --prerelease
```

The Jint preview feed is required for the Jint 5 dependency. Reuse these sources if already configured. See [packages and versions](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/ci-packages.md) for source mapping, build artifacts and tagged releases.

```csharp
using Jint;
using Jint.TypeScript;

var compiler = new TypeScriptCompiler();
var prepared = compiler.PrepareScript("""
    function add(x: number, y: number): number {
        return x + y;
    }
    add(input, 2);
    """, "rules.ts");

var result = new Engine().SetValue("input", 40).Evaluate(prepared).AsNumber(); // 42
```

Cache prepared scripts for repeated execution. The compiler can be shared between threads; Jint engines retain their usual concurrency restrictions.

For scripts with imports, configure a `TypeScriptModuleLoader` using a directory or a dictionary of virtual files:

```csharp
var loader = new TypeScriptModuleLoader(Path.GetFullPath("Scripts"), compiler);
var engine = new Engine(options => options.UseModules(loader));
var exports = engine.Modules.Import("./main.ts");
```

Use explicit file extensions and `import type` for type-only dependencies. The loader caches prepared modules; recreate it after source changes. See [module loading](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md#reuse-and-modules) for examples and limits. `PrepareModule` is available for manual module registration.

## TypeScript support

Supported features include:

- Type annotations, aliases, interfaces, unions, intersections, tuples, mapped and conditional types.
- Generic functions, arrows, calls, and classes.
- Typed and abstract classes, overload signatures, and selected `declare` declarations.
- `as`, `satisfies`, non-null assertions, and explicit type-only imports and exports.

Types are erased without type checking or runtime validation. Use your editor or `tsc` with `erasableSyntaxOnly` and `verbatimModuleSyntax`; the playground already enables these checks. They help catch unsupported constructs but do not guarantee compatibility with every parser form.

Ordinary JavaScript and JSDoc comments can also run through the compiler. JSDoc editor typings require JavaScript mode; the playground converts supported JSDoc annotations and switches a file to TypeScript mode in one action. See [JavaScript migration](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md#existing-javascript-and-jsdoc) for details.

Enums, namespaces, constructor parameter properties, JSX/TSX, and decorators are not supported. Strings passed to Jint's ordinary execution APIs, `eval`, or `Function` must still be JavaScript.

Parse failures expose `TypeScriptParseException` with a diagnostic code and original source file/position. See [diagnostics and enum alternatives](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md#unsupported-syntax-and-enum-alternatives) for guidance, including the `as const` object and union type pattern.

See the [usage and syntax reference](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/usage.md), [Acornima comparison](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/acornima-comparison.md), and [performance measurements](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/docs/performance.md). [CI](https://github.com/FoundatioFx/Jint.TypeScript/actions/workflows/build.yml) checks JavaScript compatibility through our parser, TypeScript reference cases, Jint execution and the playground.

## License

Apache-2.0, with imported components under their respective licenses. See [LICENSE](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/LICENSE.txt) and [third-party notices](https://github.com/FoundatioFx/Jint.TypeScript/blob/main/THIRD-PARTY-NOTICES.txt).
