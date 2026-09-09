using Acornima.Ast;
using System.Runtime.CompilerServices;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private bool IsAbstractClassStart()
    {
        if (!IsContextual("abstract")) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            return !probe.CanInsertSemicolon() && probe._tokenizer._type == TokenType.Class;
        }
        finally { _tokenCount = probe._tokenCount; }
    }

    private ClassDeclaration ParseAbstractClass(in Marker startMarker, FunctionOrClassFlags flags = FunctionOrClassFlags.Statement)
    {
        ExpectContextual("abstract");
        Expect(TokenType.Class);
        return (ClassDeclaration)ParseClass(startMarker, flags, isAbstract: true);
    }

    private void CheckTypeScriptClassMember(Expression key, bool computed, PropertyKind kind, bool optional,
        int modifiers, bool isStatic, bool constructorAllowsSuper, bool isAbstractClass)
        => CheckTypeScriptClassMember(key is PrivateIdentifier, CheckKeyName(key, computed, "constructor"), kind,
            optional, modifiers, isStatic, constructorAllowsSuper, isAbstractClass);

    private void CheckTypeScriptClassMember(bool privateName, bool constructorName, PropertyKind kind, bool optional,
        int modifiers, bool isStatic, bool constructorAllowsSuper, bool isAbstractClass)
    {
        if ((modifiers & ClassModifierAbstract) != 0 && (!isAbstractClass || isStatic))
            TypeScriptError("InvalidAbstractMember", "Abstract members require an abstract class and cannot be static");
        if (optional && kind is PropertyKind.Get or PropertyKind.Set)
            TypeScriptError("InvalidOptionalAccessor", "An accessor cannot be optional");
        // Babel accepts an optional constructor, but tsc rejects its syntax.
        if (optional && !isStatic && constructorName)
            TypeScriptError("InvalidOptionalConstructor", "A constructor cannot be optional");
        if ((modifiers & 16) != 0 && (!constructorAllowsSuper || (!isStatic && constructorName)))
            TypeScriptError("InvalidOverride", "Override requires a non-constructor member of a derived class");
        if ((modifiers & ~ClassModifierReadonly) != 0 && privateName)
            TypeScriptError("InvalidPrivateModifier", "Private identifiers cannot have accessibility, override, abstract or declare modifiers");
    }

    private bool TryParseErasedClassField(int modifiers, bool isStatic, bool constructorAllowsSuper, bool isAbstractClass)
    {
        // Keep computed, private, quoted and contextual-keyword names on native
        // parsing. Ordinary erased fields need no temporary identifier AST node.
        if (_tokenizer._type != TokenType.Name || _tokenizer._containsEscape) return false;
        var probe = StartLookahead();
        try
        {
            probe.Next();
            probe.Next();
            probe.Eat(TokenType.Question);
            if (probe._tokenizer._type == TokenType.ParenLeft || probe.IsTypeOperator("<")) return false;
            if (probe._tokenizer._type != TokenType.Colon && probe._tokenizer._type != TokenType.PrefixOp
                && probe._tokenizer._type != TokenType.Eq && probe._tokenizer._type != TokenType.Semicolon
                && !probe.CanInsertSemicolon()) return false;
        }
        finally { _tokenCount = probe._tokenCount; }
        var name = (string)_tokenizer._value.Value!;
        Next();
        var optional = Eat(TokenType.Question);
        CheckTypeScriptClassMember(false, name == "constructor", PropertyKind.Unknown, optional,
            modifiers, isStatic, constructorAllowsSuper, isAbstractClass);
        CheckErasedClassFieldName(name == "constructor", isStatic && name == "prototype");
        ParseClassFieldAnnotation(optional);
        FinishErasedClassField();
        return true;
    }

    private bool TryFinishErasedClassField(int modifiers, Expression key, bool computed, bool isStatic)
    {
        if ((modifiers & (ClassModifierAbstract | ClassModifierDeclare)) == 0) return false;
        // Preserve the native name checks even though no field node is constructed.
        CheckErasedClassFieldName(CheckKeyName(key, computed, "constructor"), isStatic && CheckKeyName(key, computed, "prototype"));
        FinishErasedClassField();
        return true;
    }

    private void CheckErasedClassFieldName(bool constructor, bool staticPrototype)
    {
        if (constructor || staticPrototype) TypeScriptError("InvalidErasedFieldName", "This name cannot be used for a class field");
    }

    private void FinishErasedClassField()
    {
        if (_tokenizer._type == TokenType.Eq)
            TypeScriptError("ErasedFieldInitializer", "An abstract or declare field cannot have an initializer");
        Semicolon();
    }

    private void CheckAbstractSignature(in NodeList<Node> parameters, PropertyKind kind)
    {
        if (_tokenizer._type == TokenType.BraceLeft)
            TypeScriptError("AbstractMethodBody", "An abstract method or accessor cannot have an implementation");
        if (kind == PropertyKind.Get && parameters.Count != 0)
            TypeScriptError("AbstractGetterArity", "A getter must have no parameters");
        if (kind == PropertyKind.Set && (parameters.Count != 1 || parameters[0] is RestElement))
            TypeScriptError("AbstractSetterArity", "A setter must have exactly one non-rest parameter");
    }

    private void CheckTypeScriptClassMethodModifiers(int modifiers)
    {
        if ((modifiers & ClassModifierReadonly) != 0)
            TypeScriptError("InvalidReadonlyMethod", "A method cannot be readonly");
        if ((modifiers & ClassModifierDeclare) != 0)
            TypeScriptError("InvalidDeclareMethod", "Declare is supported on fields, not methods or accessors");
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
    private const int ClassModifierAbstract = 32;
    private const int ClassModifierDeclare = 64;

    private int ParseClassModifiers(int modifiers = 0, bool afterStatic = false)
    {
        while (_tokenizer._type == TokenType.Name && !_tokenizer._containsEscape)
        {
            var flag = _tokenizer._value.Value switch
            {
                "public" => 1, "protected" => 2, "private" => 4,
                "readonly" => ClassModifierReadonly, "override" => 16,
                "abstract" => ClassModifierAbstract, "declare" => ClassModifierDeclare, _ => 0
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
            if ((flag <= 4 && (afterStatic || (modifiers & ~(ClassModifierAbstract | ClassModifierDeclare)) != 0))
                || (flag == 16 && (modifiers & ClassModifierReadonly) != 0)
                || (flag == ClassModifierAbstract && (modifiers & 16) != 0))
                TypeScriptError("InvalidModifierOrder", "Invalid ordering of accessibility, static, abstract, override or readonly modifiers");
            if ((modifiers & flag) != 0 || (flag <= 4 && (modifiers & 7) != 0))
                TypeScriptError("DuplicateClassModifier", "Duplicate or conflicting class modifiers");
            if ((flag == ClassModifierDeclare && (modifiers & 16) != 0) || (flag == 16 && (modifiers & ClassModifierDeclare) != 0))
                TypeScriptError("InvalidDeclareOverride", "Declare and override cannot be combined");
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
