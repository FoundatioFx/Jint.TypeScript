namespace Jint.TypeScript;

/// <summary>A syntax error or parsing limit, located in the original TypeScript source.</summary>
public sealed class TypeScriptParseException : Exception
{
    internal TypeScriptParseException(string code, string description, int index, Acornima.Position position,
        string? sourceFile, Exception? inner = null)
        : base($"{description} ({sourceFile ?? "<script>"}:{position.Line}:{position.Column + 1})", inner)
    {
        Code = code;
        Description = description;
        Index = index;
        Position = position;
        SourceFile = sourceFile;
    }

    /// <summary>Parser diagnostic or resource-limit code, such as <c>UnsupportedEnum</c>, <c>UnsupportedType</c> or <c>SourceLimit</c>.</summary>
    public string Code { get; }
    /// <summary>The diagnostic text without a source location, for hosts that format structured diagnostics.</summary>
    public string Description { get; }
    /// <summary>Zero-based UTF-16 offset in the original source.</summary>
    public int Index { get; }
    /// <summary>One-based line and zero-based UTF-16 column.</summary>
    public Acornima.Position Position { get; }
    /// <summary>The source identity supplied to the compiler, or null when no identity was supplied.</summary>
    public string? SourceFile { get; }
}
