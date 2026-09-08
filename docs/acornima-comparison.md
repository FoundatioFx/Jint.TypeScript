# How Jint.TypeScript differs from Acornima

[Acornima](https://github.com/adams85/acornima) provides the JavaScript parser this project builds on. Jint.TypeScript adds native C# TypeScript erasure and Jint preparation while continuing to use Acornima's official AST types.

| Area | Acornima | Jint.TypeScript |
| --- | --- | --- |
| Input | JavaScript, with a separate JSX extension | JavaScript and a supported subset of TypeScript; no JSX/TSX |
| Public API | General-purpose parser and tokenizer APIs | `TypeScriptCompiler.ParseScript` / `ParseModule` and `PrepareScript` / `PrepareModule` for Jint |
| Output | Acornima JavaScript AST | The same official AST types with TypeScript syntax erased, or Jint's prepared scripts/modules |
| Locations | Original JavaScript source locations | Original TypeScript source locations, including after type erasure |

This project does not type-check, downlevel JavaScript, or implement all TypeScript syntax. See the [supported forms and limitations](usage.md). Ordinary Jint source APIs, `eval`, and `Function` still accept JavaScript.

## Why maintain parser source instead of using Acornima directly?

We **do use the official Acornima package** for AST types and regex integration. Its public parser cannot consume TypeScript, however, and the grammar/tokenizer hooks needed to extend it are internal. A visitor cannot erase types from an AST if parsing fails on those types first.

In the June 2025 discussion of [the proposed TypeScript extension](https://github.com/adams85/acornima/pull/21), Acornima's maintainer declined the implementation over correctness and maintenance concerns. He also [declined the request to make the extension interface and supporting token types public](https://github.com/adams85/acornima/pull/21#issuecomment-2994226228), preferring to keep those implementation details internal.

He [offered `InternalsVisibleTo` access for a named extension assembly](https://github.com/adams85/acornima/pull/21#issuecomment-2994149462). That would provide access to internals, without establishing a public extension API. We chose a small, independently maintained parser port, with public APIs at the Acornima AST and Jint integration boundaries. Jint's supplied-AST preparation API makes this possible without forking Jint.

## Changes to the core parser

Handwritten hooks are limited to five upstream files:

- **`Parser.Expression.cs`:** recognize assertions, generic calls/arrows, and typed expression boundaries while preserving JavaScript precedence.
- **`Parser.Statement.cs`:** erase type-only declarations and signatures, handle typed classes, and preserve runtime bindings, module semantics, and statement ranges.
- **`Parser.LVal.cs`:** consume binding/parameter annotations and explicit `this` parameters while retaining assignment-target validation.
- **`Parser.Helpers.cs`:** finalize official AST nodes through public APIs while retaining source locations and node accounting.
- **`Tokenizer.cs`:** enter type mode so nested generic closers such as `>>` can be split without changing ordinary JavaScript shifts.

Most TypeScript logic lives in our own partial files. Import tooling also adapts namespaces, shared types, and AST construction to public APIs; those automated changes are additional to the handwritten hooks. See the [customization audit](upstream-customization-audit.md) for the full rationale and the [update workflow](upstream-updates.md) for how we track and merge upstream changes.
