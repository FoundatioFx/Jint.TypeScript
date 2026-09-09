# Usage and syntax reference

See the [README](../README.md) for setup and the [runnable sample](../samples/Jint.TypeScript.Sample/README.md) for file loading, host services, prepared-code reuse, modules and editor typings.

## Reuse and modules

Share the compiler between threads and cache prepared scripts for repeated execution. Each parse has its own state. Engines retain their usual Jint concurrency restrictions. The compiler does not manage a global source cache.

`ParseScript` and `ParseModule` return actual `Acornima.Ast.Script` and `Acornima.Ast.Module` objects. `PrepareScript` and `PrepareModule` return Jint's `Prepared<T>`. ASTs passed to Jint preparation become Jint-owned for `UserData` purposes; do not prepare the same mutable AST concurrently.

```csharp
// Uses the compiler created in the README example.
var engine = new Engine();
engine.Modules.Add("values", builder => builder.AddModule(
    compiler.PrepareModule("export const answer: number = 42;", "values")));
var answer = engine.Modules.Import("values").Get("answer");
```

For automatic TypeScript file imports, configure the reusable loader:

```csharp
var loader = new TypeScriptModuleLoader(Path.GetFullPath("Scripts"), compiler);
var engine = new Engine(options => options.UseModules(loader));
var exports = engine.Modules.Import("./main.ts");
// main.ts and its relative dependencies are prepared automatically.
```

For editor documents, embedded resources or other sources already in memory, pass a source dictionary instead of a directory:

```csharp
var loader = new TypeScriptModuleLoader(new Dictionary<string, string>
{
    ["main.ts"] = "import { n } from './value.ts'; export const answer: number = n + 2;",
    ["value.ts"] = "export const n: number = 40;"
}, compiler);
var engine = new Engine(options => options.UseModules(loader));
var answer = engine.Modules.Import("./main.ts").Get("answer"); // 42
```

File imports require explicit `.ts`, `.mts`, `.js` or `.mjs` extensions. Bare names can reference host modules registered through `engine.Modules.Add`. Package resolution, CommonJS, JSON modules and import attributes are not provided. Declaration files are editor-only; use whole `import type` declarations to remove their runtime dependency. Inline type specifiers retain their containing module's side effects.

Virtual filenames are case-sensitive relative paths using forward slashes, without empty, `.` or `..` segments. Relative imports may traverse parent folders within the virtual root. Virtual loaders never read the filesystem. Directory loaders restrict resolved URLs to their base directory; that restriction is not a filesystem sandbox against symbolic links.

The loader copies virtual sources at construction. Directory sources are read and prepared on first import. Prepared modules are cached per loader instance, including across independent engines; engine-specific module records are never shared. Recreate the loader to observe edits. Cold preparations are serialized within the loader so concurrent engines prepare each dependency once. There is no global cache or file watcher.

`TypeScriptModuleLoaderOptions` defaults to 128 files and 2,000,000 UTF-16 source units. Virtual limits apply to the entire copied snapshot, including unused declaration files. Directory limits apply to the cumulative prepared cache; reads are bounded before allocating the whole source. The compiler's per-file source limit also applies. Exceeding these limits throws Jint's `ModuleGraphLimitException`. This source budget is independent of Jint's module source-byte budget, which does not account for supplied prepared modules. Set Jint execution/module graph limits separately. A cancellation token supplied to the loader applies to that loader's lifetime, including source reads and preparation; recreate it for an independent cancellation lifetime.

For manual prepared-module registration, use the loader's resolved key as `sourceFile`. Runtime strings passed to `eval`, `Function`, or ordinary Jint source APIs remain JavaScript. Those source-string paths are not intercepted. Dynamic imports through a configured `TypeScriptModuleLoader` use that loader normally.

## Existing JavaScript and JSDoc

The native parser accepts ordinary JavaScript, including JSDoc comments, without requiring type annotations. Existing scripts can use it before types are added. Validate your scripts when changing parsers: TypeScript's interpretation of ambiguous expressions can differ from JavaScript (see explicit type arguments below).

Editor typing is separate from execution. In Monaco's TypeScript mode, JSDoc type tags such as `@param`, `@type` and `@typedef` do not supply types. Allowing implicit `any` only suppresses missing-annotation errors; it does not preserve JSDoc IntelliSense. See the [TypeScript JSDoc reference](https://www.typescriptlang.org/docs/handbook/jsdoc-supported-types.html).

