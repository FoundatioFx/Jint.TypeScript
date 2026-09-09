import * as monaco from './monaco';
import * as typescript from 'monaco-editor/languages/features/typescript/register.js';
import 'monaco-editor/languages/definitions/typescript/register.js';
import 'monaco-editor/languages/definitions/javascript/register.js';
import { convertJsDoc } from './conversion';
import EditorWorker from 'monaco-editor/editor/editor.worker.js?worker';
import TypeScriptWorker from 'monaco-editor/language/typescript/ts.worker.js?worker';
import './style.css';

type Example = { id: string; name: string; description: string; entryFile: string; files: Record<string, string>; input: unknown };
type Diagnostic = { code: string; message: string; file?: string; line?: number; column?: number; stack?: string };
type RunResponse = { success: boolean; result: unknown; logs: string[]; diagnostic?: Diagnostic; elapsedMilliseconds: number };
type SavedWorkspace = { version: 1; example: string; entry: string; active: string; files: Record<string, string>; input: string };
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const root = 'file:///__jint_typescript__/';
const storageKey = 'jint-typescript-playground-v1';
const uriFor = (name: string) => monaco.Uri.parse(root + name.split('/').map(encodeURIComponent).join('/'));
const nameFor = (uri: monaco.Uri) => uri.toString().startsWith(root) ? decodeURIComponent(uri.toString().slice(root.length)) : '';
const models = new Map<string, monaco.editor.ITextModel>();
const views = new Map<string, monaco.editor.ICodeEditorViewState | null>();
let examples: Example[] = [];
let current: Example;
let active = '';
let loading = false;
let revision = 0;
let saveTimer: ReturnType<typeof setTimeout>;
let runController: AbortController | undefined;
let renameFrom: string | undefined;
let workerAccessor: Awaited<ReturnType<typeof typescript.getTypeScriptWorker>> | undefined;
let diagnosticsTimer: ReturnType<typeof setTimeout>;
let converting = false;
let conversionUndo: SavedWorkspace | undefined;
const languageFor = (name: string) => name.endsWith('.js') ? 'javascript' : 'typescript';
const languageDefaults = [typescript.typescriptDefaults, typescript.javascriptDefaults];

self.MonacoEnvironment = {
    getWorker: (_id, label) => label === 'typescript' || label === 'javascript' ? new TypeScriptWorker() : new EditorWorker()
};
for (const defaults of languageDefaults) {
    defaults.setCompilerOptions({
        target: typescript.ScriptTarget.ESNext,
        module: typescript.ModuleKind.ESNext,
        moduleResolution: typescript.ModuleResolutionKind.NodeJs,
        lib: ['es2023'], types: [], strict: true, noUncheckedIndexedAccess: true,
        allowJs: true, checkJs: true,
        allowNonTsExtensions: true, allowImportingTsExtensions: true, verbatimModuleSyntax: true,
        erasableSyntaxOnly: true, noEmit: true
    });
    defaults.setEagerModelSync(true);
    defaults.setDiagnosticsOptions({ noSemanticValidation: false, noSyntaxValidation: false, noSuggestionDiagnostics: false });
    // Refresh the whole small workspace when an imported file changes, rather than leaving
    // diagnostics on its consumers stale until each consumer is edited.
    defaults.setModeConfiguration({ ...defaults.modeConfiguration, diagnostics: false });
}
monaco.editor.defineTheme('playground', {
    base: 'vs-dark', inherit: true, rules: [], colors: {
        'editor.background': '#1e2026', 'editorLineNumber.foreground': '#626a79',
        'editorLineNumber.activeForeground': '#bdc5d2', 'editor.lineHighlightBackground': '#242730',
        'editor.selectionBackground': '#36465b', 'editorCursor.foreground': '#70d5b4'
    }
});
const editor = monaco.editor.create($('editor'), {
    theme: 'playground', model: null, automaticLayout: true, minimap: { enabled: false },
    fontFamily: "'Cascadia Code', 'SFMono-Regular', Consolas, 'Liberation Mono', monospace", fontSize: 14,
    lineHeight: 24, padding: { top: 12 }, scrollBeyondLastLine: false,
    tabSize: 4, insertSpaces: true, renderLineHighlight: 'all', smoothScrolling: true,
    fixedOverflowWidgets: true, ariaLabel: 'TypeScript code', bracketPairColorization: { enabled: true }
});
editor.onDidChangeCursorPosition(({ position }) => $('cursor').textContent = `Ln ${position.lineNumber}, Col ${position.column}`);
editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => void run());
const canUndoConversion = editor.createContextKey<boolean>('canUndoConversion', false);
editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyZ, undoConversion, 'canUndoConversion');
monaco.editor.registerEditorOpener({
    openCodeEditor: (_source, resource, selection) => {
        const name = nameFor(resource);
        if (!models.has(name)) return false;
        activate(name);
        if (selection && 'startLineNumber' in selection) {
            editor.setSelection(selection); editor.revealRangeInCenter(selection);
        } else if (selection) { editor.setPosition(selection); editor.revealPositionInCenter(selection); }
        return true;
    }
});

