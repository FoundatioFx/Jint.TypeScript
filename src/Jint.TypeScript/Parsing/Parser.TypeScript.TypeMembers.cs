using Jint.TypeScript.Parsing.Helpers;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private enum TypeAccessorKind { None, Get, Set }

    private bool IsTypeAccessorStart()
    {
        if (!IsContextual("get") && !IsContextual("set")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            return probe.IsTypeMemberName() || probe._tokenizer._type == TokenType.BracketLeft;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private bool IsTypeMemberName() => !_tokenizer._containsEscape &&
        (_tokenizer._type == TokenType.Name || _tokenizer._type.Keyword is not null ||
         _tokenizer._type == TokenType.String || _tokenizer._type == TokenType.Number);

    private void ParseTypeMemberName()
    {
        if (_tokenizer._type == TokenType.BracketLeft) ParseTypeComputedName();
        else
        {
            if (!IsTypeMemberName())
                TypeScriptError("UnsupportedTypeMember", "Expected a property, method, accessor or index signature");
            Next(ignoreEscapeSequenceInKeyword: true);
        }
    }

    // Named symbols, qualified names and literal keys cover useful type members
    // without entering JS scopes or allocating expression nodes. Arbitrary value
    // expressions deliberately require a named key instead of a second parser.
    private bool ParseTypeComputedName(bool allowIndexSignature = false)
    {
        Expect(TokenType.BracketLeft);
        var reference = _tokenizer._type == TokenType.Name || _tokenizer._type == TokenType.This;
        if (reference)
        {
            var name = _tokenizer._type == TokenType.Name;
            var escaped = _tokenizer._containsEscape;
            var valueName = name ? (string)_tokenizer._value.Value! : null;
            Next();
            // Consume the shared '[name' prefix once. Index signatures need no
            // computed-expression nesting or a second tokenization of every key.
            if (allowIndexSignature && name && Eat(TokenType.Colon))
            {
                if (escaped) TypeScriptError("ExpectedTypeName", "Expected an unescaped index signature name");
                ParseType();
                Expect(TokenType.BracketRight);
                Expect(TokenType.Colon);
                ParseType();
                return true;
            }
            if (escaped || (valueName is not null && _isReservedWord(valueName.AsSpan(), _strict)))
                TypeScriptError("UnsupportedComputedTypeKey", "Expected an unescaped value name in a computed type key");
        }
        else
        {
            var negative = _tokenizer._type == TokenType.PlusMinus && Equals(_tokenizer._value.Value, "-");
            if (negative) Next();
            if (_tokenizer._type != TokenType.Number && _tokenizer._type != TokenType.BigInt &&
                (negative || (_tokenizer._type != TokenType.String && _tokenizer._type != TokenType.Null &&
                    _tokenizer._type != TokenType.True && _tokenizer._type != TokenType.False)))
                TypeScriptError("UnsupportedComputedTypeKey", "Use a named symbol, qualified name or literal for a computed type key");
            Next();
        }
        EnterTypeMemberNesting();
        try
        {
            while (reference)
            {
                if (Eat(TokenType.Dot))
                {
                    if (_tokenizer._containsEscape || (_tokenizer._type != TokenType.Name && _tokenizer._type.Keyword is null))
                        TypeScriptError("UnsupportedComputedTypeKey", "Expected a qualified value name in a computed type key");
                    Next(ignoreEscapeSequenceInKeyword: true);
                }
                else if (_tokenizer._type == TokenType.BracketLeft) ParseTypeComputedName();
                else break;
            }
            if (_tokenizer._type != TokenType.BracketRight)
                TypeScriptError("UnsupportedComputedTypeKey", "Use a named key instead of an expression in a computed type member");
            Next();
            return false;
        }
        finally { _typeDepth--; }
    }

    // Function-type bindings describe parameters but must never declare runtime
    // names. Consume nested patterns directly and leave default values excluded.
    private void ParseTypeBinding()
    {
        if (_tokenizer._type != TokenType.BraceLeft && _tokenizer._type != TokenType.BracketLeft)
        {
            ReadTypeIdentifier();
            return;
        }
        EnterTypeMemberNesting();
        try
        {
            var array = _tokenizer._type == TokenType.BracketLeft;
            var close = array ? TokenType.BracketRight : TokenType.BraceRight;
            Next();
            while (!Eat(close))
            {
                if (array && Eat(TokenType.Comma)) continue;
                var rest = Eat(TokenType.Ellipsis);
                if (rest && !array) ReadTypeIdentifier();
                else if (array) ParseTypeBinding();
                else
                {
                    var shorthand = _tokenizer._type == TokenType.Name;
                    ParseTypeMemberName();
                    if (Eat(TokenType.Colon)) ParseTypeBinding();
                    else if (!shorthand) TypeScriptError("ExpectedTypeBinding", "Expected a binding name after the property name");
                }
                if (_tokenizer._type == TokenType.Eq)
                    TypeScriptError("SignatureInitializer", "A type signature binding cannot have an initializer");
                if (Eat(close)) break;
                if (rest) TypeScriptError("ParameterAfterRest", "A rest binding must be last and cannot have a trailing comma");
                Expect(TokenType.Comma);
            }
        }
        finally { _typeDepth--; }
    }

    private void EnterTypeMemberNesting()
    {
        if (++_typeDepth > Limits.MaxTypeDepth) TypeScriptError("TypeDepthLimit", "Type nesting limit exceeded");
        try { StackGuard.EnsureSufficientExecutionStack(_typeDepth); }
        catch (InsufficientExecutionStackException) { TypeScriptError("TypeDepthLimit", "Insufficient stack for type nesting"); }
    }

    // ({x}: Shape) => T and ([x]: Tuple) => T share their prefix with grouped
    // object/tuple types. Scan one balanced pattern without speculative ASTs or
    // throwing on ordinary grouped types. Charge every token to the same budget.
    private bool SkipTypeBindingLookahead()
    {
        // Tokenizer contexts also depend on matching delimiters. Keep the common
        // case on the stack, and grow only when callers permit unusually deep types.
        Span<byte> delimiters = stackalloc byte[64];
        var depth = 0;
        do
        {
            var type = _tokenizer._type;
            if (type == TokenType.BraceLeft || type == TokenType.BracketLeft || type == TokenType.ParenLeft || type == TokenType.DollarBraceLeft)
            {
                if (depth >= Limits.MaxTypeDepth)
                    TypeScriptError("TypeDepthLimit", "Type binding lookahead nesting limit exceeded");
                if (depth == delimiters.Length)
                {
                    var larger = new byte[Math.Min(delimiters.Length * 2, Limits.MaxTypeDepth)];
                    delimiters.CopyTo(larger);
                    delimiters = larger;
                }
                delimiters[depth++] = type == TokenType.BracketLeft ? (byte)2 : type == TokenType.ParenLeft ? (byte)3 : (byte)1;
            }
            else if (type == TokenType.BraceRight || type == TokenType.BracketRight || type == TokenType.ParenRight)
            {
                var expected = type == TokenType.BracketRight ? 2 : type == TokenType.ParenRight ? 3 : 1;
                if (delimiters[--depth] != expected) return false;
            }
            else if (type == TokenType.EOF) return false;
            Next();
        } while (depth != 0);
        return true;
    }
}
