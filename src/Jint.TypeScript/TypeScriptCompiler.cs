using Acornima.Ast;
using System.Buffers;
using Parser = Jint.TypeScript.Parsing.Parser;

namespace Jint.TypeScript;

/// <summary>Parses a bounded TypeScript subset directly into Acornima's JavaScript AST for Jint.</summary>
/// <remarks>Instances can be shared between threads. Cache prepared results when executing source repeatedly.</remarks>
public sealed class TypeScriptCompiler
{
    private readonly TypeScriptOptions _options;
    private readonly ScriptPreparationOptions _scriptPreparation;
    private readonly ModulePreparationOptions _modulePreparation;
    private readonly Acornima.ParserOptions _scriptParserOptions;
    private readonly Acornima.ParserOptions _moduleParserOptions;
    private readonly Parsing.ParserOptions _scriptGrammarOptions;
    private readonly Parsing.ParserOptions _moduleGrammarOptions;

    internal int MaxSourceLength => _options.MaxSourceLength;

    /// <summary>Creates a reusable compiler for the supported TypeScript syntax.</summary>
    /// <param name="options">Immutable parsing limits and preparation settings, or <see langword="null"/> for defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parsing limit is outside its supported range.</exception>
    public TypeScriptCompiler(TypeScriptOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
        _scriptPreparation = new()
        {
            StaticAnalysis = _options.StaticAnalysis,
            FoldConstants = _options.FoldConstants,
            ParsingOptions = new()
            {
                AllowReturnOutsideFunction = _options.AllowReturnOutsideFunction,
                CompileRegex = _options.CompileRegex,
                RegexTimeout = _options.RegexTimeout,
                MaxSourceLength = _options.MaxSourceLength,
                MaxNodeCount = _options.MaxNodeCount
            }
        };
        _modulePreparation = new()
        {
            StaticAnalysis = _options.StaticAnalysis,
            FoldConstants = _options.FoldConstants,
            ParsingOptions = new()
            {
                CompileRegex = _options.CompileRegex,
                RegexTimeout = _options.RegexTimeout,
                MaxSourceLength = _options.MaxSourceLength,
                MaxNodeCount = _options.MaxNodeCount
            }
        };
        // Jint exposes the options that attach its required regex metadata through this public API.
        // Resolve once per compiler; no JavaScript/TypeScript parser runs in a JavaScript engine.
        _scriptParserOptions = Engine.PrepareScript(string.Empty, options: _scriptPreparation).ParserOptions!;
        _moduleParserOptions = Engine.PrepareModule(string.Empty, options: _modulePreparation).ParserOptions!;
        _scriptGrammarOptions = CreateGrammarOptions(_scriptParserOptions, module: false);
        _moduleGrammarOptions = CreateGrammarOptions(_moduleParserOptions, module: true);
    }

    /// <summary>Parses a script and erases supported types into an official Acornima JavaScript AST.</summary>
    /// <param name="source">TypeScript source text, not a filename. Types are not checked or validated at runtime.</param>
    /// <param name="sourceFile">Optional source name recorded in original source locations and diagnostics.</param>
    /// <param name="cancellationToken">Cooperative parsing cancellation; does not configure subsequent Jint execution.</param>
    /// <returns>A script AST. Use <see cref="ParseModule"/> for import/export declarations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="TypeScriptParseException">Syntax is invalid, unsupported, or exceeds a parsing limit.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public Script ParseScript(string source, string? sourceFile = null, CancellationToken cancellationToken = default) =>
        (Script) Parse(source, sourceFile, module: false, cancellationToken);

    /// <summary>Parses a strict-mode module and erases supported types without resolving or loading its imports.</summary>
    /// <param name="source">TypeScript source text, not a filename. Use explicit type-only imports for erased dependencies.</param>
    /// <param name="sourceFile">Optional source identity. For relative imports, use the module loader's canonical resolved identity.</param>
    /// <param name="cancellationToken">Cooperative parsing cancellation; does not configure subsequent Jint execution.</param>
    /// <returns>An official Acornima JavaScript module AST with original UTF-16 source locations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="TypeScriptParseException">Syntax is invalid, unsupported, or exceeds a parsing limit.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public Module ParseModule(string source, string? sourceFile = null, CancellationToken cancellationToken = default) =>
        (Module) Parse(source, sourceFile, module: true, cancellationToken);