function files() { return Object.fromEntries([...models].map(([name, model]) => [name, model.getValue()])); }
function changed() {
    if (loading) return;
    revision++;
    clearConversionUndo();
    if (!$('result').hidden || !$('runtime-error').hidden) {
        $('run-status').textContent = 'Edited — run again'; $('run-status').className = '';
    }
    for (const model of models.values()) monaco.editor.setModelMarkers(model, 'runtime', []);
    clearTimeout(saveTimer);
    $('save-state').textContent = 'Saving…';
    saveTimer = setTimeout(save, 300);
    scheduleDiagnostics();
    renderTabs();
}
function save() {
    if (!current) return;
    const state: SavedWorkspace = { version: 1, example: current.id, entry: $<HTMLSelectElement>('entry').value,
        active, files: files(), input: $<HTMLTextAreaElement>('input').value };
    try { localStorage.setItem(storageKey, JSON.stringify(state)); $('save-state').textContent = 'Saved locally'; }
    catch { $('save-state').textContent = 'Local save unavailable'; }
}
function addModel(name: string, source: string) {
    const model = monaco.editor.createModel(source, languageFor(name), uriFor(name));
    models.set(name, model);
    model.onDidChangeContent(changed);
}
function activate(name: string) {
    if (active) views.set(active, editor.saveViewState());
    active = name;
    editor.setModel(models.get(name)!);
    const view = views.get(name);
    if (view) editor.restoreViewState(view);
    $('active-file').textContent = name;
    editor.updateOptions({ ariaLabel: languageFor(name) === 'javascript' ? 'JavaScript code' : 'TypeScript code' });
    updateConversionControls();
    renderTabs();
    if (!loading) save();
    editor.focus();
}
function renderTabs() {
    const tabs = $('file-tabs');
    tabs.replaceChildren();
    for (const [name, model] of models) {
        const button = document.createElement('button');
        button.className = 'file-tab'; button.role = 'tab'; button.dataset.file = name;
        button.setAttribute('aria-selected', String(name === active));
        button.setAttribute('aria-label', name);
        const icon = document.createElement('span'); icon.className = 'file-icon'; icon.textContent = name.endsWith('.d.ts') ? 'D' : name.endsWith('.js') ? 'JS' : 'TS';
        button.append(icon, document.createTextNode(name));
        if (current?.files[name] !== model.getValue()) {
            const mark = document.createElement('span'); mark.className = 'dirty'; mark.textContent = '•'; button.append(mark);
        }
        button.onclick = () => activate(name);
        tabs.append(button);
    }
    const entry = $<HTMLSelectElement>('entry');
    const selected = entry.value || current?.entryFile;
    entry.replaceChildren(...[...models.keys()].filter(name => !name.endsWith('.d.ts')).map(name => new Option(name, name)));
    if ([...entry.options].some(option => option.value === selected)) entry.value = selected;
    $<HTMLButtonElement>('delete-file').disabled = models.size <= 1;
}
function load(example: Example, saved?: SavedWorkspace) {
    clearConversionUndo();
    $('conversion-status').hidden = true;
    runController?.abort();
    loading = true;
    editor.setModel(null);
    for (const model of models.values()) model.dispose();
    models.clear(); views.clear(); active = '';
    current = example;
    for (const [name, source] of Object.entries(saved?.files ?? example.files)) addModel(name, source);
    $('description').textContent = example.description;
    $<HTMLSelectElement>('examples').value = example.id;
    $<HTMLTextAreaElement>('input').value = saved?.input ?? JSON.stringify(example.input, null, 2);
    renderTabs();
    $<HTMLSelectElement>('entry').value = saved?.entry && models.has(saved.entry) ? saved.entry : example.entryFile;
    activate(saved?.active && models.has(saved.active) ? saved.active : $<HTMLSelectElement>('entry').value || models.keys().next().value!);
    clearResult();
    loading = false; revision++; save(); refreshLanguageService();
    $<HTMLButtonElement>('run').disabled = false;
}
function clearResult() {
    $('empty-result').hidden = false; $('result').hidden = true; $('runtime-error').hidden = true; $('logs-panel').hidden = true;
    $('run-status').textContent = 'Ready'; $('run-status').className = '';
}
function panel(name: 'result' | 'input' | 'problems') {
    for (const id of ['result', 'input', 'problems']) {
        $(`${id}-panel`).hidden = id !== name;
        $(`${id}-tab`).setAttribute('aria-selected', String(id === name));
    }
}
for (const name of ['result', 'input', 'problems'] as const) $(`${name}-tab`).onclick = () => panel(name);

