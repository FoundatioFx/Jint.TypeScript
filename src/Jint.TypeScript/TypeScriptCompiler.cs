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

    public Script ParseScript(string source, string? sourceFile = null, CancellationToken cancellationToken = default) =>
        (Script) Parse(source, sourceFile, module: false, cancellationToken);

    public Module ParseModule(string source, string? sourceFile = null, CancellationToken cancellationToken = default) =>
        (Module) Parse(source, sourceFile, module: true, cancellationToken);

    public Prepared<Script> PrepareScript(string source, string? sourceFile = null, CancellationToken cancellationToken = default)
    {
        var script = ParseScript(source, sourceFile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePreparationDepth(script, cancellationToken);
        return Engine.PrepareScript(script, _scriptPreparation);
    }

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