    /// <summary>Parses TypeScript, checks AST depth, and prepares its JavaScript AST for repeated Jint execution.</summary>
    /// <param name="source">TypeScript source text, not a filename. Preparation performs no type checking.</param>
    /// <param name="sourceFile">Optional source name used in original source locations and diagnostics.</param>
    /// <param name="cancellationToken">Cooperative cancellation during parsing/depth checks and before Jint preparation.</param>
    /// <returns>Prepared code that can be cached and shared across independent engines.</returns>
    /// <remarks>Configure execution limits and cancellation on each Jint engine separately. Repeated evaluation on one
    /// engine retains JavaScript globals and declaration rules. The compiler does not cache source or prepared results.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="TypeScriptParseException">Syntax or a parsing/preparation depth limit prevents preparation.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before Jint's preparation pass.</exception>
    public Prepared<Script> PrepareScript(string source, string? sourceFile = null, CancellationToken cancellationToken = default)
    {
        var script = ParseScript(source, sourceFile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePreparationDepth(script, cancellationToken);
        return Engine.PrepareScript(script, _scriptPreparation);
    }

    /// <summary>Prepares a TypeScript module for registration with Jint, without loading its dependencies.</summary>
    /// <param name="source">TypeScript source text, not a filename.</param>
    /// <param name="sourceFile">Optional module identity. Match the loader's resolved key when using relative imports.</param>
    /// <param name="cancellationToken">Cooperative cancellation during parsing/depth checks and before Jint preparation.</param>
    /// <returns>Prepared module code reusable across engines; each engine owns its runtime module instances.</returns>
    /// <remarks>Register the result with Jint's module builder or return it through a custom module loader.
    /// Ordinary Jint source-string loading still expects JavaScript. Configure execution limits separately.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="TypeScriptParseException">Syntax or a parsing/preparation depth limit prevents preparation.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before Jint's preparation pass.</exception>
    public Prepared<Module> PrepareModule(string source, string? sourceFile = null, CancellationToken cancellationToken = default)
    {
        var module = ParseModule(source, sourceFile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePreparationDepth(module, cancellationToken);
        return Engine.PrepareModule(module, _modulePreparation);
    }

    private void ValidatePreparationDepth(Node root, CancellationToken cancellationToken)
    {
        // A long flat operator/member chain parses iteratively but produces a deep
        // AST. Check it iteratively before Jint's recursive supplied-AST visitors.
        // Pool only this cleared traversal buffer; parser/tokenizer state is never pooled.
        var frames = ArrayPool<ChildNodes.Enumerator>.Shared.Rent(32);
        var depth = 1;
        var visited = 0;
        frames[0] = root.ChildNodes.GetEnumerator();
        try
        {
            while (depth > 0)
            {
                if ((++visited & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!frames[depth - 1].MoveNext())
                {
                    frames[--depth].Dispose();
                    continue;
                }
                var child = frames[depth - 1].Current;
                if (depth >= _options.MaxAstDepth)
                    throw new TypeScriptParseException("AstDepthLimit", "AST is too deep for Jint preparation",
                        child.Start, child.Location.Start, child.Location.SourceFile);
                if (depth == frames.Length)
                {
                    var larger = ArrayPool<ChildNodes.Enumerator>.Shared.Rent(depth * 2);
                    frames.AsSpan(0, depth).CopyTo(larger);
                    ArrayPool<ChildNodes.Enumerator>.Shared.Return(frames, clearArray: true);
                    frames = larger;
                }
                frames[depth++] = child.ChildNodes.GetEnumerator();
            }
        }
        finally
        {
            while (depth > 0) frames[--depth].Dispose();
            ArrayPool<ChildNodes.Enumerator>.Shared.Return(frames, clearArray: true);
        }
    }

    private Parsing.ParserOptions CreateGrammarOptions(Acornima.ParserOptions compatible, bool module) => new()
    {
        EcmaVersion = compatible.EcmaVersion,
        ExperimentalESFeatures = compatible.ExperimentalESFeatures & ~Acornima.ExperimentalESFeatures.Decorators,
        AllowReturnOutsideFunction = !module && _options.AllowReturnOutsideFunction,
        AllowTopLevelUsing = compatible.AllowTopLevelUsing,
        // Regex grammar and Jint metadata are handled by the official tokenizer for that leaf.
        OnRegExp = static (in Parsing.RegExpParsingContext _) => default
    };

    private Program Parse(string source, string? sourceFile, bool module, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length > _options.MaxSourceLength)
            throw new TypeScriptParseException("SourceLimit", "Source length limit exceeded", 0, Acornima.Position.From(1, 0), sourceFile);
        var parser = new Parser(module ? _moduleGrammarOptions : _scriptGrammarOptions)
        {
            RegexOptions = module ? _moduleParserOptions : _scriptParserOptions,
            Limits = _options,
            Cancellation = cancellationToken
        };
        try { return module ? parser.ParseModule(source, sourceFile) : parser.ParseScript(source, sourceFile, _options.Strict); }
        catch (InsufficientExecutionStackException exception)
        {
            throw new TypeScriptParseException("SyntaxDepthLimit", "Insufficient stack for syntax nesting",
                parser._tokenizer._start, parser._tokenizer._startLocation, sourceFile, exception);
        }
        catch (Parsing.ParseErrorException exception)
        {
            var error = exception.Error;
            throw new TypeScriptParseException(error.Code, error.Description, error.Index, error.Position, error.SourceFile, exception);
        }
        catch (Acornima.ParseErrorException exception)
        {
            var error = exception.Error;
            throw new TypeScriptParseException(error.Code, error.Description, error.Index, error.Position, error.SourceFile, exception);
        }
    }
}