function problemButton(message: string, file?: string, line?: number, column?: number, code?: string) {
    const button = document.createElement('button'); button.className = 'problem'; button.textContent = message;
    const location = document.createElement('span'); location.className = 'problem-location';
    location.textContent = [file && `${file}:${line ?? 1}:${column ?? 1}`, code].filter(Boolean).join(' · ');
    button.append(location);
    button.onclick = () => {
        if (!file || !models.has(file)) return;
        activate(file); const position = { lineNumber: line ?? 1, column: column ?? 1 };
        editor.setPosition(position); editor.revealPositionInCenter(position);
    };
    return button;
}
monaco.editor.onDidChangeMarkers(renderProblems);
function renderProblems() {
    const markers = monaco.editor.getModelMarkers({}).filter(marker => models.has(nameFor(marker.resource)));
    $('problem-count').textContent = String(markers.length);
    $('problems').replaceChildren(...markers.map(marker => problemButton(marker.message, nameFor(marker.resource),
        marker.startLineNumber, marker.startColumn, typeof marker.code === 'string' ? marker.code : marker.code?.value)));
    if (!markers.length) $('problems').textContent = 'No problems in your files.';
}

function scheduleDiagnostics() {
    clearTimeout(diagnosticsTimer);
    diagnosticsTimer = setTimeout(() => void validateDocuments(), 300);
}
function refreshLanguageService() {
    // Replacing a model can reuse its URI and version 1. Restart the language service so
    // TypeScript cannot reuse an AST cached for the previous file at that same version.
    for (const defaults of languageDefaults) defaults.setCompilerOptions(defaults.getCompilerOptions());
    $('language-status').textContent = 'Starting TypeScript…';
    scheduleDiagnostics();
}
function diagnosticText(text: typescript.Diagnostic['messageText']): string {
    return typeof text === 'string' ? text : [text.messageText, ...(text.next ?? []).map(diagnosticText)].join('\n');
}
async function validateDocuments() {
    if (!workerAccessor || loading) return;
    const checkedRevision = revision;
    const snapshot = [...models.values()];
    try {
        const resources = snapshot.map(model => model.uri);
        const worker = await workerAccessor(...resources);
        // Completion providers have separate workers. Sync both languages so JavaScript
        // callers see converted TypeScript dependencies (and their host declarations).
        if (snapshot.some(model => model.getLanguageId() === 'javascript'))
            await (await typescript.getJavaScriptWorker())(...resources);
        await Promise.all(snapshot.map(async model => {
            const name = model.uri.toString();
            const diagnostics = (await Promise.all([worker.getSyntacticDiagnostics(name), worker.getSemanticDiagnostics(name)])).flat();
            if (revision !== checkedRevision || model.isDisposed()) return;
            monaco.editor.setModelMarkers(model, 'typescript', diagnostics.map(diagnostic => {
                const start = model.getPositionAt(diagnostic.start ?? 0);
                const end = model.getPositionAt((diagnostic.start ?? 0) + Math.max(1, diagnostic.length ?? 1));
                return { message: diagnosticText(diagnostic.messageText), code: String(diagnostic.code),
                    severity: diagnostic.category === 1 ? monaco.MarkerSeverity.Error : monaco.MarkerSeverity.Warning,
                    startLineNumber: start.lineNumber, startColumn: start.column, endLineNumber: end.lineNumber, endColumn: end.column };
            }));
        }));
        if (revision === checkedRevision) {
            $('language-status').textContent = `TypeScript ${typescript.typescriptVersion}`;
            renderProblems();
        }
    } catch (error) {
        if (checkedRevision === revision) { $('language-status').textContent = 'Type checking unavailable'; toast(String(error)); }
    }
}

