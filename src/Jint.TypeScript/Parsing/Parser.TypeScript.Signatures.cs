using Acornima.Ast;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private Statement ParseStatement(StatementContext context)
    {
        var statement = ParseStatementListItem(context);
        if (statement is null) TypeScriptError("InvalidOverloadContext", "A signature requires a statement list");
        return statement!;
    }

    private bool TryParseSignatureWithoutBody(in NodeList<Node> parameters, bool allowed, PropertyKind? abstractKind = null)
    {
        if (abstractKind is { } kind) CheckAbstractSignature(parameters, kind);
        if (!allowed || _tokenizer._type == TokenType.BraceLeft) return false;
        // Parse real binding grammar once, including destructuring and rest. The
        // temporary parameter nodes are discarded; no function/body node is built.
        for (var i = 0; i < parameters.Count; i++)
            if (parameters[i] is AssignmentPattern)
                TypeScriptError("SignatureInitializer", "An overload parameter cannot have an initializer");
        CheckParams(parameters, allowDuplicates: false);
        Semicolon();
        ExitScope();
        return true;
    }

    private void ParseReturnType(bool allowConditional = true)
    {
        // Only return positions admit predicates. The cheap character check keeps
        // ordinary return annotations off the tokenizer lookahead path.
        if (_tokenizer._type == TokenType.Name || _tokenizer._type == TokenType.This)
        {
            var asserts = IsContextual("asserts");
            var next = _tokenizer.NextTokenPosition(out _, out _);
            if (asserts || _tokenizer.CharCodeAt(next) == 'i')
            {
                var probe = StartLookahead();
                bool predicate;
                try
                {
                    probe.Next();
                    probe.Next();
                    predicate = asserts
                        ? !probe.CanInsertSemicolon() && (probe._tokenizer._type == TokenType.Name || probe._tokenizer._type == TokenType.This)
                        : probe.IsContextual("is") && !probe.CanInsertSemicolon();
                }
                finally { _tokenCount = probe._tokenCount; }
                if (predicate)
                {
                    if (asserts) Next();
                    if (!Eat(TokenType.This)) ReadTypeIdentifier();
                    if (!CanInsertSemicolon() && EatContextual("is")) ParseType(allowConditional);
                    else if (!asserts) TypeScriptError("ExpectedPredicateType", "Expected a predicate type");
                    return;
                }
            }
        }
        ParseType(allowConditional);
    }

    private void ParseTypeQuery()
    {
        Expect(TokenType.TypeOf);
        if (_tokenizer._type == TokenType.Import)
        {
            ParseImportType();
            return;
        }
        if (!Eat(TokenType.This)) ReadTypeIdentifier();
        while (Eat(TokenType.Dot))
        {
            if (_tokenizer._containsEscape || (_tokenizer._type != TokenType.Name && _tokenizer._type.Keyword is null))
                TypeScriptError("ExpectedTypeQueryName", "Expected a qualified value name");
            Next(ignoreEscapeSequenceInKeyword: true);
        }
        if (IsTypeOperator("<") && !CanInsertSemicolon())
        {
            if (_tokenizer.CharCodeAt(_tokenizer._start + 1) == '<')
                TypeScriptError("AmbiguousTypeQueryArguments", "Parenthesize a generic function type in a type query argument");
            Next();
            ParseType();
            while (Eat(TokenType.Comma))
            {
                if (IsTypeOperator(">")) break;
                ParseType();
            }
            if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after type query arguments");
            Next();
        }
    }

    private void ParseImportType()
    {
        Expect(TokenType.Import);
        Expect(TokenType.ParenLeft);
        Expect(TokenType.String);
        // Import attributes have their own grammar and remain outside this slice.
        // The erased string never becomes a module dependency or runtime import.
        Expect(TokenType.ParenRight);
        while (Eat(TokenType.Dot))
        {
            if (_tokenizer._containsEscape || (_tokenizer._type != TokenType.Name && _tokenizer._type.Keyword is null))
                TypeScriptError("ExpectedImportTypeName", "Expected a qualified import type name");
            Next(ignoreEscapeSequenceInKeyword: true);
        }
        if (IsTypeOperator("<") && !CanInsertSemicolon())
        {
            Next();
            ParseType();
            while (Eat(TokenType.Comma))
            {
                if (IsTypeOperator(">")) break;
                ParseType();
            }
            if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after import type arguments");
            Next();
        }
    }
}
