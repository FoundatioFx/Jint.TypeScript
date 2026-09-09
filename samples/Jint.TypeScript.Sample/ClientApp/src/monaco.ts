// The TypeScript worker loader imports Monaco's editor contributions lazily. Register them
// before the first editor initializes its service collection, including embedded peek editors.
// This entry point includes editor features without unrelated language packs or the LSP client.
import 'monaco-editor/features/register.all.js';
export * from 'monaco-editor/editor/editor.api.js';
import 'monaco-editor/editor/browser/coreCommands.js';
import 'monaco-editor/editor/contrib/caretOperations/browser/caretOperations.js';
import 'monaco-editor/editor/contrib/dropOrPasteInto/browser/copyPasteContribution.js';
import 'monaco-editor/editor/contrib/find/browser/findController.js';
import 'monaco-editor/editor/contrib/gotoError/browser/markerSelectionStatus.js';
import 'monaco-editor/editor/contrib/gotoSymbol/browser/goToCommands.js';
import 'monaco-editor/editor/contrib/semanticTokens/browser/documentSemanticTokens.js';
import 'monaco-editor/editor/contrib/suggest/browser/suggestController.js';
