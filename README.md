# Jint.TypeScript

Run TypeScript scripts in .NET with Jint. The C# parser is based on [Acornima](https://github.com/adams85/acornima) and requires no Node.js runtime. It erases type syntax while preserving original source locations for errors.

**Experimental:** supports a growing subset of TypeScript on .NET 8 and .NET 10, using Jint 5 preview packages.

## Get started

No package has been published yet. Reference [Jint.TypeScript.csproj](src/Jint.TypeScript/Jint.TypeScript.csproj) from your application and add the Jint preview feed from [NuGet.Config](NuGet.Config) to your NuGet sources.

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

var result = new Engine().SetValue("input", 40).Evaluate(prepared); // 42
```

Cache prepared scripts for repeated execution. The compiler can be shared between threads; Jint engines retain their usual concurrency restrictions. Use `PrepareModule` to register TypeScript modules with Jint.

## TypeScript support

Supported features include:

- Type annotations, aliases, interfaces, unions, tuples, and mapped types.
- Generic functions, arrows, calls, and classes.
- Typed class members, overload signatures, and selected `declare` declarations.
- `as`, `satisfies`, non-null assertions, and explicit type-only imports and exports.

Types are erased without type checking or runtime validation. Use your editor or `tsc` for type checking.

Enums, namespaces, constructor parameter properties, JSX/TSX, and decorators are not supported. Some type syntax, including conditional and `infer` types, is also unsupported. Strings passed to Jint's ordinary execution APIs, `eval`, or `Function` must still be JavaScript.

See the [Acornima comparison](docs/acornima-comparison.md), [usage and syntax reference](docs/usage.md), and [performance measurements](docs/performance.md).

## License

Apache-2.0, with imported components under their respective licenses. See [LICENSE](LICENSE.txt) and [third-party notices](THIRD-PARTY-NOTICES.txt).
