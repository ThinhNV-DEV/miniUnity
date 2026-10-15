// Rewrites float arithmetic to Mono semantics: intermediate float results are kept in double precision and only
// narrowed to float when stored (assignment, argument, return, field). Usage: monofloat <defines> <listOfAllSources> <listOfFilesToRewrite> <outDir>
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var defines = File.ReadAllText(args[0]).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var all = File.ReadAllLines(args[1]).Where(l => l.Length > 0).ToList();
var targets = new HashSet<string>(File.ReadAllLines(args[2]).Where(l => l.Length > 0));
var outDir = args[3];
var opts = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: defines);
var trees = all.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), opts, f)).ToList();
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var comp = CSharpCompilation.Create("x", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
int changed = 0;
foreach (var t in trees)
{
    if (!targets.Contains(t.FilePath)) continue;
    var model = comp.GetSemanticModel(t);
    var rw = new MonoFloat(model);
    var root = rw.Visit(t.GetRoot());
    var outPath = Path.Combine(outDir, t.FilePath.TrimStart('/'));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
    File.WriteAllText(outPath, root.ToFullString());
    changed += rw.Count;
}
Console.WriteLine($"rewrote {changed} float expression trees");

class MonoFloat : CSharpSyntaxRewriter
{
    readonly SemanticModel m; public int Count;
    public MonoFloat(SemanticModel model) { m = model; }

    bool IsFloat(ExpressionSyntax e) => m.GetTypeInfo(e).Type?.SpecialType == SpecialType.System_Single;
    static bool IsArith(SyntaxKind k) => k is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression;

    bool IsFloatArith(ExpressionSyntax e)
    {
        e = Unparen(e);
        if (e is BinaryExpressionSyntax b && IsArith(b.Kind()) && IsFloat(b)) return OpIsBuiltin(b);
        if (e is PrefixUnaryExpressionSyntax u && u.IsKind(SyntaxKind.UnaryMinusExpression) && IsFloat(u)) return IsFloatArith(u.Operand);
        return false;
    }
    bool OpIsBuiltin(BinaryExpressionSyntax b) => m.GetSymbolInfo(b).Symbol is not IMethodSymbol ms || ms.MethodKind == MethodKind.BuiltinOperator;
    static ExpressionSyntax Unparen(ExpressionSyntax e) { while (e is ParenthesizedExpressionSyntax p) e = p.Expression; return e; }

    // Build the double-precision version of a float arithmetic tree (operands are original nodes → visit leaves).
    ExpressionSyntax ToDouble(ExpressionSyntax e)
    {
        var u = Unparen(e);
        if (u is BinaryExpressionSyntax b && IsArith(b.Kind()) && IsFloat(b) && OpIsBuiltin(b))
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.BinaryExpression(b.Kind(), ToDouble(b.Left), ToDouble(b.Right)));
        if (u is PrefixUnaryExpressionSyntax pu && pu.IsKind(SyntaxKind.UnaryMinusExpression) && IsFloat(pu) && IsFloatArith(pu.Operand))
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, ToDouble(pu.Operand)));
        var leaf = (ExpressionSyntax)Visit(e);
        return SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword)), SyntaxFactory.ParenthesizedExpression(leaf.WithoutTrivia()));
    }

    bool HasArithChild(BinaryExpressionSyntax b) => IsFloatArith(b.Left) || IsFloatArith(b.Right);

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // root of a float arithmetic chain (≥2 ops): evaluate in double, narrow once
        if (IsArith(node.Kind()) && IsFloat(node) && OpIsBuiltin(node) && HasArithChild(node) && !IsFloatArith(ParentExpr(node)))
        {
            Count++;
            var d = ToDouble(node);
            if (ParentIsComparison(node)) return d.WithTriviaFrom(node);
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword)), d)).WithTriviaFrom(node);
        }
        // comparison of float chains: compare in double
        return base.VisitBinaryExpression(node);
    }

    ExpressionSyntax ParentExpr(SyntaxNode n)
    {
        var p = n.Parent;
        while (p is ParenthesizedExpressionSyntax) p = p.Parent;
        return p as ExpressionSyntax;
    }
    bool ParentIsComparison(SyntaxNode n) => ParentExpr(n) is BinaryExpressionSyntax b && b.Kind() is SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression;

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // x op= <float chain>  →  x = (float)((double)x op <chain in double>)
        if (node.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or SyntaxKind.DivideAssignmentExpression
            && IsFloat(node.Left) && IsFloatArith(node.Right) && (node.Left is IdentifierNameSyntax || node.Left is MemberAccessExpressionSyntax ma && ma.Expression is IdentifierNameSyntax or ThisExpressionSyntax)
            && m.GetSymbolInfo(node).Symbol is not IMethodSymbol { MethodKind: not MethodKind.BuiltinOperator })
        {
            Count++;
            var op = node.Kind() switch { SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression, SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression, SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression, _ => SyntaxKind.DivideExpression };
            var left = node.Left.WithoutTrivia();
            var dl = SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword)), SyntaxFactory.ParenthesizedExpression(left));
            var rhs = SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.FloatKeyword)),
                SyntaxFactory.ParenthesizedExpression(SyntaxFactory.BinaryExpression(op, dl, ToDouble(node.Right))));
            return SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, left, rhs).WithTriviaFrom(node);
        }
        return base.VisitAssignmentExpression(node);
    }
}
