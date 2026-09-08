namespace Jint.TypeScript;

/// <summary>Limits and execution-preparation settings for the supported TypeScript scripting subset.</summary>
public sealed record TypeScriptOptions
{
    public int MaxSourceLength { get; init; } = 1_000_000;
    public int MaxNodeCount { get; init; } = 100_000;
    public int MaxTokenCount { get; init; } = 500_000;
    public int MaxTypeDepth { get; init; } = 64;
    public int MaxSyntaxDepth { get; init; } = 256;
    /// <summary>Maximum AST depth passed to Jint's recursive preparation visitors (1–256).</summary>
    public int MaxAstDepth { get; init; } = 256;
    public bool AllowReturnOutsideFunction { get; init; } = true;
    public bool Strict { get; init; }
    public bool StaticAnalysis { get; init; } = true;
    public bool FoldConstants { get; init; } = true;
    public bool? CompileRegex { get; init; }
    public TimeSpan? RegexTimeout { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSourceLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNodeCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTokenCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTypeDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSyntaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAstDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxAstDepth, 256);
    }
}