async function run() {
    if (runController) { runController.abort(); return; }
    let input: unknown;
    try { input = JSON.parse($<HTMLTextAreaElement>('input').value); }
    catch { panel('input'); toast('Enter valid JSON before running.'); $<HTMLTextAreaElement>('input').focus(); return; }
    const snapshot = files();
    if (Object.values(snapshot).some(text => text.length > 100_000) || Object.values(snapshot).reduce((sum, text) => sum + text.length, 0) > 200_000) {
        toast('Use at most 100,000 characters per file and 200,000 across the workspace.'); return;
    }
    save(); panel('result');
    for (const model of models.values()) monaco.editor.setModelMarkers(model, 'runtime', []);
    const controller = new AbortController(); runController = controller;
    const runRevision = revision;
    $('run').textContent = '■ Stop'; $('run-status').textContent = 'Running…'; $('run-status').className = '';
    try {
        const response = await fetch('/api/run', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Playground': '1' },
            body: JSON.stringify({ files: snapshot, entryFile: $<HTMLSelectElement>('entry').value, input }), signal: controller.signal });
        if (!response.ok) throw new Error(`Execution request failed (${response.status}).`);
        const result = await response.json() as RunResponse;
        if (runRevision !== revision) { $('run-status').textContent = 'Files changed — run again'; return; }
        $('empty-result').hidden = true;
        $('result').hidden = !result.success; $('runtime-error').hidden = result.success;
        $('result').textContent = JSON.stringify(result.result, null, 2);
        $('run-status').textContent = result.success ? `Completed · ${result.elapsedMilliseconds} ms` : 'Execution failed';
        $('run-status').className = result.success ? 'success' : 'error';
        $('logs-panel').hidden = result.logs.length === 0; $('logs').textContent = result.logs.join('\n');
        if (result.diagnostic) {
            const error = result.diagnostic;
            const box = $('runtime-error'); box.replaceChildren(problemButton(error.message, error.file, error.line, error.column, error.code));
            if (error.stack) { const stack = document.createElement('pre'); stack.textContent = error.stack.replaceAll(root, ''); box.append(stack); }
            const model = error.file && models.get(error.file);
            if (model && error.line) monaco.editor.setModelMarkers(model, 'runtime', [{ severity: monaco.MarkerSeverity.Error,
                message: error.message, code: error.code, startLineNumber: error.line, endLineNumber: error.line,
                startColumn: error.column ?? 1, endColumn: (error.column ?? 1) + 1 }]);
        }
    } catch (error) {
        if (controller.signal.aborted) $('run-status').textContent = 'Canceled';
        else { toast(error instanceof Error ? error.message : String(error)); $('run-status').textContent = 'Connection failed'; }
    } finally {
        if (runController === controller) { runController = undefined; $('run').innerHTML = '▶ Run <kbd>Ctrl ↵</kbd>'; }
    }
}
function toast(message: string) {
    $('toast').textContent = message; $('toast').hidden = false;
    setTimeout(() => $('toast').hidden = true, 6000);
}
async function confirmAction(title: string, description: string) {
    const dialog = $<HTMLDialogElement>('confirm-dialog');
    $('confirm-title').textContent = title; $('confirm-description').textContent = description;
    dialog.returnValue = ''; dialog.showModal();
    return new Promise<boolean>(resolve => dialog.addEventListener('close', () => resolve(dialog.returnValue === 'confirm'), { once: true }));
}
function fileDialog(rename = false) {
    if (!rename && models.size >= 32) { toast('The workspace supports up to 32 files.'); return; }
    renameFrom = rename ? active : undefined;
    $('file-dialog-title').textContent = rename ? 'Rename file' : 'Add a file';
    $('save-file').textContent = rename ? 'Rename' : 'Add file';
    $('file-error').textContent = '';
    $<HTMLInputElement>('filename').value = rename ? active : '';
    $<HTMLDialogElement>('file-dialog').showModal();
    $<HTMLInputElement>('filename').focus();
}
$('add-file').onclick = () => fileDialog();
$('rename-file').onclick = () => fileDialog(true);
$('cancel-file').onclick = () => $<HTMLDialogElement>('file-dialog').close();
$('file-form').onsubmit = event => {
    event.preventDefault();
    const name = $<HTMLInputElement>('filename').value.trim();
    if (!/\.(ts|js)$/.test(name) || /[\\:\x00-\x1f]/.test(name) || name.split('/').some(part => !part || part === '.' || part === '..')) {
        $('file-error').textContent = 'Use a relative .ts or .js filename, such as lib/helper.ts.'; return;
    }
    if (models.has(name) && name !== renameFrom) { $('file-error').textContent = 'A file with this name already exists.'; return; }
    if (name !== renameFrom) {
        const text = renameFrom ? models.get(renameFrom)!.getValue() : name.endsWith('.d.ts')
            ? 'interface Example {\n    value: number;\n}\n' : name.endsWith('.js') ? '/** @param {number} value */\nexport function helper(value) {\n    return value * 2;\n}\n' : 'export function helper(value: number): number {\n    return value * 2;\n}\n';
        addModel(name, text);
        if (renameFrom) {
            const old = models.get(renameFrom)!; models.delete(renameFrom); old.dispose();
            if ($<HTMLSelectElement>('entry').value === renameFrom) { renderTabs(); $<HTMLSelectElement>('entry').value = name; }
            toast(`Renamed ${renameFrom}. Update imports that use the old filename.`);
        }
        activate(name); changed(); refreshLanguageService();
    }
    $<HTMLDialogElement>('file-dialog').close();
};
$('delete-file').onclick = async () => {
    const name = active;
    if (models.size <= 1 || !await confirmAction(`Delete ${name}?`, 'Imports that reference this file will need to be updated.')) return;
    const model = models.get(name)!; models.delete(name); model.dispose();
    activate(models.keys().next().value!); changed(); refreshLanguageService();
};

