using Acornima.Ast;

namespace Jint.TypeScript.Parsing;

using static SyntaxErrorMessages;

internal sealed partial class Parser
{
    private Expression ParseGenericArrow(in Marker startMarker, ExpressionContext context)
    {
        if (_potentialArrowAt != _tokenizer._start || (context & ExpressionContext.ForNew) != 0) Unexpected();
        ParseRuntimeTypeParameters();
        if (_tokenizer._type != TokenType.ParenLeft) TypeScriptError("ExpectedArrow", "Expected a parenthesized generic arrow function");
        return ParseParenAndDistinguishExpression(canBeArrow: true, context, startMarker);
    }

    private HashSet<Node>? _instantiatedExpressions;

    private bool IsExpressionTypeArgumentStart() => IsTypeOperator("<")
        || (_tokenizer._type == TokenType.BitShift && Equals(_tokenizer._value.Value, "<<"));

    private bool ParseGenericSubscript(Expression baseExpr, bool noCalls, ref bool maybeAsyncArrow, ref bool optional, ref int optionalChainPos)
    {
        var requireGenericArrow = false;
        if (maybeAsyncArrow && !optional && IsAsyncTypeParameterHead())
            requireGenericArrow = ParseRuntimeTypeParameters();
        else
        {
            ParseRuntimeTypeArguments();
            maybeAsyncArrow = false;
        }
        if (optional && _tokenizer._type != TokenType.ParenLeft)
            TypeScriptError("ExpectedOptionalCall", "Optional type arguments require a call");
        if (_tokenizer._type != TokenType.ParenLeft && _tokenizer._type != TokenType.BackQuote)
        {
            MarkInstantiation(baseExpr);
            if (_tokenizer._type == TokenType.QuestionDot)
            {
                if (noCalls) Raise(_tokenizer._start, OptionalChainingNoNew);
                if (optionalChainPos < 0) optionalChainPos = _tokenizer._start;
                Next();
                optional = true;
            }
        }
        return requireGenericArrow;
    }

    private void ParseRuntimeTypeArguments()
    {
        var wasInType = _tokenizer.InType;
        _tokenizer.InType = true;
        try
        {
            _tokenizer.RescanTypeBoundary();
            Next();
            ParseType();
            while (Eat(TokenType.Comma))
            {
                if (IsTypeOperator(">")) break;
                ParseType();
            }
            if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after type arguments");
            // Read the following token in expression mode, including shifts and regex boundaries.
            _tokenizer.InType = wasInType;
            Next();
        }
        finally { _tokenizer.InType = wasInType; }
    }

    private bool IsAsyncTypeParameterHead()
    {
        if (!IsTypeOperator("<")) return false;
        var probe = StartLookahead();
        try
        {
            probe._tokenizer.InType = true;
            probe.Next();
            probe.ParseTypeParameters();
            return probe._tokenizer._type == TokenType.ParenLeft;
        }
        catch (ParseErrorException) { return false; }
        catch (TypeScriptParseException error) when (!error.Code.EndsWith("Limit", StringComparison.Ordinal)) { return false; }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void MarkInstantiation(Expression expression)
    {
        if (_tokenizer._type == TokenType.Dot || (_tokenizer._type == TokenType.QuestionDot
            && _tokenizer.CharCodeAt(_tokenizer.NextTokenPosition(out _, out _)) != '('))
            TypeScriptError("InvalidInstantiationAccess", "Parenthesize an instantiation expression before accessing a property");
        (_instantiatedExpressions ??= new()).Add(expression);
    }

    private void CheckInstantiatedTarget(Node node)
    {
        if (_instantiatedExpressions?.Contains(node) == true)
            throw new TypeScriptParseException("InvalidInstantiationTarget", "An instantiation expression cannot be an assignment or parameter target",
                node.Start, node.Location.Start, _tokenizer._sourceFile);
    }
}
