using Acornima.Ast;
using static System.Runtime.CompilerServices.Unsafe;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    // Only assertions allocate this set. It retains syntax information until
    // arrow-parameter reinterpretation has finished, without wrapping runtime ASTs.
    private HashSet<Node>? _assertedExpressions;

    private bool IsAssertionOperator(int minPrecedence) =>
        minPrecedence < TokenType.In.Precedence && _tokenizer._type == TokenType.Name
        && !CanInsertSemicolon() && (IsContextual("as") || IsContextual("satisfies"));

    private Expression ParseAssertionWithExponentiation(in Marker leftStartMarker, Expression left, ExpressionContext context)
    {
        ParseAssertion(left);
        if (_tokenizer._type == TokenType.StarStar)
        {
            // tsc requires grouping here even though Babel accepts an
            // unparenthesized composite assertion before exponentiation.
            if (left is BinaryExpression { Operator: not Operator.Exponentiation } && left.Start == leftStartMarker.Index)
                TypeScriptError("AssertionExponentiation", "Parenthesize the composite expression before asserting and exponentiating it");
            Next();
            EnterRecursion();
            var exponent = ExitRecursion(ParseMaybeUnary(sawUnary: false, incDec: false, ref NullRef<DestructuringErrors>(), context));
            left = BuildBinary(leftStartMarker, left, exponent, Operator.Exponentiation, logical: false);
        }
        return left;
    }

    private void ParseAssertion(Expression expression)
    {
        var allowConst = IsContextual("as");
        var wasInType = _tokenizer.InType;
        _tokenizer.InType = true;
        try
        {
            Next();
            if (allowConst && _tokenizer._type == TokenType.Const) Next();
            else ParseType();
        }
        finally { _tokenizer.InType = wasInType; }
        // The type lexer splits >>, >>> and >=. Restore the complete JS operator
        // at the boundary before the expression precedence loop resumes.
        _tokenizer.RescanTypeBoundary();
        (_assertedExpressions ??= new()).Add(expression);
    }

    private bool TryParseNonNull(Expression expression)
    {
        if (_tokenizer._type != TokenType.PrefixOp || !Equals(_tokenizer._value.Value, "!") || CanInsertSemicolon()) return false;
        Next();
        (_assertedExpressions ??= new()).Add(expression);
        return true;
    }

    private void CheckAssertedBinding(Node node)
    {
        if (_assertedExpressions?.Contains(node) == true)
            throw new TypeScriptParseException("AssertionInBinding", "An assertion cannot be used as a parameter binding",
                node.Start, node.Location.Start, _tokenizer._sourceFile);
    }

    private bool ParseRuntimeTypeParameters()
    {
        if (!IsTypeOperator("<")) return false;
        var wasInType = _tokenizer.InType;
        _tokenizer.InType = true;
        try { return ParseTypeParameters(); }
        finally { _tokenizer.InType = wasInType; }
    }
}
