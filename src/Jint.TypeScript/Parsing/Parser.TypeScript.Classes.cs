using Acornima.Ast;
using System.Runtime.CompilerServices;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private void CheckTypeScriptClassMember(Expression key, bool computed, PropertyKind kind, bool optional,
        int modifiers, bool isStatic, bool constructorAllowsSuper)
    {
        if (optional && kind is PropertyKind.Get or PropertyKind.Set)
            TypeScriptError("InvalidOptionalAccessor", "An accessor cannot be optional");
        // Babel accepts an optional constructor, but tsc rejects its syntax.
        if (optional && !isStatic && CheckKeyName(key, computed, "constructor"))
            TypeScriptError("InvalidOptionalConstructor", "A constructor cannot be optional");
        if ((modifiers & 16) != 0 && (!constructorAllowsSuper || (!isStatic && CheckKeyName(key, computed, "constructor"))))
            TypeScriptError("InvalidOverride", "Override requires a non-constructor member of a derived class");
        if ((modifiers & ~ClassModifierReadonly) != 0 && key is PrivateIdentifier)
            TypeScriptError("InvalidPrivateModifier", "Private identifiers cannot have accessibility or override modifiers");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseClassFieldAnnotation(bool optional)
    {
        var definite = _tokenizer._type == TokenType.PrefixOp && Equals(_tokenizer._value.Value, "!");
        if (definite)
        {
            if (optional) TypeScriptError("InvalidDefiniteField", "A field cannot be both optional and definite");
            Next();
        }
        if (!ParseTypeAnnotation() && definite)
            TypeScriptError("ExpectedTypeAnnotation", "A definite field requires a type annotation");
        if (definite && _tokenizer._type == TokenType.Eq)
            TypeScriptError("InvalidDefiniteField", "A definite field cannot have an initializer");
    }

    private const int ClassModifierReadonly = 8;

    private int ParseClassModifiers(int modifiers = 0, bool afterStatic = false)
    {
        while (_tokenizer._type == TokenType.Name && !_tokenizer._containsEscape)
        {
            var flag = _tokenizer._value.Value switch
            {
                "public" => 1, "protected" => 2, "private" => 4,
                "readonly" => ClassModifierReadonly, "override" => 16, _ => 0
            };
            if (flag == 0) break;
            var probe = StartLookahead();
            bool isModifier;
            try
            {
                probe.Next();
                probe.Next();
                isModifier = !probe.CanInsertSemicolon() && (probe.IsClassElementNameStart() || probe._tokenizer._type == TokenType.Star);
            }
            finally { _tokenCount = probe._tokenCount; }
            if (!isModifier) break;
            if ((flag <= 4 && (afterStatic || modifiers != 0)) || (flag == 16 && (modifiers & ClassModifierReadonly) != 0))
                TypeScriptError("InvalidModifierOrder", "Accessibility must precede static, override and readonly; override must precede readonly");
            if ((modifiers & flag) != 0 || (flag <= 4 && (modifiers & 7) != 0))
                TypeScriptError("DuplicateClassModifier", "Duplicate or conflicting class modifiers");
            modifiers |= flag;
            Next();
        }
        return modifiers;
    }

    private void ParseClassImplements()
    {
        if (!EatContextual("implements")) return;
        do
        {
            ReadTypeIdentifier();
            while (Eat(TokenType.Dot)) ReadTypeIdentifier();
            if (IsTypeOperator("<")) ParseRuntimeTypeArguments();
        } while (Eat(TokenType.Comma));
    }
}
