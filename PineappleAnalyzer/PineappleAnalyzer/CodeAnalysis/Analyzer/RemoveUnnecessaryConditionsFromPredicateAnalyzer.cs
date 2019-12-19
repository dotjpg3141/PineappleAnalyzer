using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PineappleAnalyzer.CodeAnalysis.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RemoveUnnecessaryConditionsFromPredicateAnalyzer : DiagnosticAnalyzerBase
    {
        private static readonly ImmutableArray<string> queryableTypeNames = ImmutableArray.Create("System.Linq.Queryable");
        private static readonly ImmutableArray<string> primaryKeyAttributeTypeNames = ImmutableArray.Create("System.ComponentModel.DataAnnotations.KeyAttribute");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
            context.RegisterCompilationStartAction(CompilationStartAction);
        }

        private void CompilationStartAction(CompilationStartAnalysisContext context)
        {
            var queryableTypes = ResolveTypes(queryableTypeNames);
            if (queryableTypes.Count == 0)
            {
                return;
            }

            var primaryKeyAttributeTypes = ResolveTypes(primaryKeyAttributeTypeNames);
            if (primaryKeyAttributeTypes.Count == 0)
            {
                return;
            }

            var analzyer = new Analyzer(queryableTypes, primaryKeyAttributeTypes);
            context.RegisterSyntaxNodeAction(analzyer.AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);

            ImmutableHashSet<INamedTypeSymbol> ResolveTypes(IEnumerable<string> typeNames)
            {
                return typeNames
                    .Select(name => context.Compilation.GetTypeByMetadataName(name))
                    .Where(symbol => symbol != null)
                    .ToImmutableHashSet();
            }
        }

        private class Analyzer
        {
            public ImmutableHashSet<INamedTypeSymbol> QueryableTypes { get; }

            public ImmutableHashSet<INamedTypeSymbol> PrimaryKeyAttributesTypes { get; }

            public Analyzer(ImmutableHashSet<INamedTypeSymbol> queryableTypes, ImmutableHashSet<INamedTypeSymbol> primaryKeyAttributesTypes)
            {
                QueryableTypes = queryableTypes;
                PrimaryKeyAttributesTypes = primaryKeyAttributesTypes;
            }

            public void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
            {
                var invocationExpression = (InvocationExpressionSyntax)context.Node;

                if (!(invocationExpression.Expression is MemberAccessExpressionSyntax memberAccessExpression))
                {
                    return;
                }

                var methodName = memberAccessExpression.Name.Identifier;

                switch (methodName.ValueText)
                {
                    case "Where":
                    {
                        var methodInfo = context.SemanticModel.GetSymbolInfo(memberAccessExpression, context.CancellationToken);
                        if (methodInfo.Symbol is IMethodSymbol methodSymbol
                            && QueryableTypes.Contains(methodSymbol.ContainingType)
                            && methodSymbol.Parameters.Length == 1
                            && invocationExpression.ArgumentList.Arguments.Count == 1
                            && invocationExpression.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambdaExpression)
                        {
                            AnalyzePredicate(context, methodName, lambdaExpression);
                        }

                        break;
                    }

                    default:
                        break;
                }
            }

            private void AnalyzePredicate(SyntaxNodeAnalysisContext context, SyntaxToken methodName, LambdaExpressionSyntax lambdaExpression)
            {
                if (GetParameters(lambdaExpression) is var parameters
                    && parameters.Length == 1
                    && lambdaExpression.ExpressionBody is ExpressionSyntax body)
                {
                    var parameterName = parameters[0].Identifier.ValueText;

                    var operands = GetBinaryExpressionOperands(body, SyntaxKind.LogicalAndExpression)
                        .Select(operand =>
                        {
                            var propertyName = GetEqualParameterPropertyExpression(operand, parameterName);
                            IPropertySymbol? propertySymbol = null;

                            if (propertyName != null)
                            {
                                var propertyInfo = context.SemanticModel.GetSymbolInfo(propertyName, context.CancellationToken);
                                propertySymbol = propertyInfo.Symbol as IPropertySymbol;
                            }

                            return new BinaryOperatorOperand(operand, propertySymbol);
                        })
                        .ToList();

                    if (operands.Count == 0)
                    {
                        return;
                    }

                    var columns = operands
                        .Where(c => c.ColumnSymbol != null)
                        .Select(c => c.ColumnSymbol!)
                        .ToList();

                    // TODO(jpg): check if this is true for inherited properties
                    Debug.Assert(
                        columns.Select(c => c.ContainingType).Distinct().Count() <= 1,
                        "All properties must be contained in the same type."
                    );

                    var type = columns[0].ContainingType;
                    var declaredPrimaryKeys = type.GetMembers().OfType<IPropertySymbol>().Where(IsPrimaryKey);
                    var usedPrimaryKeys = columns.Where(IsPrimaryKey);

                    // TODO(jpg): check if this is true for inherited properties
                    Debug.Assert(
                        !usedPrimaryKeys.Except(declaredPrimaryKeys).Any(),
                        "usedPrimaryKeys ⊆ declaredPrimaryKeys"
                    );

                    var unusedPrimaryKeys = declaredPrimaryKeys.Except(usedPrimaryKeys);
                    if (unusedPrimaryKeys.Any())
                    {
                        return;
                    }

                    var usedNonPrimaryKeys = operands
                        .Where(c => c.ColumnSymbol == null || !IsPrimaryKey(c.ColumnSymbol))
                        .ToList();

                    if (usedNonPrimaryKeys.Count == 0)
                    {
                        return;
                    }

                    var diagnostic = Diagnostic.Create(
                         descriptor: DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate,
                         location: methodName.GetLocation(),
                         additionalLocations: new[] { lambdaExpression.GetLocation() }.Concat(usedNonPrimaryKeys.Select(c => c.Expression.GetLocation()))
                    );

                    context.ReportDiagnostic(diagnostic);
                }
            }

            private bool IsPrimaryKey(IPropertySymbol property)
            {
                return property.GetAttributes()
                    .Any((attribute) => PrimaryKeyAttributesTypes.Contains(attribute.AttributeClass));
            }
        }

        private static IEnumerable<ExpressionSyntax> GetBinaryExpressionOperands(ExpressionSyntax expression, SyntaxKind kind)
        {
            var todo = new Stack<ExpressionSyntax>();
            todo.Push(expression);

            while (todo.Count != 0)
            {
                var currentExpression = IgnoreParenthesisExpression(todo.Pop());

                if (currentExpression.IsKind(kind))
                {
                    var binaryExpression = (BinaryExpressionSyntax)currentExpression;
                    todo.Push(binaryExpression.Left);
                    todo.Push(binaryExpression.Right);
                }
                else
                {
                    yield return currentExpression;
                }
            }
        }

        private static ExpressionSyntax IgnoreParenthesisExpression(ExpressionSyntax expression)
        {
            while (expression.IsKind(SyntaxKind.ParenthesizedExpression))
            {
                expression = ((ParenthesizedExpressionSyntax)expression).Expression;
            }
            return expression;
        }

        private static ImmutableArray<ParameterSyntax> GetParameters(LambdaExpressionSyntax lambdaExpression)
        {
            switch (lambdaExpression.Kind())
            {
                case SyntaxKind.SimpleLambdaExpression:
                    return ImmutableArray.Create(((SimpleLambdaExpressionSyntax)lambdaExpression).Parameter);

                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)lambdaExpression).ParameterList.Parameters.ToImmutableArray();

                default:
                    Debug.Fail("Unreachable");
                    return ImmutableArray<ParameterSyntax>.Empty;
            }
        }

        private static SimpleNameSyntax? GetEqualParameterPropertyExpression(ExpressionSyntax expression, string parameterName)
        {
            // matches: <parameterName>.<Property> == <Expression>

            if (!expression.IsKind(SyntaxKind.EqualsExpression))
            {
                return null;
            }

            var binaryExpression = (BinaryExpressionSyntax)expression;

            var left = GetPropertyIdentifier(binaryExpression.Left, parameterName);
            var right = GetPropertyIdentifier(binaryExpression.Right, parameterName);

            if (left != null && right != null)
            {
                return null;
            }

            return left ?? right;
        }

        private static SimpleNameSyntax? GetPropertyIdentifier(ExpressionSyntax expression, string parameterName)
        {
            // matches: <name>.<Property>

            if (!expression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                return null;
            }

            var memberAccessExpression = (MemberAccessExpressionSyntax)expression;

            if (memberAccessExpression.Expression is IdentifierNameSyntax identifierName
                && identifierName.Identifier.ValueText == parameterName)
            {
                return memberAccessExpression.Name;
            }

            return null;
        }

        private sealed class BinaryOperatorOperand
        {
            public BinaryOperatorOperand(ExpressionSyntax expression, IPropertySymbol? columnSymbol)
            {
                Expression = expression;
                ColumnSymbol = columnSymbol;
            }

            public ExpressionSyntax Expression { get; }
            public IPropertySymbol? ColumnSymbol { get; }
        }
    }
}
