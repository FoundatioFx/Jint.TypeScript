using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Mechanical public-API adaptation only. Grammar changes belong in owned partial
// files and the managed-file patch. Always run this on an isolated import directory.
if (args.Length != 1 || !Directory.Exists(args[0]))
    throw new ArgumentException("Usage: AdaptAst IMPORT_DIRECTORY");
foreach (var file in Directory.GetFiles(args[0], "*.cs", SearchOption.AllDirectories))
{
    var text = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(text);
    if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        throw new InvalidOperationException($"Cannot normalize invalid C#: {file}");
    var rewritten = new PublicApiRewriter(Path.GetFileName(file).StartsWith("Parser", StringComparison.Ordinal))
        .Visit(tree.GetRoot())!.ToFullString();
    File.WriteAllText(file, rewritten);
}

sealed class PublicApiRewriter(bool parserFile) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        node = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
        // Single-node and collection constructors share the public From factory.
        // Keep the array/count ownership constructor outside this mechanical rule.
        if (node is { Type: GenericNameSyntax { Identifier.ValueText: "NodeList" } listType,
            Initializer: null, ArgumentList.Arguments.Count: 1 })
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression($"NodeList.From{listType.TypeArgumentList}"), node.ArgumentList)
                .WithTriviaFrom(node);
        if (node.Type.ToString() is not ("Range" or "Position" or "SourceLocation" or "TemplateValue")) return node;
        // These shared value types have public factories but internal constructors.
        var factoryType = node.Type.ToString() == "TemplateValue" ? "Acornima.TemplateValue" : node.Type.ToString();
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression($"{factoryType}.From"), node.ArgumentList!)
            .WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        node = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        if (!parserFile) return node;
        if (node.Name.Identifier.ValueText is "_range" or "_location")
            return node.WithName(SyntaxFactory.IdentifierName(node.Name.Identifier.ValueText == "_range" ? "Range" : "Location")
                .WithTriviaFrom(node.Name));
        return node;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (node.Expression.ToString() == "NodeList.From" && node.ArgumentList.Arguments.Any(a => a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)))
            return node.WithExpression(SyntaxFactory.ParseExpression("ParserNodeList.From").WithTriviaFrom(node.Expression));
        var name = (node.Expression as SimpleNameSyntax)?.Identifier.ValueText;
        if (name is not ("FinishNode" or "FinishNodeAt" or "ReinterpretNode")) return node;
        var arguments = node.ArgumentList.Arguments;
        var index = name == "FinishNodeAt" ? 2 : 1;
        if (arguments.Count <= index) throw new InvalidOperationException($"Unexpected finalization call: {node}");
        var start = arguments[0].Expression.ToString();
        var end = name == "FinishNodeAt" ? arguments[1].Expression.ToString() : null;
        var range = name == "ReinterpretNode" ? $"{start}.Range" : $"NodeRange({start}{(end is null ? "" : ", " + end)})";
        var location = name == "ReinterpretNode" ? $"{start}.Location" : $"NodeLocation({start}{(end is null ? "" : ", " + end)})";
        var expression = Initialize(arguments[index].Expression, range, location);
        return node.WithArgumentList(node.ArgumentList.WithArguments(arguments.Replace(arguments[index], arguments[index].WithExpression(expression))));
    }

    static ExpressionSyntax Initialize(ExpressionSyntax expression, string range, string location) => expression switch
    {
        ObjectCreationExpressionSyntax { Initializer: null } creation => creation.WithoutTrailingTrivia().WithInitializer(
            SyntaxFactory.ParseExpression($"new X() {{ Range = {range}, Location = {location} }}")
                .DescendantNodes().OfType<InitializerExpressionSyntax>().Single().WithLeadingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(creation.GetTrailingTrivia()),
        ConditionalExpressionSyntax conditional => conditional.WithWhenTrue(Initialize(conditional.WhenTrue, range, location))
            .WithWhenFalse(Initialize(conditional.WhenFalse, range, location)),
        SwitchExpressionSyntax choice => choice.WithArms(SyntaxFactory.SeparatedList(choice.Arms.Select(
            arm => arm.WithExpression(Initialize(arm.Expression, range, location))), choice.Arms.GetSeparators())),
        _ => expression
    };
}
