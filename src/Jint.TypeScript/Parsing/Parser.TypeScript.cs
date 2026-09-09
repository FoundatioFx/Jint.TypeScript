using Acornima.Ast;
using Jint.TypeScript.Parsing.Helpers;
using static System.Runtime.CompilerServices.Unsafe;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    internal TypeScriptOptions Limits { private get; init; } = null!;
    internal CancellationToken Cancellation { private get; init; }
    private int _tokenCount, _nodeCount, _typeDepth;
    private Parser? _lookahead;
    private int _typeArgumentScanEnd;
    private HashSet<int>? _expressionTypeCandidates;
    private List<(int Depth, int Index)>? _angleCandidates;
    private List<TokenType>? _lookaheadDelimiters;

    private bool HasExpressionTypeArguments()
    {
        if (!IsExpressionTypeArgumentStart()) return false;
        if (_tokenizer._start < _typeArgumentScanEnd)
            return _expressionTypeCandidates?.Contains(_tokenizer._start) == true;
        _expressionTypeCandidates?.Clear();

        // First identify a type-argument-shaped suffix without throwing on ordinary
        // comparisons. Then commit to the type grammar: unsupported types must not
        // silently execute as JavaScript comparisons. Examine nested candidates too
        // and cache the scanned interval to keep long comparison chains linear.
        var probe = StartLookahead();
        var angles = _angleCandidates ??= new();
        angles.Clear();
        var delimiters = _lookaheadDelimiters ??= new();
        delimiters.Clear();
        var depth = 0;
        var found = false;
        probe._tokenizer.InType = true;
        try
        {
            probe.Next();
            while (probe._tokenizer._type != TokenType.EOF)
            {
                var type = probe._tokenizer._type;
                // These operators cannot occur in a type. In particular, stop at
                // '/' so a regex pattern or division operand is never scanned as
                // potential type syntax by a tokenizer running in parser mode.
                if (type == TokenType.Slash || type == TokenType.Star || type == TokenType.StarStar
                    || type == TokenType.LogicalOr || type == TokenType.LogicalAnd
                    || type == TokenType.Coalesce || type == TokenType.Equality || type == TokenType.IncDec) break;
                if (probe.IsTypeOperator("<"))
                    angles.Add((depth, probe._tokenizer._start));
                else if (probe.IsTypeOperator(">") && angles.Count > 0 && angles[^1].Depth == depth)
                {
                    // '>=' remains a comparison; it is not a closing type argument
                    // followed by assignment. The type lexer splits it deliberately.
                    if (probe._tokenizer._end < probe._tokenizer._input.Length && probe._tokenizer._input[probe._tokenizer._end] == '=') break;
                    var candidate = angles[^1];
                    angles.RemoveAt(angles.Count - 1);
                    probe._tokenizer.InType = angles.Count > 0;
                    probe.Next();
                    type = probe._tokenizer._type;
                    // Group bare instantiations before <, >, >= or right shifts.
                    // Babel and tsc disagree on some ungrouped continuations.
                    if (type == TokenType.ParenLeft || type == TokenType.BackQuote
                        || ((type != TokenType.Relational || probe.IsTypeOperator("<="))
                            && (type != TokenType.BitShift || Equals(probe._tokenizer._value.Value, "<<"))
                            && (!type.StartsExpression || probe.CanInsertSemicolon())))
                    {
                        if (candidate.Index == _tokenizer._start) found = true;
                        else (_expressionTypeCandidates ??= new()).Add(candidate.Index);
                    }
                    if (angles.Count == 0) break;
                    continue;
                }
                else if (type == TokenType.ParenLeft || type == TokenType.BracketLeft || type == TokenType.BraceLeft || type == TokenType.DollarBraceLeft)
                {
                    if (++depth > Limits.MaxSyntaxDepth) probe.TypeScriptError("SyntaxDepthLimit", "Syntax nesting limit exceeded");
                    delimiters.Add(type);
                }
                else if (type == TokenType.ParenRight || type == TokenType.BracketRight || type == TokenType.BraceRight)
                {
                    if (depth == 0) break;
                    var open = delimiters[^1];
                    if (type == TokenType.ParenRight ? open != TokenType.ParenLeft
                        : type == TokenType.BracketRight ? open != TokenType.BracketLeft
                        : open != TokenType.BraceLeft && open != TokenType.DollarBraceLeft) break;
                    depth--;
                    delimiters.RemoveAt(delimiters.Count - 1);
                    while (angles.Count > 0 && angles[^1].Depth > depth) angles.RemoveAt(angles.Count - 1);
                }
                else if (depth == 0 && type == TokenType.Semicolon) break;
                probe.Next();
            }
        }
        catch (ParseErrorException) { /* Let the real expression parser report lexical errors in context. */ }
        finally
        {
            // All nested candidates in this interval were examined too. Do not scan
            // the suffix again for each '<' in a long comparison chain (quadratic).
            _typeArgumentScanEnd = probe._tokenizer._start;
            _tokenCount = probe._tokenCount;
            angles.Clear();
            delimiters.Clear();
        }
        return found;
    }

    private Parser StartLookahead()
    {
        var probe = _lookahead ??= new Parser(_options) { Limits = Limits, Cancellation = Cancellation };
        probe._tokenCount = _tokenCount;
        probe._typeDepth = 0;
        // The probe belongs to this parse only. Keep its string deduplication table
        // between token lookaheads so repeated type names do not allocate again.
        probe._tokenizer.ResetForTypeScriptLookahead(_tokenizer._input,
            _inModule ? SourceType.Module : SourceType.Script, _tokenizer._sourceFile);
        probe._tokenizer._position = _tokenizer._start;
        probe._tokenizer._currentLine = _tokenizer._startLocation.Line;
        probe._tokenizer._lineStart = _tokenizer._start - _tokenizer._startLocation.Column;
        probe._tokenizer.InType = false;
        probe._tokenizer._contextStack.Clear();
        probe._tokenizer._contextStack.AddRange(_tokenizer._contextStack.AsReadOnlySpan());
        probe._tokenizer._expressionAllowed = _tokenizer._expressionAllowed;
        probe._strict = _strict;
        probe._isReservedWord = _isReservedWord;
        return probe;
    }

    private void CheckTokenBudget()
    {
        if (++_tokenCount > Limits.MaxTokenCount) TypeScriptError("TokenLimit", "Token limit exceeded");
        if ((_tokenCount & 255) == 0) Cancellation.ThrowIfCancellationRequested();
    }

    private void CheckNodeBudget()
    {
        if (++_nodeCount > Limits.MaxNodeCount) TypeScriptError("NodeLimit", "AST node limit exceeded");
    }

    private void TypeScriptError(string code, string message) =>
        throw new TypeScriptParseException(code, message, _tokenizer._start, _tokenizer._startLocation, _tokenizer._sourceFile);

    private bool ParseTypeAnnotation(bool returnType = false)
    {
        if (_tokenizer._type != TokenType.Colon) return false;
        // Like acorn-typescript's tsInType/tsParseTypeAnnotation: only enter type
        // grammar after a binding or return-type delimiter. Build no type AST.
        var wasInType = _tokenizer.InType;
        _tokenizer.InType = true;
        try
        {
            Next();
            if (returnType) ParseReturnType();
            else ParseType();
            return true;
        }
        finally { _tokenizer.InType = wasInType; }
    }

    private enum TypeShape { Other, Array, Tuple }

    private TypeShape ParseType(bool allowConditional = true)
    {
        if (++_typeDepth > Limits.MaxTypeDepth) TypeScriptError("TypeDepthLimit", "Type nesting limit exceeded");
        try
        {
            // Keep even a caller-raised type depth from exhausting the native stack.
            try { StackGuard.EnsureSufficientExecutionStack(_typeDepth); }
            catch (InsufficientExecutionStackException) { TypeScriptError("TypeDepthLimit", "Insufficient stack for type nesting"); }
            // Function types bind less tightly than unions/intersections. A function
            // used as a constituent must be parenthesized, just as in TypeScript.
            TypeShape shape;
            if (IsFunctionTypeStart())
            {
                ParseTypeSignature(TokenType.Arrow, requireReturnType: true, allowConditional);
                shape = TypeShape.Other;
            }
            else if (_tokenizer._type == TokenType.New || IsAbstractConstructorType())
            {
                if (IsContextual("abstract")) Next();
                Expect(TokenType.New);
                ParseTypeSignature(TokenType.Arrow, requireReturnType: true, allowConditional);
                shape = TypeShape.Other;
            }
            else
            {
                var leadingUnion = Eat(TokenType.BitwiseOr);
                shape = ParseIntersectionType(allowConditional);
                if (leadingUnion) shape = TypeShape.Other;
                while (Eat(TokenType.BitwiseOr))
                {
                    ParseIntersectionType(allowConditional);
                    shape = TypeShape.Other;
                }
            }
            if (allowConditional && _tokenizer._type == TokenType.Extends && !CanInsertSemicolon())
            {
                Next();
                ParseType(allowConditional: false);
                ParseConditionalTypeBranches();
                return TypeShape.Other;
            }
            return shape;
        }
        finally { _typeDepth--; }
    }

    private void ParseConditionalTypeBranches()
    {
        Expect(TokenType.Question);
        ParseType();
        Expect(TokenType.Colon);
        ParseType();
    }

    private TypeShape ParseIntersectionType(bool allowConditional)
    {
        var leadingIntersection = Eat(TokenType.BitwiseAnd);
        var shape = ParseArrayType(allowConditional);
        if (leadingIntersection) shape = TypeShape.Other;
        while (Eat(TokenType.BitwiseAnd)) { ParseArrayType(allowConditional); shape = TypeShape.Other; }
        return shape;
    }

    private TypeShape ParseArrayType(bool allowConditional)
    {
        var shape = TypeShape.Other;
        if (IsContextual("keyof") || IsContextual("readonly") || IsContextual("unique"))
        {
            var readOnly = IsContextual("readonly");
            var unique = IsContextual("unique");
            if (++_typeDepth > Limits.MaxTypeDepth) TypeScriptError("TypeDepthLimit", "Type nesting limit exceeded");
            try
            {
                try { StackGuard.EnsureSufficientExecutionStack(_typeDepth); }
                catch (InsufficientExecutionStackException) { TypeScriptError("TypeDepthLimit", "Insufficient stack for type nesting"); }
                Next();
                if (unique)
                {
                    ExpectContextual("symbol");
                }
                else
                {
                    var operand = ParseArrayType(allowConditional);
                    if (readOnly && operand is not (TypeShape.Array or TypeShape.Tuple))
                        TypeScriptError("InvalidReadonlyType", "Readonly requires an array or tuple type");
                }
            }
            finally { _typeDepth--; }
            return TypeShape.Other;
        }
        if (IsContextual("infer"))
        {
            Next();
            ReadTypeParameterName();
            var lineBreak = CanInsertSemicolon();
            if (Eat(TokenType.Extends))
            {
                ParseType(allowConditional: false);
                // With a type AST, `infer U extends X ? Y : Z` needs rollback to
                // distinguish an infer constraint from a conditional check. Erasure
                // can consume the shared prefix once, avoiding nested reparsing.
                if (allowConditional && _tokenizer._type == TokenType.Question)
                {
                    if (lineBreak) TypeScriptError("ConditionalTypeLineBreak", "A conditional type cannot break before 'extends'");
                    ParseConditionalTypeBranches();
                }
            }
            return TypeShape.Other;
        }
        if (_tokenizer._type == TokenType.ParenLeft)
        {
            Next();
            shape = ParseType();
            Expect(TokenType.ParenRight);
        }
        else if (_tokenizer._type == TokenType.BraceLeft) ParseObjectType();
        else if (_tokenizer._type == TokenType.BracketLeft) { ParseTupleType(); shape = TypeShape.Tuple; }
        else if (_tokenizer._type == TokenType.TypeOf) ParseTypeQuery();
        else if (_tokenizer._type == TokenType.Import) ParseImportType();
        else if (_tokenizer._type == TokenType.BackQuote) ParseTemplateType();
        else if (_tokenizer._type == TokenType.PlusMinus && Equals(_tokenizer._value.Value, "-"))
        {
            Next();
            if (_tokenizer._type != TokenType.Number && _tokenizer._type != TokenType.BigInt)
                TypeScriptError("ExpectedLiteralType", "Expected a numeric literal after '-'");
            Next();
        }
        else
        {
            if (_tokenizer._type == TokenType.Name)
            {
                var name = (string)_tokenizer._value.Value!;
                if (_isReservedWord(name.AsSpan(), _strict)) TypeScriptError("ReservedTypeName", "A reserved word cannot be a type name");
                if (_tokenizer._containsEscape || name is "keyof" or "typeof" or "readonly" or "unique" or "abstract")
                    TypeScriptError("UnsupportedType", "This type form is not supported");
                Next();
                var primitive = name is "any" or "unknown" or "number" or "object" or "boolean" or "bigint" or "string" or "symbol" or "undefined" or "never" or "intrinsic";
                if (!primitive)
                {
                    while (Eat(TokenType.Dot))
                    {
                        if (_tokenizer._type != TokenType.Name || _tokenizer._containsEscape)
                            TypeScriptError("ExpectedTypeName", "Expected a qualified type name");
                        Next();
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
                        if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after type arguments");
                        Next();
                    }
                }
            }
            else if (_tokenizer._type == TokenType.Void || _tokenizer._type == TokenType.Null || _tokenizer._type == TokenType.This
                || _tokenizer._type.Kind is TokenKind.StringLiteral or TokenKind.NumericLiteral or TokenKind.BooleanLiteral or TokenKind.BigIntLiteral)
            {
                Next();
            }
            else
            {
                TypeScriptError("UnsupportedType", "Expected a supported type");
            }
        }

        while (_tokenizer._type == TokenType.BracketLeft && !CanInsertSemicolon())
        {
            Next();
            shape = TypeShape.Array;
            if (_tokenizer._type != TokenType.BracketRight) { ParseType(); shape = TypeShape.Other; }
            Expect(TokenType.BracketRight);
        }
        return shape;
    }

    private bool IsTypeOperator(string value) =>
        _tokenizer._type == TokenType.Relational && Equals(_tokenizer._value.Value, value);

    private bool IsOptionalParameterQuestion()
    {
        if (_tokenizer._type != TokenType.Question) return false;
        var next = _tokenizer.NextTokenPosition(out _, out _);
        return _tokenizer.CharCodeAt(next) is ':' or ',' or ')' or '=';
    }

    private bool ParseParameterAnnotation(Node parameter, bool allowOptional = true)
    {
        var optional = _tokenizer._type == TokenType.Question;
        if (optional)
        {
            if (!allowOptional || parameter is not Identifier)
                TypeScriptError("InvalidOptionalParameter", "Only ordinary identifier parameters can be optional");
            Next();
        }
        var annotated = ParseTypeAnnotation();
        if (optional && _tokenizer._type == TokenType.Eq)
            TypeScriptError("InvalidOptionalDefault", "An optional parameter cannot also have a default value");
        return optional || annotated;
    }

    private Expression ParseArrowParameterAnnotation(Expression parameter, ref bool typed)
    {
        if (_tokenizer._type != TokenType.Colon && _tokenizer._type != TokenType.Question) return parameter;
        if (parameter is not (Identifier or ObjectExpression or ArrayExpression or SpreadElement))
            TypeScriptError("InvalidTypedParameter", "A type annotation must follow an arrow parameter binding");
        ParseParameterAnnotation(parameter);
        typed = true;
        if (!Eat(TokenType.Eq)) return parameter;
        if (parameter is SpreadElement) TypeScriptError("InvalidRestDefault", "A rest parameter cannot have a default value");
        var start = new Marker(parameter.Start, parameter.Location.Start);
        var value = ParseMaybeAssign(ref NullRef<DestructuringErrors>());
        return FinishNode(start, new AssignmentExpression(Operator.Assignment, parameter, value)
        {
            Range = NodeRange(start),
            Location = NodeLocation(start)
        });
    }

    private bool TryParseArrowReturnType(bool hasTypedParameters = false)
    {
        if (_tokenizer._type != TokenType.Colon) return false;
        // Typed parameters already commit this construct to arrow grammar.
        if (hasTypedParameters) return ParseTypeAnnotation(returnType: true);

        // A colon after ')' can belong to `condition ? (expression) : value`.
        // Probe only the bounded type grammar on an independent tokenizer. No JS
        // AST or scope state is rolled back, and source-prefix scanning is avoided.
        var probe = StartLookahead();
        bool isReturnType;
        try
        {
            probe.Next();
            probe.ParseTypeAnnotation(returnType: true);
            isReturnType = probe._tokenizer._type == TokenType.Arrow && !probe.CanInsertSemicolon();
        }
        catch (ParseErrorException) { isReturnType = false; }
        catch (TypeScriptParseException exception) when (!exception.Code.EndsWith("Limit", StringComparison.Ordinal))
        {
            isReturnType = false;
        }
        finally
        {
            _tokenCount = probe._tokenCount;
        }
        if (isReturnType) ParseTypeAnnotation(returnType: true);
        return isReturnType;
    }
}
