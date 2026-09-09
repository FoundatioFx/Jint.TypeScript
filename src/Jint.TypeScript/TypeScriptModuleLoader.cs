using System.Text;
using Acornima.Ast;
using Jint.Runtime.Modules;

namespace Jint.TypeScript;

/// <summary>Loads relative TypeScript modules from a directory or an immutable snapshot of virtual files.</summary>
/// <remarks>Pass an instance to Jint's <c>options.UseModules(loader)</c>. Imports require explicit .ts, .mts,
/// .js or .mjs extensions. Bare names can reference modules registered on the engine by the host.
/// Declaration files are editor-only. Package resolution, JSON modules and import
/// attributes are not supported. Prepared code is cached per instance and can be shared across engines;
/// each engine receives its own module instances. Recreate the loader to see file edits. A directory root
/// restricts resolved URLs, but is not a filesystem sandbox against symbolic links.</remarks>
public sealed class TypeScriptModuleLoader : IModuleLoader
{
    private const string VirtualRoot = "file:///__jint_typescript__/";
    private readonly DefaultModuleLoader _resolver;
    private readonly TypeScriptCompiler _compiler;
    private readonly TypeScriptModuleLoaderOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, string>? _sources;
    private readonly Dictionary<string, Prepared<Module>> _prepared = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _totalSourceLength;

    /// <summary>Creates a loader that reads and prepares directory modules on their first import.</summary>
    /// <param name="basePath">Base directory for relative imports. Resolved URLs must remain inside it.</param>
    /// <param name="compiler">Reusable compiler, or null to use default parsing limits.</param>
    /// <param name="options">Loader source/cache limits, or null for defaults.</param>
    /// <param name="cancellationToken">Cancellation for this loader's lifetime, checked during reads and preparation.</param>
    public TypeScriptModuleLoader(string basePath, TypeScriptCompiler? compiler = null,
        TypeScriptModuleLoaderOptions? options = null, CancellationToken cancellationToken = default)
        : this(new DefaultModuleLoader(Path.GetFullPath(basePath)), compiler, options, cancellationToken) { }

