using Acornima.Ast;
using Jint.TypeScript.Parsing.Helpers;

namespace Jint.TypeScript.Parsing;

internal sealed partial class Parser
{
    private Range NodeRange(in Marker start) => Range.From(start.Index, _tokenizer._lastTokenEnd);
    private static Range NodeRange(in Marker start, in Marker end) => Range.From(start.Index, end.Index);
    private SourceLocation NodeLocation(in Marker start) => SourceLocation.From(start.Position, _tokenizer._lastTokenEndLocation, _tokenizer._sourceFile);
    private SourceLocation NodeLocation(in Marker start, in Marker end) => SourceLocation.From(start.Position, end.Position, _tokenizer._sourceFile);

    // Literal tokens already know their end before Next reads the following token.
    private Range TokenNodeRange(in Marker start) => Range.From(start.Index, _tokenizer._end);
    private SourceLocation TokenNodeLocation(in Marker start) => SourceLocation.From(start.Position, _tokenizer._endLocation, _tokenizer._sourceFile);

    private readonly record struct VariableDeclarationParts(VariableDeclarationKind Kind, NodeList<VariableDeclarator> Declarations);

    private VariableDeclaration FinishNode(in Marker start, in VariableDeclarationParts parts) =>
        FinishNode(start, new VariableDeclaration(parts.Kind, parts.Declarations) { Range = NodeRange(start), Location = NodeLocation(start) });

    // This is the real public tokenizer, used only for regex leaf metadata required by Jint.
    internal Acornima.ParserOptions RegexOptions { private get; init; } = null!;
    private Acornima.Tokenizer? _regexTokenizer;

    private RegExpLiteral ParsePublicRegExp(string raw)
    {
        // A slice of the full source makes Acornima scan the prefix for line information
        // at every regex. Tokenize only the literal, then attach its original location.
        _regexTokenizer ??= new Acornima.Tokenizer(string.Empty, RegexOptions.GetTokenizerOptions());
        _regexTokenizer.Reset(raw, SourceType.Script, _tokenizer._sourceFile);
        try
        {
            var token = _regexTokenizer.GetToken(new Acornima.TokenizerContext(_strict));
            return new RegExpLiteral(token.RegExpValue!.Value, token.RegExpParseResult!.Value, raw)
            {
                Range = Range.From(_tokenizer._start, _tokenizer._end),
                Location = SourceLocation.From(_tokenizer._startLocation, _tokenizer._endLocation, _tokenizer._sourceFile)
            };
        }
        catch (Acornima.ParseErrorException exception)
        {
            var error = exception.Error;
            // A regex literal cannot contain a literal line terminator.
            throw new TypeScriptParseException(error.Code, error.Description, _tokenizer._start + error.Index,
                Position.From(_tokenizer._startLocation.Line, _tokenizer._startLocation.Column + error.Column),
                _tokenizer._sourceFile, exception);
        }
    }
}

internal static class ParserNodeList
{
    internal static NodeList<T> From<T>(ref ArrayList<T> list) where T : Node?
    {
        // The public factory copies once from ICollection<T>. No AST conversion pass.
        if (list.Count == 0) return default;
        var result = NodeList.From(list);
        list = default;
        return result;
    }
}
