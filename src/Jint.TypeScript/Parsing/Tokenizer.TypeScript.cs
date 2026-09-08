namespace Jint.TypeScript.Parsing;

internal sealed partial class Tokenizer
{
    // Keep the same-parse lookahead pool without changing upstream reset logic.
    // Detach it before ResetInternal clears the active pool; restoring a copied
    // struct after Clear would be too late because its arrays are shared.
    internal void ResetForTypeScriptLookahead(string input, SourceType sourceType, string? sourceFile)
    {
        var strings = _stringPool;
        _stringPool = default;
        try { ResetInternal(input, 0, input.Length, sourceType, sourceFile); }
        finally { _stringPool = strings; }
    }

    internal bool InType;

    internal void RescanTypeBoundary()
    {
        if (_type != TokenType.Relational && !(InType && _type == TokenType.BitShift)) return;
        _position = _start;
        ReadToken_Lt_Gt(CharCodeAtPosition());
    }
}
