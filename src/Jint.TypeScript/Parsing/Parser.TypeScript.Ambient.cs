namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private bool IsAmbientDeclarationStart()
    {
        if (!IsContextual("declare")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            return !probe.CanInsertSemicolon() && (probe._tokenizer._type == TokenType.Function
                || probe._tokenizer._type == TokenType.Var || probe._tokenizer._type == TokenType.Const
                || probe.IsContextual("let") || probe.IsContextual("type") || probe.IsContextual("interface"));
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseAmbientDeclaration()
    {
        ExpectContextual("declare");
        if (IsTypeDeclarationStart()) { ParseTypeDeclaration(); return; }
        if (_tokenizer._type == TokenType.Function)
        {
            var marker = StartNode();
            Next();
            ParseFunction(marker, FunctionOrClassFlags.Statement, ambient: true);
            return;
        }
        var isConst = _tokenizer._type == TokenType.Const;
        var lexical = _tokenizer._type != TokenType.Var;
        Next(); // var, let or const
        do
        {
            ReadAmbientBinding(lexical);
            var annotated = ParseTypeAnnotation();
            if (Eat(TokenType.Eq))
            {
                if (!isConst || annotated)
                    TypeScriptError("AmbientInitializer", "Only an unannotated ambient const can have a literal initializer");
                var negative = _tokenizer._type == TokenType.PlusMinus && Equals(_tokenizer._value.Value, "-");
                if (negative) Next();
                var kind = _tokenizer._type.Kind;
                if (kind is not (TokenKind.NumericLiteral or TokenKind.BigIntLiteral)
                    && (negative || kind is not (TokenKind.StringLiteral or TokenKind.BooleanLiteral)))
                    TypeScriptError("UnsupportedAmbientInitializer", "Expected a string, boolean or numeric ambient literal");
                Next();
            }
        } while (Eat(TokenType.Comma));
        Semicolon();
    }

    private void ReadAmbientBinding(bool lexical)
    {
        // Ordinary names need neither an AST node nor a runtime binding. Keep
        // escaped/reserved/context-sensitive names on the original validation path.
        if (_tokenizer._type == TokenType.Name && !_tokenizer._containsEscape)
        {
            var name = (string)_tokenizer._value.Value!;
            if (name is not ("await" or "yield" or "arguments" or "let") && !_isReservedWordBind(name.AsSpan(), _strict))
            {
                Next();
                return;
            }
        }
        var id = ParseIdentifier();
        CheckLValSimple(id, BindingType.Outside);
        if (lexical && id.Name == "let") TypeScriptError("InvalidAmbientBinding", "A lexical binding cannot be named 'let'");
    }
}
