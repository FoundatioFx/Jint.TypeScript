# TypeScript playground

A local web app with Monaco file tabs, TypeScript IntelliSense, editable JSON input, and execution through Jint's native C# TypeScript parser. View returned JSON, host logs and diagnostics alongside your code.

Install the .NET 10 SDK and Node 22.12+ (or a compatible newer Node release), then run from the repository root:

```powershell
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0
```

Open [http://localhost:5178](http://localhost:5178). The first build installs pinned frontend dependencies and bundles Monaco and its workers locally; no CDN is required. Subsequent unchanged builds reuse those assets. Use `-f net8.0` with the ASP.NET Core 8 runtime installed to run on .NET 8. The library itself does not require Node.

## Editing and running

- Choose order pricing, order validation, webhook normalization, JSDoc → TypeScript, or the small import example.
- Add `.ts` or `.js` files using **+**, including names such as `lib/helper.ts`. Import them with explicit relative filenames: `import { helper } from './lib/helper.ts'`.
- Select an entry file that exports `run(input)`. Both synchronous and asynchronous functions work. **Run** or **Ctrl/Cmd+Enter** executes the current files; **Stop** cancels a pending request.
- Edit the JSON in **Input**. **Result** displays returned data and `host.Log` messages. Click a diagnostic to navigate to its file and position.
- Monaco provides completion, signatures, hover information, formatting, symbol rename and navigation across open files. Type errors refresh across the workspace when a dependency changes. Editable `host.d.ts` describes the host and shared input shapes.
- Files and input are saved in this browser's local storage. **Reset** restores the selected example. Renaming a file changes its filename; update imports referencing its old name. Symbol rename inside code uses Monaco's language service.

The examples retain real business rules: pricing uses integer cents and chooses the largest discount without stacking; validation returns field issues; webhook normalization checks unknown input and copies selected public metadata. The order examples expect the documented order shape. Types do not validate external input at runtime.

**Webhook normalization** also demonstrates the enum alternative: a `Severity` object declared with `as const`, a derived union type for annotations, and `Severity.High` / `Severity.Normal` used in the returned ticket.

Monaco's bundled TypeScript service performs editor checks, including `erasableSyntaxOnly` and `verbatimModuleSyntax`. That JavaScript worker is for editing only. The server parses user TypeScript in C# and never runs Monaco's emitted JavaScript. Editor checks do not guarantee every construct is supported by the bounded native parser; see [supported syntax](../../docs/usage.md).

## Convert existing JSDoc scripts

Choose **JSDoc → TypeScript**. The three JavaScript files quote a cart at 5,850 cents. They start unchanged in JavaScript editor mode, where JSDoc annotations provide real IntelliSense and type checking. Try completion on `input.` in `migration/main.js`.

Click **Convert to TypeScript** on any JavaScript file. The action adds annotations, converts supported typedefs, renames the file to `.ts`, updates imports (including JSDoc import types), and switches the editor mode. Convert the subtotal helper, the quote module, and finally the entry file; JavaScript and TypeScript files work together at each step. **Run** produces the same result throughout.

**Undo conversion** or **Ctrl/Cmd+Z** restores the last conversion's files, imports, entry point and JavaScript mode. This workspace-level Undo is available until another edit; ordinary editor Undo continues to handle later text edits. Reload preserves the converted files; conversion Undo is session-only.

Conversion runs on demand in a separate browser worker with a ten-second timeout, using the TypeScript library already bundled with Monaco. It checks the converted workspace and compares the emitted JavaScript AST with the original before applying any edits. Existing type errors must be fixed first; missing annotations are not replaced with `any`. Descriptive comments are retained, and the upstream actions may retain redundant JSDoc type tags alongside the new annotations.

The converter supports common parameters, optional/default/rest parameters, returns, variables, generics and typedefs. It refuses unsupported JSDoc forms such as callbacks, casts, inheritance annotations and some adjacent typedef/type comment blocks. Dynamic import paths must be literals so filename references can be updated. Failed or incomplete conversions leave all source files unchanged and explain what needs attention. This is a migration sample with a bounded set of supported forms, not a complete JavaScript-to-TypeScript migration tool.

## Host integration

[PlaygroundRunner.cs](PlaygroundRunner.cs) creates a bounded source snapshot with the library's `TypeScriptModuleLoader`, configures a fresh Jint engine, imports the entry module and invokes `run(input)`. It exposes a small host object with a logging callback, request ID and timestamp. All imported runtime files come from the submitted editor snapshot; declaration files need not be loaded or registered at runtime.

The sample allows 32 files, 100,000 UTF-16 units per file and 200,000 per snapshot, with statement, memory, recursion and time limits. Two runs may execute concurrently. Logs and returned JSON are bounded. Each run has fresh globals and module instances; editing one browser workspace cannot change another request's files. This is a local developer playground, not a deployment template for executing arbitrary public submissions.

Build/publish copies the frontend, examples and inputs beside the executable, so published output works independently of the current directory:

```powershell
dotnet publish samples/Jint.TypeScript.Sample -c Release -f net10.0 -o artifacts/playground
dotnet artifacts/playground/Jint.TypeScript.Sample.dll --urls http://localhost:5178
```

## Frontend development and checks

Run the .NET host above, then use Vite's development server in a second terminal:

```powershell
npm run dev --prefix samples/Jint.TypeScript.Sample/ClientApp
```

Vite proxies `/api` to the .NET host at port 5178. To validate the sample:

```powershell
dotnet test tests/Jint.TypeScript.Tests -c Release --filter "FullyQualifiedName~SampleTests|FullyQualifiedName~PlaygroundTests|FullyQualifiedName~ModuleLoaderTests"
npm ci --ignore-scripts --prefix eng/reference-checks
node eng/reference-checks/node_modules/typescript/bin/tsc --project samples/Jint.TypeScript.Sample/tsconfig.json
npm exec --prefix samples/Jint.TypeScript.Sample/ClientApp -- playwright install chromium
npm test --prefix samples/Jint.TypeScript.Sample/ClientApp
```

The browser suite starts an already-built Release .NET 10 host when necessary. CI runs it against the built web app. C# tests cover actual business rules, virtual/directory imports, source snapshots, isolation, cancellation, limits and original error locations. `SkipPlaygroundBuild=true` skips the frontend build when CI or a developer has already built it explicitly.