Applications that depend on JSDoc typings should keep existing scripts in JavaScript editor mode and offer TypeScript mode when users are ready to replace those typings with annotations. Both can execute through the native parser. The playground's [JSDoc conversion example](../samples/Jint.TypeScript.Sample/README.md#convert-existing-jsdoc-scripts) demonstrates a single action that converts supported annotations and imports, then switches a file to TypeScript mode. Unsupported conversions preserve the original source.

## Supported syntax

| Position/form | Examples |
| --- | --- |
| Variable and destructuring annotations | `let n: number = 1`, `const {x}: Result = value` |
| Function/method parameters and returns | `function f(x: number = 1): number { return x; }` |
| Explicit receiver parameters | `function f(this: Context, x: number) {}` |
| Return predicates and assertions | `x is Shape`, `asserts x`, `asserts this is Ready` |
| Readonly types and value queries | `readonly number[]`, `readonly [number, string]`, `unique symbol`, `typeof value`, `typeof f<T>` |
| Function, method and constructor overloads | `function f(x: number): number; function f(x) { return x; }` |
| Selected ambient declarations | `declare const host: Host`, `declare function run(x: number): void`, `export declare interface Shape {}` |
| Rest parameter annotations | `function f(...xs: number[]) {}` |
| Parenthesized arrows, including async | `(x: number): number => x`, `async (x): Promise<number> => x` |
| Named and qualified references | `Result`, `My.Domain.Result` |
| Primitive and literal types | `number`, `unknown`, `void`, `null`, `"ok"`, `42`, `true`, `42n` |
| Arrays, unions and grouping | `number[]`, `(string \| number)[]`, `\| A \| B` |
| Generic arrows, methods and classes | `<T>(x: T): T => x`, `async <T>(x: T) => x`, `class Box<T> { get<U>() {} }` |
| Explicit expression type arguments | `f<number>(42)`, `f?.<T>(value)`, `new Box<T>()`, `` tag<T>`text` ``, `const g = f<T>` |
| Typed class members and heritage | `public readonly value: T`, `value?: T`, `value!: T`, `extends Base<T> implements Shape<T>` |
| Abstract classes and members | `abstract class C { abstract value: number; abstract run(): void; abstract get result(): number; }` |
| Declaration-only class fields | `class C extends Base { declare value: string; }` |
| Abstract constructor types | `abstract new (...args: Args) => Instance` |
| Generic type references | `Promise<Result>`, `Map<string, Array<number>>`, `Result<T,>` |
| Object types and index signatures | `{ readonly value?: number; [key: string]: number }` |
| Function, method, call and construct types | `(x?: number) => string`, `{ f(x: number): string }`, `new () => Result` |
| Tuples and intersections | `[first: number, second?: string]`, `[...number[]]`, `A & B` |
| Optional identifier parameters | `function f(x?: number) {}`, `(x?) => x` |
| Type aliases and interfaces | `type Id = number`, `interface Shape extends Base { value: Id }` |
| Generics within erased declarations/signatures | `type Box<T = number> = { value: T }`, `interface Box<T> { value: T }` |
| Generic function declarations/expressions | `function identity<T>(x: T): T { return x; }`, `function<T>(x: T) { return x; }` |
| Const and variance type parameters | `function f<const T>(x: T) {}`, `type Reader<out T> = () => T`, `interface Cell<in out T> { value: T }` |
| Conditional types and inference syntax | `T extends U ? X : Y`, `T extends Array<infer U> ? U : never`, `T extends infer U extends string ? U : never` |
| Import types and module value queries | `import("types").Shape<T>`, `typeof import("types")`, `typeof import("types").factory<T>` |
| Assertions | `value as number`, `value as const`, `value satisfies Shape`, `value!.member` |
| Mapped types, key remapping and indexed access | `{ readonly [P in keyof T as P]?: T[P] }`, `T["value"]` |
| Template literal types | `` `prefix-${number}-${string}` `` |
| Explicit type-only imports/exports | `import type { Shape } from './types'`, `export type { Shape }`, `import { type Shape, value } from './values'` |

Types are erased as grammar is consumed. No type checking, symbol resolution, declaration-file loading, runtime validation, or downlevel JavaScript emission occurs. A named type can come from an editor's external `.d.ts` file; this parser does not need that declaration to execute the script.

Type aliases, interfaces, ambient declarations and bodyless function overloads disappear from statement lists without creating runtime bindings or placeholder statements. Runtime names are registered only for implementations. Class method and constructor signatures likewise disappear, including any computed key evaluation. Implementation matching and overload type consistency are left to the editor or `tsc`. They can occur inside ordinary blocks and functions. Type namespace consistency, redeclaration checks and interface merging are left to the editor or `tsc`. Optional parameters are ordinary identifier bindings; optional rest/destructuring/setter parameters and optional parameters with defaults are rejected.

