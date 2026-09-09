import { TypeScriptWorker } from 'monaco-editor/languages/features/typescript/tsWorker.js';
import { typescript as ts } from 'monaco-editor/languages/features/typescript/lib/typescriptServices.js';

const root = 'file:///__jint_typescript__/';
const uri = name => root + name.split('/').map(encodeURIComponent).join('/');
const options = { target: ts.ScriptTarget.ESNext, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.NodeJs, lib: ['lib.es2023.d.ts'], types: [],
    allowJs: true, checkJs: true, strict: true, noUncheckedIndexedAccess: true,
    allowImportingTsExtensions: true, verbatimModuleSyntax: true, erasableSyntaxOnly: true, noEmit: true };
const format = { indentSize: 4, tabSize: 4, newLineCharacter: '\n', convertTabsToSpaces: true };
const supportedTags = new Set(['param', 'arg', 'argument', 'return', 'returns', 'type', 'typedef', 'property', 'prop', 'template']);
const documentationTags = new Set(['description', 'summary', 'example', 'deprecated', 'see', 'link', 'author', 'license', 'copyright', 'since', 'version', 'throws', 'exception', 'todo', 'note', 'remarks']);

class ConversionError extends Error {
    constructor(message, node, source) {
        super(message);
        const position = node && source.getLineAndCharacterOfPosition(node.getStart(source));
        this.issue = { message, ...(position && { line: position.line + 1, column: position.character + 1 }) };
    }
}
function fail(message, node, source) { throw new ConversionError(message, node, source); }
function applyEdits(text, edits) {
    let boundary = text.length;
    for (const edit of [...edits].sort((a, b) => b.span.start - a.span.start)) {
        const { start, length } = edit.span;
        if (start < 0 || length < 0 || start + length > boundary) fail('The converter produced overlapping edits. Your files have not changed.');
        text = text.slice(0, start) + edit.newText + text.slice(start + length);
        boundary = start;
    }
    return text;
}
function visit(node, action) { action(node); ts.forEachChild(node, child => { visit(child, action); }); }

function runtimeShape(text) {
    const source = ts.createSourceFile('runtime.js', text, ts.ScriptTarget.ESNext, true, ts.ScriptKind.JS);
    function shape(node) {
        const children = [];
        ts.forEachChild(node, child => { children.push(shape(child)); });
        // Ignore source offsets and optional arrow-parameter parentheses, which are not
        // AST children. Keep expression parentheses, declaration flags and raw templates.
        return [node.kind, node.flags, children.length ? undefined : node.text, node.rawText, children];
    }
    return JSON.stringify(shape(source));
}

function renameImports(program, snapshot, originalUri) {
    // TypeScript's rename action deliberately retains .js import extensions for emitted
    // output. Jint loads the source files, so update actual module literals, including
    // JSDoc import types. Unrelated strings and comments must remain unchanged.
    for (const [name, item] of snapshot) {
        const source = program.getSourceFile(name), edits = new Map();
        function inspect(node) {
            let literal;
            if (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) literal = node.moduleSpecifier;
            else if (ts.isImportTypeNode(node) && ts.isLiteralTypeNode(node.argument)) literal = node.argument.literal;
            else if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
                literal = node.arguments[0];
                if (!literal || !ts.isStringLiteralLike(literal))
                    fail('Dynamic imports must use literal paths so the converter can update filenames.');
            }
            if (literal && ts.isStringLiteralLike(literal) && literal.text.startsWith('.')
                && new URL(literal.text, name).href === originalUri) {
                edits.set(literal.getStart(source), { span: { start: literal.getStart(source), length: literal.getWidth(source) },
                    newText: JSON.stringify(literal.text.slice(0, -3) + '.ts') });
            }
            for (const doc of node.jsDoc ?? []) visit(doc, inspect);
        }
        visit(source, inspect);
        item.text = applyEdits(item.text, [...edits.values()]); item.version++;
    }
}