    /// <summary>Copies virtual source files into a bounded snapshot without accessing the filesystem.</summary>
    /// <param name="sources">Relative filenames mapped to source text, for example main.ts and lib/math.ts.
    /// Names are case-sensitive, use forward slashes, and cannot contain empty, dot or parent segments.</param>
    /// <param name="compiler">Reusable compiler, or null to use default parsing limits.</param>
    /// <param name="options">Limits for the entire snapshot, including editor-only declaration files.</param>
    /// <param name="cancellationToken">Cancellation for this loader's lifetime, including preparation.</param>
    public TypeScriptModuleLoader(IReadOnlyDictionary<string, string> sources, TypeScriptCompiler? compiler = null,
        TypeScriptModuleLoaderOptions? options = null, CancellationToken cancellationToken = default)
        : this(new DefaultModuleLoader(VirtualRoot), compiler, options, cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count > _options.MaxModuleCount) throw Limit("Module count limit exceeded.");
        _sources = new(StringComparer.Ordinal);
        foreach (var (name, source) in sources)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(source);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Contains('\\') || name.Contains(':')
                || name.Any(char.IsControl) || name.Split('/').Any(part => part is "" or "." or ".."))
                throw new ArgumentException($"Invalid virtual filename '{name}'.", nameof(sources));
            CheckSourceLength(source.Length, name);
            var key = VirtualRoot + string.Join('/', name.Split('/').Select(Uri.EscapeDataString));
            _sources.Add(key, source);
            _totalSourceLength += source.Length;
        }
    }

    private TypeScriptModuleLoader(DefaultModuleLoader resolver, TypeScriptCompiler? compiler,
        TypeScriptModuleLoaderOptions? options, CancellationToken cancellationToken)
    {
        _resolver = resolver;
        _compiler = compiler ?? new();
        _options = options ?? new();
        _options.Validate();
        _cancellationToken = cancellationToken;
    }

    /// <summary>Resolves a file import against the importing module, retaining Jint's canonical URI identity.</summary>
    /// <param name="referencingModuleLocation">Importing module identity, or null for an entry import.</param>
    /// <param name="moduleRequest">Import specifier with an explicit supported extension and no attributes.</param>
    /// <returns>The resolved file identity; resolution does not read or prepare source.</returns>
    public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (moduleRequest.Attributes is { Length: > 0 })
            throw new NotSupportedException("TypeScript module loading does not support import attributes.");
        var resolved = _resolver.Resolve(referencingModuleLocation, moduleRequest);
        if (resolved.Type == SpecifierType.Bare) return resolved;
        if (resolved.Uri is not { IsFile: true })
            throw new NotSupportedException($"Use a relative file import with an explicit extension: '{moduleRequest.Specifier}'.");
        var path = resolved.Uri.LocalPath;
        if (path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"'{moduleRequest.Specifier}' is a declaration file. Use 'import type' to erase its dependency.");
        if (Path.GetExtension(path).ToLowerInvariant() is not (".ts" or ".mts" or ".js" or ".mjs"))
            throw new NotSupportedException($"Unsupported module extension in '{moduleRequest.Specifier}'. Use .ts, .mts, .js or .mjs.");
        return resolved;
    }

    /// <summary>Obtains cached prepared code and creates a new module record owned by the requesting engine.</summary>
    /// <param name="engine">Engine that will own the runtime module instance.</param>
    /// <param name="resolved">Identity returned by this loader's <see cref="Resolve"/> method.</param>
    /// <returns>An engine-specific module record with original TypeScript locations.</returns>
    /// <exception cref="TypeScriptParseException">The dependency contains invalid or unsupported syntax.</exception>
    /// <exception cref="ModuleGraphLimitException">The loader's source or module limit is exceeded.</exception>
    /// <exception cref="OperationCanceledException">The loader's cancellation token was canceled.</exception>
    public ModuleRecord LoadModule(Engine engine, ResolvedSpecifier resolved)
    {
        ArgumentNullException.ThrowIfNull(engine);
        // Re-resolve public input so direct callers cannot bypass the root/extension contract.
        resolved = Resolve(null, new ModuleRequest(resolved.Key, resolved.ModuleRequest.Attributes));
        if (resolved.Uri is null)
            throw new NotSupportedException($"Module '{resolved.Key}' is not registered by the host. Use a relative file import with an explicit extension.");
        Prepared<Module> prepared;
        lock (_gate)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_prepared.TryGetValue(resolved.Key, out prepared))
            {
                if (_prepared.Count >= _options.MaxModuleCount) throw Limit("Module count limit exceeded.");
                var source = _sources is null ? ReadSource(resolved.Uri!.LocalPath)
                    : _sources.TryGetValue(resolved.Key, out var text) ? text
                    : throw new FileNotFoundException($"Module '{resolved.ModuleRequest.Specifier}' was not found in the supplied files.", resolved.Key);
                prepared = _compiler.PrepareModule(source, resolved.Key, _cancellationToken);
                _prepared.Add(resolved.Key, prepared);
                if (_sources is null) _totalSourceLength += source.Length;
            }
        }
        return ModuleFactory.BuildSourceTextModule(engine, prepared);
    }

    private string ReadSource(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var max = Math.Min(_compiler.MaxSourceLength, _options.MaxTotalSourceLength - _totalSourceLength);
        var result = new StringBuilder(Math.Min(max, 4096));
        var buffer = new char[(int) Math.Min(max + 1L, 4096)];
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer, 0, (int) Math.Min(buffer.Length, max + 1L - result.Length));
            if (read == 0) return result.ToString();
            CheckSourceLength(result.Length + read, path);
            result.Append(buffer, 0, read);
        }
    }

    private void CheckSourceLength(int length, string name)
    {
        if (length > _compiler.MaxSourceLength) throw Limit($"Source length limit exceeded for '{name}'.");
        if (length > _options.MaxTotalSourceLength - _totalSourceLength) throw Limit("Total module source length limit exceeded.");
    }

    private static ModuleGraphLimitException Limit(string message) => new(message);
}