An explicit `this` parameter must be first, typed, and free of optional/rest/default modifiers. It disappears from the runtime parameter list, preserving the receiver, function length, arguments and strict-mode parameter rules. It is supported on functions and ordinary methods; arrows, constructors and accessors remain excluded. Predicates consume types only in return positions and do not check values at runtime.

Ambient `var`, `let`, `const`, function, type-alias and interface declarations are supported, including named exports. An ambient function must have no body. Only an unannotated ambient `const` can carry a supported literal initializer (string, boolean, numeric or bigint, including negative numbers); it is erased without evaluating anything. Template/enum-reference initializers and ambient classes/namespaces remain unsupported.

Module erasure follows explicit `verbatimModuleSyntax` rules: a whole `import type` or `export type` declaration disappears, including its dependency. Inline type specifiers disappear while the containing import/export remains, preserving module side effects. Ordinary imports are retained even when used only in annotations; write `import type` explicitly. If all module declarations disappear, the AST includes an empty `export {}` at EOF to preserve module identity when serialized to JavaScript.

Assertions erase while preserving expression precedence, optional chaining, assignment targets, direct `eval` and method receivers. They do not check or convert values at runtime. A line break before `as`, `satisfies` or postfix `!` ends that assertion opportunity. Assertions are rejected in parameter bindings. Parenthesize composite expressions before asserting and exponentiating, for example `(a + b) as number ** 2`.

Generic functions, arrows, methods and classes support constraints/defaults, including async and generator forms where JavaScript permits them. Explicit type arguments work on calls, optional calls, constructors, tagged templates and instantiation expressions. They are erased without creating runtime type bindings. Mapped types support readonly/optional modifiers, key remapping and nested types in aliases and annotations.

Const type parameters are supported on functions, arrows, methods, classes and type signatures. Variance modifiers (`in`, `out`, `in out`) are accepted on aliases, interfaces and classes; they are rejected on function/method type parameters. Duplicate modifiers and reversed `out in` ordering are rejected. Contextual `out` remains usable as an unmodified parameter name. Modifier applicability and variance correctness that require TypeScript semantic checks remain the responsibility of `tsc`.

Conditional types support nested branches, function/constructor types and `infer` constraints, including inference syntax inside tuples, templates and mapped types. The parser consumes their syntax without resolving types or enforcing inference scope. A conditional type cannot contain a line break before its `extends`. Nested type work remains bounded by `MaxTypeDepth` and `MaxTokenCount`.

Import types consume a string literal module name, an optional dotted qualifier and type arguments. They erase without loading a module, reading declarations, creating runtime dependencies or changing script/module identity. Runtime `import(...)` expressions keep their JavaScript behavior. Import attributes/options inside these type expressions remain unsupported. As with other supported type references, keep type arguments on the same line as their preceding name or import expression.

Class fields retain native JavaScript define semantics: an uninitialized typed field still creates a property. Accessibility, readonly and override modifiers are erased; they do not enforce access or immutability at runtime. JavaScript private fields retain their normal semantics. Modifier words also work as property or method names.

Abstract class declarations remain executable JavaScript classes, including their base-class evaluation, constructors, concrete fields/methods and static blocks. Named/default exports retain runtime bindings. Abstract fields, method signatures and getter/setter signatures disappear. `declare` fields disappear in ordinary or abstract classes, including static fields; they do not create properties or overwrite inherited values. Computed keys belonging to erased members are parsed and validated but never evaluated. For example, `class Child extends Base { declare value: string; }` preserves the value initialized by `Base`.

Abstract members require an abstract class and cannot be static. Abstract bodies, erased-field initializers, `declare` methods/accessors, duplicate modifiers and incompatible modifier combinations are rejected. Getter/setter parameter arity is validated before their signatures disappear. `abstract` must be on the same line as `class`; a newline can leave it as an ordinary JavaScript identifier expression. Erased `#private` declarations, ambient `declare class` declarations, decorators and auto-accessors remain unsupported. Ordinary private fields work normally. This erasure does not enforce abstract instantiation, member implementation, type compatibility or every semantic modifier restriction; use `tsc` for those checks.