// Fail closed for JSDoc features the bundled code fixes do not preserve. In particular,
// an empty diagnostics list alone cannot prove that unused JSDoc types were converted.
function checkJsDoc(source) {
    const optionalParameters = [];
    visit(source, node => {
        for (const doc of node.jsDoc ?? []) {
            const typedef = doc.tags?.some(tag => ts.isJSDocTypedefTag(tag));
            const initializer = ts.isVariableStatement(node) && node.declarationList.declarations.length === 1
                ? node.declarationList.declarations[0].initializer : undefined;
            const signature = ts.isFunctionLike(node) ? node : initializer && ts.isFunctionLike(initializer) ? initializer : undefined;
            for (const tag of doc.tags ?? []) {
                const name = tag.tagName.text;
                if (!supportedTags.has(name) && !documentationTags.has(name))
                    fail(`JSDoc @${name} is not supported by this converter. Convert it manually before switching to TypeScript.`, tag, source);
                if (['param', 'arg', 'argument', 'return', 'returns', 'template'].includes(name) && !signature && !typedef)
                    fail(`JSDoc @${name} is not attached to a supported function or typedef.`, tag, source);
                if (['property', 'prop'].includes(name) && !typedef)
                    fail('JSDoc properties need an explicit @typedef before conversion.', tag, source);
                if (name === 'template' && signature && !ts.getJSDocReturnType(signature)
                    && !signature.parameters.some(parameter => ts.getJSDocType(parameter)))
                    fail('Add parameter or return types for this JSDoc template before converting.', tag, source);
                if (ts.isJSDocTypeTag(tag) && !(ts.isVariableStatement(node) || ts.isPropertyDeclaration(node)))
                    fail('JSDoc casts and expression types are not supported by this converter.', tag, source);
                if (ts.isJSDocTypeTag(tag) && ts.isVariableStatement(node) && node.declarationList.declarations.length !== 1)
                    fail('Split this declaration into separate variables before converting its JSDoc type.', tag, source);
            }
        }
        if (ts.isParameter(node)) {
            const tags = ts.getJSDocParameterTags(node);
            const type = ts.getJSDocType(node);
            if (type?.kind === ts.SyntaxKind.JSDocVariadicType && !node.dotDotDotToken)
                fail('A variadic JSDoc parameter needs an explicit JavaScript rest parameter before conversion.', node, source);
            if (tags.some(tag => tag.isBracketed) || type?.kind === ts.SyntaxKind.JSDocOptionalType) {
                if (!ts.isIdentifier(node.name) || node.dotDotDotToken)
                    fail('Optional destructured or rest parameters are not supported by this converter.', node, source);
                if (!node.initializer) optionalParameters.push(node.name.end);
            }
        }
    });
    return optionalParameters;
}

