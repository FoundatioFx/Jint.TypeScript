namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private bool IsAbstractConstructorType()
    {
        if (!IsContextual("abstract")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            return probe._tokenizer._type == TokenType.New;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    // These routines consume types without creating nodes or entering JS scopes.
    // Reference: acorn-typescript's type member/signature and tuple productions.
    // Modern mixed labeled/unlabeled tuples follow the current compiler oracle.
    private bool IsFunctionTypeStart()
    {
        if (IsTypeOperator("<")) return true;
        if (_tokenizer._type != TokenType.ParenLeft) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            if (probe._tokenizer._type == TokenType.ParenRight || probe._tokenizer._type == TokenType.Ellipsis) return true;
            if (probe._tokenizer._type == TokenType.BraceLeft || probe._tokenizer._type == TokenType.BracketLeft)
            {
                if (!probe.SkipTypeBindingLookahead()) return false;
            }
            else
            {
                if (probe._tokenizer._type != TokenType.Name && probe._tokenizer._type != TokenType.This) return false;
                probe.Next();
            }
            if (probe._tokenizer._type == TokenType.Colon || probe._tokenizer._type == TokenType.Question
                || probe._tokenizer._type == TokenType.Comma) return true;
            if (probe._tokenizer._type != TokenType.ParenRight) return false;
            probe.Next();
            return probe._tokenizer._type == TokenType.Arrow;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseTypeSignature(TokenType returnDelimiter, bool requireReturnType, bool allowConditional = true,
        TypeAccessorKind accessor = TypeAccessorKind.None)
    {
        if (accessor != TypeAccessorKind.None && IsTypeOperator("<"))
            TypeScriptError("InvalidTypeAccessor", "An accessor cannot have type parameters");
        ParseTypeParameters();
        Expect(TokenType.ParenLeft);
        var first = true;
        var parameterCount = 0;
        while (!Eat(TokenType.ParenRight))
        {
            parameterCount++;
            if (accessor == TypeAccessorKind.Get || (accessor == TypeAccessorKind.Set && parameterCount > 1))
                TypeScriptError("InvalidTypeAccessor", "A getter requires no parameters and a setter requires exactly one");
            var rest = Eat(TokenType.Ellipsis);
            var isThis = _tokenizer._type == TokenType.This;
            if (accessor != TypeAccessorKind.None && (rest || isThis))
                TypeScriptError("InvalidTypeAccessor", "An accessor cannot declare a rest or 'this' parameter");
            if (isThis)
            {
                if (!first || rest) TypeScriptError("InvalidThisParameter", "A 'this' parameter must be first");
                Next();
            }
            else ParseTypeBinding();
            var optional = Eat(TokenType.Question);
            if (optional && (rest || isThis || accessor == TypeAccessorKind.Set))
                TypeScriptError("InvalidOptionalParameter", "This parameter cannot be optional");
            if (Eat(TokenType.Colon)) ParseType();
            else if (isThis) TypeScriptError("ExpectedTypeAnnotation", "A 'this' parameter requires a type");
            if (_tokenizer._type == TokenType.Eq)
                TypeScriptError("SignatureInitializer", "A type signature parameter cannot have an initializer");
            if (Eat(TokenType.ParenRight)) break;
            if (rest) TypeScriptError("ParameterAfterRest", "A rest parameter must be last and cannot have a trailing comma");
            Expect(TokenType.Comma);
            first = false;
        }
        if (accessor == TypeAccessorKind.Set)
        {
            if (parameterCount != 1 || _tokenizer._type == returnDelimiter)
                TypeScriptError("InvalidTypeAccessor", "A setter requires exactly one parameter and cannot have a return type");
            return;
        }
        if (Eat(returnDelimiter)) ParseReturnType(allowConditional);
        else if (requireReturnType) TypeScriptError("ExpectedReturnType", "Expected a function return type");
    }

    private string ReadTypeIdentifier()
    {
        if (_tokenizer._type != TokenType.Name || _tokenizer._containsEscape)
            TypeScriptError("ExpectedTypeName", "Expected an unescaped identifier in type syntax");
        var name = (string)_tokenizer._value.Value!;
        Next();
        return name;
    }

    private void ReadTypeParameterName()
    {
        if (_tokenizer._type == TokenType.Name && _isReservedWord(((string)_tokenizer._value.Value!).AsSpan(), _strict))
            TypeScriptError("ReservedTypeName", "A reserved word cannot be a type parameter name");
        ReadTypeIdentifier();
    }

    private bool ParseTypeParameters(bool allowVariance = false, bool allowConst = true)
    {
        if (!IsTypeOperator("<")) return false;
        var hasConstraintOrDefault = false;
        Next();
        do
        {
            var modifiers = 0;
            while ((_tokenizer._type == TokenType.Const || _tokenizer._type == TokenType.In || IsContextual("out")) && TypeModifierFollows())
            {
                var modifier = _tokenizer._type == TokenType.Const ? 1 : _tokenizer._type == TokenType.In ? 2 : 4;
                if (modifier == 1 ? !allowConst : !allowVariance)
                    TypeScriptError("InvalidTypeParameterModifier", "This type parameter modifier is not allowed here");
                if ((modifiers & modifier) != 0 || (modifier == 2 && (modifiers & 4) != 0))
                    TypeScriptError("InvalidTypeParameterModifier", "Duplicate or incorrectly ordered type parameter modifiers");
                modifiers |= modifier;
                Next();
            }
            // A modifier makes async<const T>(x) unambiguously an arrow head.
            hasConstraintOrDefault |= modifiers != 0;
            ReadTypeParameterName();
            if (Eat(TokenType.Extends)) { hasConstraintOrDefault = true; ParseType(); }
            if (Eat(TokenType.Eq)) { hasConstraintOrDefault = true; ParseType(); }
            if (!Eat(TokenType.Comma)) break;
            if (IsTypeOperator(">")) break;
        } while (true);
        if (!IsTypeOperator(">")) TypeScriptError("ExpectedTypeClose", "Expected '>' after type parameters");
        Next();
        return hasConstraintOrDefault;
    }

    private void ParseObjectType(bool allowMapped = true)
    {
        if (IsMappedTypeStart())
        {
            if (!allowMapped) TypeScriptError("MappedInterface", "A mapped type requires a type alias or annotation");
            ParseMappedType();
            return;
        }
        Expect(TokenType.BraceLeft);
        while (!Eat(TokenType.BraceRight))
        {
            var readOnly = IsContextual("readonly") && TypeModifierFollows();
            if (readOnly) Next();
            if (_tokenizer._type == TokenType.ParenLeft || IsTypeOperator("<"))
            {
                if (readOnly) TypeScriptError("InvalidReadonly", "A call signature cannot be readonly");
                ParseTypeSignature(TokenType.Colon, requireReturnType: false);
            }
            else
            {
                var accessor = TypeAccessorKind.None;
                if (IsTypeAccessorStart())
                {
                    accessor = IsContextual("get") ? TypeAccessorKind.Get : TypeAccessorKind.Set;
                    if (readOnly) TypeScriptError("InvalidReadonly", "An accessor cannot be readonly");
                    Next();
                }
                var indexSignature = false;
                if (_tokenizer._type == TokenType.BracketLeft)
                    indexSignature = ParseTypeComputedName(allowIndexSignature: accessor == TypeAccessorKind.None);
                else ParseTypeMemberName();
                if (!indexSignature)
                {
                    if (Eat(TokenType.Question) && accessor != TypeAccessorKind.None)
                        TypeScriptError("InvalidTypeAccessor", "An accessor cannot be optional");
                    if (_tokenizer._type == TokenType.ParenLeft || IsTypeOperator("<"))
                    {
                        if (readOnly) TypeScriptError("InvalidReadonly", "A method or construct signature cannot be readonly");
                        ParseTypeSignature(TokenType.Colon, requireReturnType: false, accessor: accessor);
                    }
                    else if (accessor != TypeAccessorKind.None)
                        TypeScriptError("InvalidTypeAccessor", "Expected an accessor signature");
                    else if (Eat(TokenType.Colon)) ParseType();
                }
                // `new: T` is a property; `new(...): T` is a construct signature.
                // Both are erased, so no separate runtime representation is needed.
            }
            if (!Eat(TokenType.Semicolon) && !Eat(TokenType.Comma)
                && _tokenizer._type != TokenType.BraceRight && !CanInsertSemicolon())
                TypeScriptError("ExpectedTypeSeparator", "Expected a type member separator");
        }
    }

    private bool IsMappedTypeStart()
    {
        if (_tokenizer._type != TokenType.BraceLeft) return false;
        // Ordinary property shapes need no tokenizer probe. A mapped type starts
        // with '[', a signed readonly modifier, or the unescaped word readonly.
        var next = _tokenizer.NextTokenPosition(out _, out _);
        if (_tokenizer.CharCodeAt(next) is not ('[' or '+' or '-' or 'r')) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            if (probe._tokenizer._type == TokenType.PlusMinus) probe.Next();
            probe.EatContextual("readonly");
            if (!probe.Eat(TokenType.BracketLeft) || probe._tokenizer._type != TokenType.Name) return false;
            probe.Next();
            return probe._tokenizer._type == TokenType.In;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseMappedType()
    {
        Expect(TokenType.BraceLeft);
        if (_tokenizer._type == TokenType.PlusMinus)
        {
            Next();
            ExpectContextual("readonly");
        }
        else EatContextual("readonly");
        Expect(TokenType.BracketLeft);
        ReadTypeIdentifier();
        Expect(TokenType.In);
        ParseType();
        if (EatContextual("as")) ParseType();
        Expect(TokenType.BracketRight);
        if (_tokenizer._type == TokenType.PlusMinus)
        {
            Next();
            Expect(TokenType.Question);
        }
        else Eat(TokenType.Question);
        if (Eat(TokenType.Colon)) ParseType();
        Eat(TokenType.Semicolon);
        Expect(TokenType.BraceRight);
    }

    private void ParseTemplateType()
    {
        Next(requireValidEscapeSequenceInTemplate: true);
        while (true)
        {
            Expect(TokenType.Template);
            if (Eat(TokenType.BackQuote)) return;
            Expect(TokenType.DollarBraceLeft);
            ParseType();
            if (_tokenizer._type != TokenType.BraceRight) TypeScriptError("ExpectedTemplateClose", "Expected '}' after the template type substitution");
            Next(requireValidEscapeSequenceInTemplate: true);
        }
    }

    private bool TypeModifierFollows()
    {
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            var type = probe._tokenizer._type;
            return !probe.CanInsertSemicolon() && (type == TokenType.Name || type.Keyword is not null
                || type == TokenType.String || type == TokenType.Number || type == TokenType.BracketLeft);
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private void ParseTupleType()
    {
        Expect(TokenType.BracketLeft);
        var sawOptional = false;
        while (!Eat(TokenType.BracketRight))
        {
            var rest = Eat(TokenType.Ellipsis);
            var labeled = IsTupleLabel();
            var optional = false;
            if (labeled)
            {
                ReadTypeIdentifier();
                optional = Eat(TokenType.Question);
                Expect(TokenType.Colon);
            }
            ParseType();
            if (!labeled) optional = Eat(TokenType.Question);
            if (rest && optional) TypeScriptError("InvalidOptionalTupleRest", "A rest element cannot be optional");
            if (sawOptional && !optional && !rest) TypeScriptError("RequiredTupleElement", "A required tuple element cannot follow an optional element");
            sawOptional |= optional;
            if (Eat(TokenType.BracketRight)) break;
            Expect(TokenType.Comma);
        }
    }

    private bool IsTupleLabel()
    {
        if (_tokenizer._type != TokenType.Name) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            probe.Eat(TokenType.Question);
            return probe._tokenizer._type == TokenType.Colon;
        }
        finally { _tokenCount = probe._tokenCount; }
    }
}