Type arguments follow TypeScript's expression disambiguation. For example, `f<number>(42)` executes a call, while ordinary comparisons and shifts keep their JavaScript meaning. Write `(a < b) > (c)` when that comparison is intended. Recognized type-argument forms with unsupported type contents raise a parse error instead of executing a JavaScript fallback.

Use `(f<T>)` when combining a bare instantiation with comparisons or shifts. Ungrouped consecutive forms such as `f<T><U>(x)` remain deliberately unsupported; the reference parsers disagree on some of these boundaries. Generic calls such as `f<T>(x) < g<U>(y)` are supported.

The scope still excludes angle-bracket assertions; ambient classes/namespaces; erased private declarations; escaped type names; JSX/TSX; decorators; enums, namespaces and constructor parameter properties. Object-type accessors/computed keys, destructured function-type parameters and attributes on whole type-only imports or import types remain unsupported. Non-abstract bodyless getter/setter signatures are rejected because TypeScript emits runtime accessors for them. A type query supports a value name or `this`, dotted properties and type arguments. Keep type arguments on the same line as the queried name; parenthesize a generic function type argument, as in `typeof f<(<T>() => T)>`. `asserts` and its parameter must also stay on the same line. Runtime transforms remain a later phase. This is a bounded syntax/erasure implementation, not a type checker or a full validator for every TypeScript production.

## Unsupported syntax and enum alternatives

Recognized unsupported declaration forms produce actionable `TypeScriptParseException` diagnostics. `Code` identifies the failure, `Description` contains the message without a location, and `Index`/`Position` refer to the original TypeScript source. The playground displays the same diagnostics, including failures in imported files.

| Code | Guidance |
| --- | --- |
| `UnsupportedEnum` | Regular, `const`, and ambient enum declarations are unsupported. Use an `as const` object and a union type. |
| `UnsupportedNamespace` | Replace namespaces or ambient module blocks with separate files and ES module imports/exports. |
| `UnsupportedParameterProperty` | Declare the field separately and assign it explicitly in the constructor. |
| `UnsupportedAmbientClass` | Describe a host value using an interface and `declare const`, or keep the ambient class in an editor-only `.d.ts` file. |
| `ModuleSyntaxInScript` | Pass import/export declarations to `ParseModule` or `PrepareModule`. |

For a set of named constants, this supported pattern provides TypeScript completion and a value type:

```typescript
const Status = { Ready: "ready", Done: "done" } as const;
type Status = typeof Status[keyof typeof Status];

function isDone(status: Status): boolean {
    return status === Status.Done;
}

isDone(Status.Done); // true
```

The object exists at runtime; its type declaration and assertions erase. This pattern is also described in the [TypeScript enum handbook](https://www.typescriptlang.org/docs/handbook/enums.html#objects-vs-enums). It does not provide numeric enums' automatic numbering or reverse mappings. Assign numeric values explicitly if needed. `as const` supplies editor checks; it does not freeze the object at runtime.

These diagnostics recognize specific grammar contexts rather than scanning for keywords. Malformed or ambiguous neighboring syntax may still produce an ordinary syntax error. Unsupported declarations must be in a supported declaration position; an enum in an invalid single-statement `if` body, for example, can retain its ordinary syntax error. Monaco's `erasableSyntaxOnly` checks complement these messages but do not guarantee compatibility with every supported parser form.

## Locations, limits and semantics

AST ranges and diagnostics refer to the original UTF-16 source. Lines are one-based and columns zero-based. Type erasure does not alter line endings or offsets. Binding identifiers retain the span of their runtime name; parent nodes span the surrounding original syntax, including erased annotations between runtime children.

`TypeScriptOptions` defaults to a 1,000,000-code-unit source limit, 100,000 constructed AST nodes, 500,000 token steps, type depth 64 and syntax depth 256.

`PrepareScript` and `PrepareModule` additionally check AST depth iteratively before entering Jint's recursive visitors. `MaxAstDepth` defaults to 256 and can be lowered (valid range 1–256). A long, flat member/operator chain can parse successfully but fail preparation with `AstDepthLimit`.

Cancellation is checked on entry, periodically during token processing and depth validation, and before Jint preparation. It is cooperative, not a hard time limit within a single token or Jint's preparation pass. Apply Jint execution constraints separately.

Scripts follow Jint's usual script mode, with top-level return enabled and no implicit strict directive. Set `Strict = true`, use `"use strict"`, or parse a module for strict semantics. `StaticAnalysis`, `FoldConstants`, `CompileRegex` and `RegexTimeout` are configurable. No function source text or referenced-global collection is supplied through the AST preparation path; function `toString()` follows Jint's placeholder behavior.