/** Converts one file in a bounded, isolated copy. The caller applies a successful result atomically. */
export async function convertJsDoc(files, file) {
    try {
        const names = Object.keys(files);
        if (!names.length || names.length > 32 || names.some(name =>
            !/\.(ts|js)$/.test(name) || name.length > 240 || /[\\:\x00-\x1f]/.test(name)
            || name.split('/').some(part => !part || part === '.' || part === '..')
            || typeof files[name] !== 'string' || files[name].length > 100_000)
            || Object.values(files).reduce((sum, text) => sum + text.length, 0) > 200_000)
            fail('The workspace exceeds the conversion limits (32 files, 100,000 characters per file, 200,000 total).');
        if (!file.endsWith('.js') || !Object.hasOwn(files, file)) fail('Choose a JavaScript file to convert.');
        const target = file.slice(0, -3) + '.ts';
        if (Object.hasOwn(files, target)) fail(`${target} already exists. Rename it before converting this file.`);
        const snapshot = new Map(names.map(name => [uri(name), { text: files[name], version: 1 }]));
        const worker = new TypeScriptWorker({ getMirrorModels: () => [...snapshot].map(([name, item]) => ({
            uri: { path: new URL(name).pathname, toString: () => name }, version: item.version, getValue: () => item.text
        })) }, { compilerOptions: options, extraLibs: {}, inlayHintsOptions: {} });
        const service = worker.getLanguageService();
        try {
            const originalUri = uri(file), targetUri = uri(target);
            const original = service.getProgram().getSourceFile(originalUri);
            if (original.parseDiagnostics.length) fail('Fix the JavaScript syntax errors before converting.', original, original);
            checkJsDoc(original);
            const checker = service.getProgram().getTypeChecker();
            const exportedNames = new Set(original.symbol ? checker.getExportsOfModule(original.symbol).map(symbol => symbol.name) : []);
            // The upstream annotation fix misses [optional] parameters. Record the explicit
            // question tokens in the scratch TS file before requesting its code fixes.
            renameImports(service.getProgram(), snapshot, originalUri);
            const renamedOriginal = snapshot.get(originalUri).text;
            // Reapply optional markers against the renamed source: a self-import can change offsets.
            const optionalAfterRename = checkJsDoc(ts.createSourceFile(originalUri, renamedOriginal, ts.ScriptTarget.ESNext, true, ts.ScriptKind.JS));
            const converted = { text: applyEdits(renamedOriginal, optionalAfterRename.map(start => ({ span: { start, length: 0 }, newText: '?' }))), version: 1 };
            snapshot.delete(originalUri); snapshot.set(targetUri, converted);
            for (let pass = 0; ; pass++) {
                const diagnostics = (await worker.getSuggestionDiagnostics(targetUri)).filter(d => d.code === 80004 || d.code === 80009);
                if (!diagnostics.length) break;
                if (pass >= 256) fail('This file has too many JSDoc declarations to convert in one operation.');
                const diagnostic = diagnostics[0];
                const fixes = await worker.getCodeFixesAtPosition(targetUri, diagnostic.start, diagnostic.start + diagnostic.length, [diagnostic.code], format);
                const fix = fixes.find(action => action.fixName === 'annotateWithTypeFromJSDoc' || action.fixName === 'convertTypedefToType');
                if (!fix || fix.changes.some(change => change.fileName !== targetUri)) fail('A JSDoc declaration could not be converted. Your files have not changed.');
                const next = applyEdits(converted.text, fix.changes.flatMap(change => change.textChanges));
                if (next === converted.text) fail('A JSDoc declaration could not be converted. Your files have not changed.');
                converted.text = next; converted.version++;
            }
            // JSDoc typedefs are implicitly exported from JavaScript modules. The code fix
            // creates local aliases, so restore their public visibility for existing imports.
            const convertedAst = service.getProgram().getSourceFile(targetUri);
            const exports = convertedAst.statements.filter(node => (ts.isTypeAliasDeclaration(node) || ts.isInterfaceDeclaration(node))
                && exportedNames.has(node.name.text) && !node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.ExportKeyword));
            converted.text = applyEdits(converted.text, exports.map(node => ({ span: { start: node.getStart(convertedAst), length: 0 }, newText: 'export ' })));
            converted.version++;
            const diagnostics = (await Promise.all([...snapshot.keys()].map(async name => ([
                ...await worker.getSyntacticDiagnostics(name), ...await worker.getSemanticDiagnostics(name)
            ]).map(diagnostic => ({ file: decodeURIComponent(name.slice(root.length)), diagnostic }))))).flat()
                .filter(item => item.diagnostic.category === ts.DiagnosticCategory.Error);
            if (diagnostics.length) {
                const { file: problemFile, diagnostic } = diagnostics[0];
                fail(`Conversion needs attention in ${problemFile}: ${ts.flattenDiagnosticMessageText(diagnostic.messageText, ' ')} Your files have not changed.`);
            }
            // A type-only migration must preserve emitted runtime code. Import path changes
            // are included in both sides of this comparison.
            const emitOptions = { ...options, noEmit: false, removeComments: true };
            const emit = (source, filename) => ts.transpileModule(source, { fileName: filename, compilerOptions: emitOptions }).outputText;
            if (runtimeShape(emit(renamedOriginal, originalUri)) !== runtimeShape(emit(converted.text, targetUri)))
                fail('Conversion would change runtime JavaScript. Your files have not changed.');
            const result = {};
            for (const name of names) result[name === file ? target : name] = snapshot.get(uri(name === file ? target : name)).text;
            if (Object.values(result).some(text => text.length > 100_000) || Object.values(result).reduce((sum, text) => sum + text.length, 0) > 200_000)
                fail('The converted source exceeds the workspace size limit.');
            return { success: true, files: result, file: target };
        } finally { service.dispose(); }
    } catch (error) {
        return { success: false, issues: [error instanceof ConversionError ? error.issue : { message: 'The converter could not process this file. Your files have not changed.' }] };
    }
}