function workspaceSnapshot(): SavedWorkspace {
    return { version: 1, example: current.id, entry: $<HTMLSelectElement>('entry').value,
        active, files: files(), input: $<HTMLTextAreaElement>('input').value };
}
function updateConversionControls() {
    $('file-language').textContent = active.endsWith('.js') ? 'JavaScript · JSDoc' : 'TypeScript';
    $('convert-file').hidden = !active.endsWith('.js');
    $<HTMLButtonElement>('convert-file').disabled = converting;
    $('convert-file').textContent = converting ? 'Converting…' : 'Convert to TypeScript';
    $('undo-conversion').hidden = !conversionUndo;
    canUndoConversion.set(!!conversionUndo);
}
function clearConversionUndo() { conversionUndo = undefined; updateConversionControls(); }
function undoConversion() {
    if (!conversionUndo) return;
    const before = conversionUndo;
    load(current, before);
    toast('Conversion undone. JavaScript and JSDoc restored.');
}
$('undo-conversion').onclick = undoConversion;
$('convert-file').onclick = async () => {
    if (converting || !active.endsWith('.js')) return;
    converting = true; updateConversionControls();
    $('conversion-status').hidden = true;
    const before = workspaceSnapshot(), startedRevision = revision;
    try {
        const result = await convertJsDoc(before.files, before.active);
        if (startedRevision !== revision || active !== before.active) {
            toast('The workspace changed during conversion. Your edits were kept; try again.'); return;
        }
        if (!result.success) {
            $('conversion-status').replaceChildren(...result.issues.map(issue => problemButton(issue.message, issue.line ? before.active : undefined, issue.line, issue.column)));
            $('conversion-status').hidden = false; return;
        }
        load(current, { ...before, files: result.files, active: result.file,
            entry: before.entry === before.active ? result.file : before.entry });
        conversionUndo = before;
        toast('Converted to TypeScript. Imports updated. Undo is available until your next edit.');
    } finally { converting = false; updateConversionControls(); }
};

