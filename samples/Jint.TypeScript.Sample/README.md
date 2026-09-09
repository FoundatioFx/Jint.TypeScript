# TypeScript playground

A local web app with Monaco file tabs, TypeScript IntelliSense, editable JSON input, and execution through Jint's native C# TypeScript parser. It replaces the console sample. There is no HTML/component preview: output is returned JSON, host logs and diagnostics.

Install the .NET 10 SDK and Node 22.12+ (or a compatible newer Node release), then run from the repository root:

```powershell
dotnet run --project samples/Jint.TypeScript.Sample -f net10.0
```

Open [http://localhost:5178](http://localhost:5178). The first build installs pinned frontend dependencies and bundles Monaco and its workers locally; no CDN is required. Subsequent unchanged builds reuse those assets. Use `-f net8.0` with the ASP.NET Core 8 runtime installed to run on .NET 8. The library itself does not require Node.

## Editing and running

- Choose order pricing, order validation, webhook normalization, or the small import example.
- Add `.ts` files using **+**, including names such as `lib/helper.ts`. Import them with explicit relative filenames: `import { helper } from './lib/helper.ts'`.
- Select an entry file that exports `run(input)`. Both synchronous and asynchronous functions work. **Run** or **Ctrl/Cmd+Enter** executes the current files; **Stop** cancels a pending request.
- Edit the JSON in **Input**. **Result** displays returned data and `host.Log` messages. Click a diagnostic to navigate to its file and position.
- Monaco provides completion, signatures, hover information, formatting, symbol rename and navigation across open files. Type errors refresh across the workspace when a dependency changes. Editable `host.d.ts` describes the host and shared input shapes.
- Files and input are saved in this browser's local storage. **Reset** restores the selected example. Renaming a file changes its filename; update imports referencing its old name. Symbol rename inside code uses Monaco's language service.

The examples retain real business rules: pricing uses integer cents and chooses the largest discount without stacking; validation returns field issues; webhook normalization checks unknown input and copies selected public metadata. The order examples expect the documented order shape. Types do not validate external input at runtime.

Monaco's bundled TypeScript service performs editor checks, including `erasableSyntaxOnly` and `verbatimModuleSyntax`. That JavaScript worker is for editing only. The server parses user TypeScript in C# and never runs Monaco's emitted JavaScript. Editor checks do not guarantee every construct is supported by the bounded native parser; see [supported syntax](../../docs/usage.md).

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
