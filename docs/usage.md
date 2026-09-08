# Usage and syntax reference

See the [README](../README.md) for setup and a script example.

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

Register prepared modules through the host. For relative imports, use the loader's resolved module key as `sourceFile`. Runtime strings passed to `eval`, `Function`, or ordinary Jint source APIs remain JavaScript. This library does not intercept those paths.

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
| Generic type references | `Promise<Result>`, `Map<string, Array<number>>`, `Result<T,>` |
| Object types and index signatures | `{ readonly value?: number; [key: string]: number }` |
| Function, method, call and construct types | `(x?: number) => string`, `{ f(x: number): string }`, `new () => Result` |
| Tuples and intersections | `[first: number, second?: string]`, `[...number[]]`, `A & B` |
| Optional identifier parameters | `function f(x?: number) {}`, `(x?) => x` |
| Type aliases and interfaces | `type Id = number`, `interface Shape extends Base { value: Id }` |
| Generics within erased declarations/signatures | `type Box<T = number> = { value: T }`, `interface Box<T> { value: T }` |
| Generic function declarations/expressions | `function identity<T>(x: T): T { return x; }`, `function<T>(x: T) { return x; }` |
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

Class fields retain native JavaScript define semantics: an uninitialized typed field still creates a property. Accessibility, readonly and override modifiers are erased; they do not enforce access or immutability at runtime. JavaScript private fields retain their normal semantics. Modifier words also work as property or method names. Abstract/declare members and constructor parameter properties remain unsupported.

Type arguments follow TypeScript's expression disambiguation. For example, `f<number>(42)` executes a call, while ordinary comparisons and shifts keep their JavaScript meaning. Write `(a < b) > (c)` when that comparison is intended. Recognized type-argument forms with unsupported type contents raise a parse error instead of executing a JavaScript fallback.

Use `(f<T>)` when combining a bare instantiation with comparisons or shifts. Ungrouped consecutive forms such as `f<T><U>(x)` remain deliberately unsupported; the reference parsers disagree on some of these boundaries. Generic calls such as `f<T>(x) < g<U>(y)` are supported.

The scope still excludes angle-bracket assertions; ambient classes/namespaces and abstract/declare class members; conditional/infer types; import types and `typeof import(...)` queries; escaped type names; JSX/TSX; decorators; enums, namespaces and constructor parameter properties. Object-type accessors/computed keys, destructured function-type parameters, variance/const type parameters and attributes on whole type-only imports remain unsupported. Bodyless getter/setter signatures are rejected because TypeScript emits runtime accessors for them. A type query supports a value name or `this`, dotted properties and type arguments. Keep type arguments on the same line as the queried name; parenthesize a generic function type argument, as in `typeof f<(<T>() => T)>`. `asserts` and its parameter must also stay on the same line. Runtime transforms remain a later phase. This is a bounded syntax/erasure implementation, not a type checker or a full validator for every TypeScript production.

## Locations, limits and semantics

AST ranges and diagnostics refer to the original UTF-16 source. Lines are one-based and columns zero-based. Type erasure does not alter line endings or offsets. Binding identifiers retain the span of their runtime name; parent nodes span the surrounding original syntax, including erased annotations between runtime children.

`TypeScriptOptions` defaults to a 1,000,000-code-unit source limit, 100,000 constructed AST nodes, 500,000 token steps, type depth 64 and syntax depth 256.

`PrepareScript` and `PrepareModule` additionally check AST depth iteratively before entering Jint's recursive visitors. `MaxAstDepth` defaults to 256 and can be lowered (valid range 1–256). A long, flat member/operator chain can parse successfully but fail preparation with `AstDepthLimit`.

Cancellation is checked on entry, periodically during token processing and depth validation, and before Jint preparation. It is cooperative, not a hard time limit within a single token or Jint's preparation pass. Apply Jint execution constraints separately.

Scripts follow Jint's usual script mode, with top-level return enabled and no implicit strict directive. Set `Strict = true`, use `"use strict"`, or parse a module for strict semantics. `StaticAnalysis`, `FoldConstants`, `CompileRegex` and `RegexTimeout` are configurable. No function source text or referenced-global collection is supplied through the AST preparation path; function `toString()` follows Jint's placeholder behavior.
