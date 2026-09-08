using Xunit;

namespace Jint.TypeScript.Tests;

public class LimitTests
{
    [Theory]
    [InlineData("binary")]
    [InlineData("member")]
    [InlineData("call")]
    [InlineData("optional")]
    public void Deep_flat_trees_are_rejected_before_recursive_Jint_preparation(string kind)
    {
        var source = "x" + string.Concat(Enumerable.Repeat(kind switch
        {
            "binary" => " + x", "member" => ".x", "call" => "()", _ => "?.x"
        }, 10000));
        var compiler = new TypeScriptCompiler();
        compiler.ParseScript(source); // This is iterative, not parser recursion.
        Assert.Equal("AstDepthLimit", Assert.Throws<TypeScriptParseException>(() => compiler.PrepareScript(source)).Code);
        Assert.Equal("AstDepthLimit", Assert.Throws<TypeScriptParseException>(() => compiler.PrepareModule(source)).Code);
        Assert.Equal(42, new Engine().Evaluate(compiler.PrepareScript("const x: number = 42; x;")).AsNumber());
    }

    [Fact]
    public void Ast_depth_boundary_counts_root_and_children()
    {
        new TypeScriptCompiler(new() { MaxAstDepth = 3 }).PrepareScript("42");
        Assert.Equal("AstDepthLimit", Assert.Throws<TypeScriptParseException>(() =>
            new TypeScriptCompiler(new() { MaxAstDepth = 2 }).PrepareScript("42")).Code);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("tokens")]
    [InlineData("nodes")]
    [InlineData("types")]
    [InlineData("syntax")]
    [InlineData("ast")]
    public void Limits_must_be_positive(string kind)
    {
        var options = kind switch
        {
            "source" => new TypeScriptOptions { MaxSourceLength = 0 },
            "tokens" => new TypeScriptOptions { MaxTokenCount = 0 },
            "nodes" => new TypeScriptOptions { MaxNodeCount = 0 },
            "types" => new TypeScriptOptions { MaxTypeDepth = 0 },
            "syntax" => new TypeScriptOptions { MaxSyntaxDepth = 0 },
            _ => new TypeScriptOptions { MaxAstDepth = 0 }
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeScriptCompiler(options));
    }

    [Fact]
    public void Preparation_depth_cannot_disable_stack_protection() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypeScriptCompiler(new() { MaxAstDepth = 257 }));

    [Fact]
    public void Large_but_shallow_script_is_allowed()
    {
        var source = string.Join('\n', Enumerable.Range(0, 10000).Select(i => $"const x{i}: Array<number> = [{i}];"));
        new TypeScriptCompiler().PrepareScript(source);
    }

    [Fact]
    public async Task Cancellation_during_large_parse_is_observed()
    {
        // The task signals immediately before entry; cancellation is also valid at
        // entry if scheduling beats the first token. No brittle latency assertion.
        using var started = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var source = string.Concat(Enumerable.Repeat("{ let value: number = 1; value++; }\n", 200000));
        var compiler = new TypeScriptCompiler(new() { MaxSourceLength = 10_000_000, MaxNodeCount = 5_000_000, MaxTokenCount = 5_000_000 });
        var parse = Task.Run(() => { started.Set(); return compiler.ParseScript(source, cancellationToken: cancellation.Token); });
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await parse);
    }

    [Fact]
    public void Null_and_empty_sources_have_consistent_contracts()
    {
        var compiler = new TypeScriptCompiler();
        Assert.Throws<ArgumentNullException>(() => compiler.ParseScript(null!));
        Assert.Throws<ArgumentNullException>(() => compiler.ParseModule(null!));
        Assert.Empty(compiler.ParseScript("").Body);
        Assert.Empty(compiler.ParseModule("").Body);
        compiler.PrepareScript("");
        compiler.PrepareModule("");
    }
}
