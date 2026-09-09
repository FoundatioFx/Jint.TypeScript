using Acornima.Ast;
using Jint.TypeScript.Parsing.Helpers;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private bool TryParseInlineTypeSpecifier(bool import)
    {
        if (!IsInlineTypeSpecifier()) return false;
        Next();
        ParseErasedSpecifier(import, inline: true);
        return true;
    }

    private bool _erasedModuleSyntax;
    private bool _erasedExportRequiresSource;

    // Only statement lists call this: type declarations cannot be an if/loop body.
    private bool TryParseErasedDeclaration(bool topLevel = false)
    {
        CheckUnsupportedDeclaration(topLevel);
        if (IsAmbientDeclarationStart())
        {
            ParseAmbientDeclaration();
            return true;
        }
        if (IsTypeDeclarationStart())
        {
            ParseTypeDeclaration();
            return true;
        }
        if (!topLevel || !_inModule) return false;
        if (_tokenizer._type == TokenType.Import && IsWholeTypeImport())
        {
            Next(); // import
            Next(); // type
            ParseWholeTypeImport();
        }
        else if (_tokenizer._type == TokenType.Export && IsTypeExport())
        {
            _erasedExportRequiresSource = false;
            Next(); // export
            if (IsAmbientDeclarationStart()) ParseAmbientDeclaration();
            else if (Eat(TokenType.Default))
            {
                if (!IsContextual("interface")) TypeScriptError("UnsupportedTypeExport", "Expected an interface");
                ParseTypeDeclaration();
            }
            else if (IsTypeDeclarationStart()) ParseTypeDeclaration();
            else
            {
                ExpectContextual("type");
                if (Eat(TokenType.Star))
                {
                    if (EatContextual("as")) ReadTypeModuleName();
                    ParseTypeModuleSource();
                }
                else
                {
                    ParseWholeTypeSpecifiers(import: false);
                    if (IsContextual("from")) ParseTypeModuleSource();
                    else CheckErasedExportSource();
                }
                Semicolon();
            }
        }
        else return false;
        _erasedModuleSyntax = true;
        return true;
    }

    private bool IsTypeDeclarationStart()
    {
        if (!IsContextual("type") && !IsContextual("interface")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            return !probe.CanInsertSemicolon() && probe._tokenizer._type == TokenType.Name;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseTypeDeclaration()
    {
        var isInterface = IsContextual("interface");
        var wasInType = _tokenizer.InType;
        _tokenizer.InType = true;
        try
        {
            Next();
            ReadTypeIdentifier();
            ParseTypeParameters(allowVariance: true, allowConst: isInterface);
            if (isInterface)
            {
                if (Eat(TokenType.Extends))
                {
                    do { ParseInterfaceBase(); } while (Eat(TokenType.Comma));
                }
                ParseObjectType(allowMapped: false);
            }
            else
            {
                Expect(TokenType.Eq);
                ParseType();
                Semicolon();
            }
        }
        finally { _tokenizer.InType = wasInType; }
    }

    private void ParseInterfaceBase()
    {
        ReadTypeIdentifier();
        while (Eat(TokenType.Dot)) ReadTypeIdentifier();
        if (!IsTypeOperator("<")) return;
        Next();
        ParseType();
        while (Eat(TokenType.Comma))
        {
            if (IsTypeOperator(">")) break;
            ParseType();
        }
        if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after interface base type arguments");
        Next();
    }

    private bool IsWholeTypeImport()
    {
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            if (!probe.EatContextual("type")) return false;
            if (probe.EatContextual("from")) return probe.IsContextual("from");
            return probe._tokenizer._type == TokenType.BraceLeft || probe._tokenizer._type == TokenType.Star
                || probe._tokenizer._type == TokenType.Name;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private bool IsTypeExport()
    {
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            probe.CheckUnsupportedDeclaration();
            if (probe.Eat(TokenType.Default)) return probe.IsContextual("interface");
            return probe.IsContextual("type") || probe.IsContextual("interface") || probe.IsAmbientDeclarationStart();
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseWholeTypeImport()
    {
        if (_tokenizer._type == TokenType.BraceLeft) ParseWholeTypeSpecifiers(import: true);
        else if (Eat(TokenType.Star))
        {
            ExpectContextual("as");
            ReadTypeImportBinding();
        }
        else ReadTypeImportBinding();
        ParseTypeModuleSource();
        Semicolon();
    }

    private void ParseTypeModuleSource()
    {
        ExpectContextual("from");
        Expect(TokenType.String);
        _erasedExportRequiresSource = false;
        // Import attributes on type-only declarations are outside this milestone.
    }

    private void ParseWholeTypeSpecifiers(bool import)
    {
        Expect(TokenType.BraceLeft);
        while (!Eat(TokenType.BraceRight))
        {
            if (IsInlineTypeSpecifier()) TypeScriptError("RedundantTypeModifier", "A type-only declaration cannot contain another type modifier");
            ParseErasedSpecifier(import);
            if (Eat(TokenType.BraceRight)) break;
            Expect(TokenType.Comma);
        }
    }

    private void ReadTypeModuleName()
    {
        if (_tokenizer._type != TokenType.Name && _tokenizer._type.Keyword is null && _tokenizer._type != TokenType.String)
            TypeScriptError("ExpectedModuleName", "Expected an import or export name");
        if (_tokenizer._type == TokenType.String && ((string)_tokenizer._value.Value!).AsSpan().ContainsLoneSurrogate())
            TypeScriptError("InvalidModuleName", "A module name cannot contain an unpaired surrogate");
        Next(ignoreEscapeSequenceInKeyword: true);
    }

    private void ReadTypeImportBinding()
    {
        // Validate a local binding without registering a runtime variable or node.
        if (_tokenizer._type != TokenType.Name) TypeScriptError("ExpectedImportBinding", "Expected a type import binding");
        var name = (string)_tokenizer._value.Value!;
        if (name is "await" or "yield" or "eval" or "arguments" || _isReservedWordBind(name, _strict))
            TypeScriptError("InvalidImportBinding", "Invalid type import binding");
        Next();
    }

    private void ParseErasedSpecifier(bool import, bool inline = false)
    {
        if (inline && _tokenizer._type == TokenType.String)
            TypeScriptError("UnsupportedInlineTypeName", "Use a whole type-only declaration for quoted module names");
        if (!import && (_tokenizer._type == TokenType.String || _tokenizer._type.Keyword is not null))
            _erasedExportRequiresSource = true;
        // Check an unaliased import as a binding; aliased names can be keywords or strings.
        var probe = StartLookahead();
        bool aliased;
        try { probe.Next(); probe.Next(); aliased = probe.IsContextual("as"); }
        finally { _tokenCount = probe._tokenCount; }
        if (import && !aliased) ReadTypeImportBinding();
        else
        {
            ReadTypeModuleName();
            if (EatContextual("as"))
            {
                if (import) ReadTypeImportBinding();
                else ReadTypeModuleName();
            }
        }
    }

    private void CheckErasedExportSource()
    {
        if (_erasedExportRequiresSource)
            TypeScriptError("ExpectedExportSource", "A quoted or keyword export binding requires a 'from' clause");
    }

    private bool IsInlineTypeSpecifier()
    {
        if (!IsContextual("type")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            if (probe._tokenizer._type == TokenType.Comma || probe._tokenizer._type == TokenType.BraceRight) return false;
            if (!probe.IsContextual("as")) return true;
            probe.Next();
            // `type as Foo` imports the value named type; `type as` erases the
            // type named as, and `type as as Foo` erases its alias.
            if (probe._tokenizer._type == TokenType.Comma || probe._tokenizer._type == TokenType.BraceRight) return true;
            if (!probe.EatContextual("as")) return false;
            return probe._tokenizer._type != TokenType.Comma && probe._tokenizer._type != TokenType.BraceRight;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void PreserveErasedModule(ref ArrayList<Statement> body)
    {
        if (!_erasedModuleSyntax) return;
        for (var i = 0; i < body.Count; i++)
            if (body[i] is ImportDeclaration or ExportDeclaration) return;
        // Preserve module identity when callers serialize the official AST to JS.
        var marker = StartNode(); // EOF: synthetic marker has an empty original range.
        body.Add(FinishNodeAt(marker, marker, new ExportNamedDeclaration(null, default, null, default)
        { Range = NodeRange(marker, marker), Location = NodeLocation(marker, marker) }));
    }
}
