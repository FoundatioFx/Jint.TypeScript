namespace Jint.TypeScript;

/// <summary>Limits and execution-preparation settings for the supported TypeScript scripting subset.</summary>
public sealed record TypeScriptOptions
{
    /// <summary>Maximum source length in UTF-16 code units. Default: 1,000,000; must be positive.</summary>
    public int MaxSourceLength { get; init; } = 1_000_000;
    /// <summary>Maximum constructed AST nodes, including temporary parsing nodes. Default: 100,000; must be positive.</summary>
    public int MaxNodeCount { get; init; } = 100_000;
    /// <summary>Maximum token-processing steps, including lookahead work. Default: 500,000; must be positive.</summary>
    public int MaxTokenCount { get; init; } = 500_000;
    /// <summary>Maximum nested type grammar depth. Default: 64; must be positive.</summary>
    public int MaxTypeDepth { get; init; } = 64;
    /// <summary>Maximum nested runtime syntax depth. Default: 256; must be positive.</summary>
    public int MaxSyntaxDepth { get; init; } = 256;
    /// <summary>Maximum AST depth passed to Jint's recursive preparation visitors (1–256).</summary>
    public int MaxAstDepth { get; init; } = 256;
    /// <summary>Allows a top-level return in scripts. Default: true. Modules always reject it.</summary>
    public bool AllowReturnOutsideFunction { get; init; } = true;
    /// <summary>Parses scripts in strict mode. Default: false. Modules and explicit strict directives remain strict.</summary>
    public bool Strict { get; init; }
    /// <summary>Enables Jint static analysis during preparation. Default: true. This is not TypeScript type checking.</summary>
    public bool StaticAnalysis { get; init; } = true;
    /// <summary>Enables Jint constant folding during preparation. Default: true.</summary>
    public bool FoldConstants { get; init; } = true;
    /// <summary>Controls Jint's regular-expression compilation. Null (the default) uses Jint's preparation default.</summary>
    public bool? CompileRegex { get; init; }
    /// <summary>Regular-expression matching timeout passed to Jint. Null (the default) uses Jint's default.</summary>
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
