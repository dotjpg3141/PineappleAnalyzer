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
        private static readonly ImmutableArray<string> queryableTypeNames = ImmutableArray.Create("System.Linq.Queryable", "System.Data.Entity.QueryableExtensions");
        private static readonly ImmutableArray<string> primaryKeyAttributeTypeNames = ImmutableArray.Create("System.ComponentModel.DataAnnotations.KeyAttribute");
        private static readonly string ExpressionTypeName = typeof(System.Linq.Expressions.Expression<>).FullName;
        private static readonly string FuncTypeName = typeof(Func<,>).FullName;

        private static readonly ImmutableHashSet<string> queryMethodNames = ImmutableHashSet.Create(
            "All", "AllAync",
            "Any", "AnyAsync",
            "Contains", "ContainsAsync",
            "Count", "CountAsync",
            "First", "FirstAsync",
            "FirstOrDefault", "FirstOrDefaultAsync",
            "Last", "LastAsync",
            "LastOrDefault", "LastOrDefaultAsync",
            "LongCount", "LongCountAsync",
            "Single", "SingleAsync",
            "SingleOrDefault", "SingleOrDefaultAsync",
            "Where"
        );

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
            var expressionType = context.Compilation.GetTypeByMetadataName(ExpressionTypeName);
            if (expressionType == null)
            {
                return;
            }

            var funcType = context.Compilation.GetTypeByMetadataName(FuncTypeName);
            if (funcType == null)
            {
                return;
            }

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

            var analzyer = new Analyzer(expressionType, funcType, queryableTypes, primaryKeyAttributeTypes);
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
            public INamedTypeSymbol ExpressionType { get; }
            public INamedTypeSymbol FuncType { get; }

            public ImmutableHashSet<INamedTypeSymbol> QueryableTypes { get; }

            public ImmutableHashSet<INamedTypeSymbol> PrimaryKeyAttributesTypes { get; }

            public Analyzer(INamedTypeSymbol expressionType, INamedTypeSymbol funcType, ImmutableHashSet<INamedTypeSymbol> queryableTypes, ImmutableHashSet<INamedTypeSymbol> primaryKeyAttributesTypes)
            {
                ExpressionType = expressionType;
                FuncType = funcType;
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
                if (!queryMethodNames.Contains(methodName.ValueText))
                {
                    return;
                }

                var methodInfo = context.SemanticModel.GetSymbolInfo(memberAccessExpression, context.CancellationToken);
                if (methodInfo.Symbol is IMethodSymbol methodSymbol
                    && QueryableTypes.Contains(methodSymbol.ContainingType)
                    && methodSymbol.Parameters.Length >= 1
                    && invocationExpression.ArgumentList.Arguments.Count >= 1)
                {
                    foreach (var argument in invocationExpression.ArgumentList.Arguments)
                    {
                        if (argument.Expression is LambdaExpressionSyntax lambdaExpression)
                        {
                            var typeInfo = context.SemanticModel.GetTypeInfo(argument.Expression);
                            if (typeInfo.ConvertedType != null && IsPredicate(typeInfo.ConvertedType))
                            {
                                AnalyzePredicate(context, methodName, lambdaExpression);
                            }
                        }
                    }

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

            private bool IsPredicate(ITypeSymbol type)
            {
                if (!ExpressionType.Equals(type.OriginalDefinition, SymbolEqualityComparer.Default)
                    || !(type is INamedTypeSymbol namedType))
                {
                    return false;
                }

                var actualFuncType = namedType.TypeArguments[0];
                if (!FuncType.Equals(actualFuncType.OriginalDefinition, SymbolEqualityComparer.Default)
                    || !(actualFuncType is INamedTypeSymbol actualFuncNamedType))
                {
                    return false;
                }

                var returnType = actualFuncNamedType.TypeArguments[1];
                if (returnType.SpecialType != SpecialType.System_Boolean)
                {
                    return false;
                }

                return true;
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