$('format-file').onclick = () => void editor.getAction('editor.action.formatDocument')?.run();
$('run').onclick = () => void run();
$('entry').onchange = changed;
$('input').oninput = changed;
$('reset').onclick = async () => { if (await confirmAction('Reset this example?', 'Your local edits will be replaced with the original example.')) load(current); };
$('examples').onchange = async () => {
    const next = examples.find(example => example.id === $<HTMLSelectElement>('examples').value)!;
    const edited = JSON.stringify(files()) !== JSON.stringify(current.files) || $<HTMLTextAreaElement>('input').value !== JSON.stringify(current.input, null, 2);
    if (edited && !await confirmAction('Open another example?', 'This will replace the current files and input, including your local edits.')) {
        $<HTMLSelectElement>('examples').value = current.id; return;
    }
    load(next);
};
window.addEventListener('pagehide', save);
document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter' && !event.defaultPrevented) { event.preventDefault(); void run(); }
});

try {
    const response = await fetch('/api/examples');
    if (!response.ok) throw new Error('Could not load the examples. Restart the sample and try again.');
    examples = await response.json();
    $<HTMLSelectElement>('examples').replaceChildren(...examples.map(example => new Option(example.name, example.id)));
    let saved: SavedWorkspace | undefined;
    try {
        const value = JSON.parse(localStorage.getItem(storageKey) ?? 'null') as SavedWorkspace | null;
        if (value?.version === 1 && typeof value.input === 'string' && value.files && Object.keys(value.files).length <= 32
            && Object.keys(value.files).length > 0 && Object.entries(value.files).every(([name, text]) =>
                /\.(ts|js)$/.test(name) && name.length <= 240 && !/[\\:\x00-\x1f]/.test(name)
                && name.split('/').every(part => part && part !== '.' && part !== '..') && typeof text === 'string' && text.length <= 100_000)
            && Object.values(value.files).reduce((sum, text) => sum + text.length, 0) <= 200_000 && value.files[value.entry]) saved = value;
    } catch { /* A stale or unavailable local workspace does not prevent loading the examples. */ }
    load(examples.find(example => example.id === saved?.example) ?? examples[0], saved);
    // Monaco activates language services lazily after attaching a model. Its worker accessor
    // can briefly reject while that activation finishes, even though the language is registered.
    const worker = await (async () => {
        for (let attempt = 0; ; attempt++) {
            try { return await typescript.getTypeScriptWorker(); }
            catch (error) {
                if (attempt >= 19) throw error;
                await new Promise(resolve => setTimeout(resolve, 50));
            }
        }
    })();
    workerAccessor = worker;
    scheduleDiagnostics();
} catch (error) {
    toast(error instanceof Error ? error.message : String(error));
    $('language-status').textContent = 'Editor service unavailable';
}
