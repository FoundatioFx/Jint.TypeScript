namespace Jint.TypeScript;

/// <summary>A syntax error or parsing limit, located in the original TypeScript source.</summary>
public sealed class TypeScriptParseException : Exception
{
    internal TypeScriptParseException(string code, string description, int index, Acornima.Position position,
        string? sourceFile, Exception? inner = null)
        : base($"{description} ({sourceFile ?? "<script>"}:{position.Line}:{position.Column + 1})", inner)
    {
        Code = code;
        Index = index;
        Position = position;
        SourceFile = sourceFile;
    }

    public string Code { get; }
    /// <summary>Zero-based UTF-16 offset in the original source.</summary>
    public int Index { get; }
    /// <summary>One-based line and zero-based UTF-16 column.</summary>
    public Acornima.Position Position { get; }
    public string? SourceFile { get; }
}
