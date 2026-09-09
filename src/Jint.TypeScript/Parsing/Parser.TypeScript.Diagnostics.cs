using Acornima.Ast;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    // Recognize declaration heads only where the parser expects a statement list item.
    // Do not scan source text for keywords: comments, literals and property names are ordinary JS.
    private void CheckUnsupportedDeclaration(bool topLevel = false)
    {
        var type = _tokenizer._type;
        if (topLevel && !_inModule && (type == TokenType.Import || type == TokenType.Export))
        {
            var next = _tokenizer.NextTokenPosition(out _, out _);
            if (type == TokenType.Export || _tokenizer.CharCodeAt(next) is not ('(' or '.'))
                TypeScriptError("ModuleSyntaxInScript", "Import and export declarations require module mode. Use ParseModule or PrepareModule instead of ParseScript or PrepareScript.");
            return;
        }

        if (type == TokenType.Const)
        {
            // Ordinary const declarations must not allocate or tokenize a lookahead.
            var next = _tokenizer.NextTokenPosition(out _, out _);
            var after = _tokenizer.CharCodeAt(next + 4);
            if (!_tokenizer._input.AsSpan(next).StartsWith("enum", StringComparison.Ordinal)
                || Tokenizer.IsIdentifierChar(after, allowAstral: false)
                // Surrogates cannot separate enum from its declaration name.
                || char.IsSurrogate((char)after) || after == '\\') return;
        }
        else if (type != TokenType.Name || _tokenizer._containsEscape
            || (string)_tokenizer._value.Value! is not ("enum" or "namespace" or "module")) return;

        var probe = StartLookahead();
        try
        {
            probe.Next();
            if (probe._tokenizer._type == TokenType.Const) probe.Next();
            var marker = probe.StartNode();
            if (probe.IsContextual("enum"))
            {
                probe.Next();
                if (probe._tokenizer._type != TokenType.Name) return;
                probe.Next();
                if (probe._tokenizer._type == TokenType.BraceLeft)
                    throw new TypeScriptParseException("UnsupportedEnum",
                        "TypeScript enums are not supported. Jint.TypeScript erases types but does not generate or inline enum values. Use an object with 'as const' and a union type instead.",
                        marker.Index, marker.Position, _tokenizer._sourceFile);
            }
            else if (probe.IsContextual("namespace") || probe.IsContextual("module"))
            {
                var quotedName = probe.IsContextual("module");
                probe.Next();
                if (probe.CanInsertSemicolon() || (probe._tokenizer._type != TokenType.Name
                    && !(quotedName && probe._tokenizer._type == TokenType.String))) return;
                probe.Next();
                if (probe._tokenizer._type == TokenType.BraceLeft || probe._tokenizer._type == TokenType.Dot)
                    throw new TypeScriptParseException("UnsupportedNamespace",
                        "TypeScript namespaces and ambient module blocks are not supported. Use separate files with ES module imports and exports instead.",
                        marker.Index, marker.Position, _tokenizer._sourceFile);
            }
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void CheckUnsupportedAmbientDeclaration()
    {
        // Reuse the existing probe after declare, without rescanning supported declarations.
        if (IsContextual("abstract")) Next();
        if (_tokenizer._type == TokenType.Class)
            TypeScriptError("UnsupportedAmbientClass", "Ambient 'declare class' declarations are not supported. Describe host values with an interface and a 'declare const' binding, or keep class declarations in an editor-only .d.ts file.");
        CheckUnsupportedDeclaration();
    }

    private Node ParseTypeScriptBindingAtom(bool constructorParameter)
    {
        if (constructorParameter) CheckUnsupportedParameterProperty();
        return ParseBindingAtom();
    }

    private void CheckUnsupportedParameterProperty()
    {
        if (_tokenizer._type != TokenType.Name || _tokenizer._containsEscape
            || (string)_tokenizer._value.Value! is not ("public" or "private" or "protected" or "readonly" or "override")) return;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            // readonly: T / readonly = value are ordinary parameters named readonly.
            if (probe._tokenizer._type == TokenType.Name)
                TypeScriptError("UnsupportedParameterProperty", "Constructor parameter properties are not supported. Declare the field separately and assign it explicitly with 'this.name = name' in the constructor.");
        }
        finally { _tokenCount = probe._tokenCount; }
    }
}
