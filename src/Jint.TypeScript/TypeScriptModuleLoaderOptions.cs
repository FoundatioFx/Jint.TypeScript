namespace Jint.TypeScript;

/// <summary>Bounds the source snapshot owned by a <see cref="TypeScriptModuleLoader"/>.</summary>
public sealed record TypeScriptModuleLoaderOptions
{
    /// <summary>Maximum files in a virtual snapshot or prepared files in a directory loader. Default: 128.</summary>
    public int MaxModuleCount { get; init; } = 128;

    /// <summary>Maximum total UTF-16 source length retained by this loader. Default: 2,000,000.</summary>
    /// <remarks>This independent limit includes cached sources. Jint's source-byte limit does not account for prepared modules.</remarks>
    public int MaxTotalSourceLength { get; init; } = 2_000_000;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxModuleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalSourceLength);
    }
}
