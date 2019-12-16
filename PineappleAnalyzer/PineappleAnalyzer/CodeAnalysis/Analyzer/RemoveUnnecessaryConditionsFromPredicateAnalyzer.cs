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
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction((startContext) =>
            {
                var registerActions = false;

                if (!registerActions)
                {
                    var dbContextSymbol = startContext.Compilation.GetTypeByMetadataName("System.Data.Entity.DbContext");
                    registerActions = dbContextSymbol != null;
                }

                if (registerActions)
                {
                    startContext.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
                }
            });
        }

        private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
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
                        && methodSymbol.ContainingType.Name == "Queryable"
                        && methodSymbol.ContainingType.ContainingNamespace?.Name == "Linq"
                        && methodSymbol.ContainingType.ContainingNamespace.ContainingNamespace?.Name == "System"
                        && methodSymbol.ContainingType.ContainingNamespace.ContainingNamespace.ContainingNamespace?.IsGlobalNamespace == true
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

        private static void AnalyzePredicate(SyntaxNodeAnalysisContext context, SyntaxToken methodName, LambdaExpressionSyntax lambdaExpression)
        {
            if (GetParameters(lambdaExpression) is var parameters
                && parameters.Length == 1
                && lambdaExpression.ExpressionBody is ExpressionSyntax body)
            {
                var parameterName = parameters[0].Identifier.ValueText;

                List<IPropertySymbol> properties = GetBinaryExpressionOperands(body, SyntaxKind.LogicalAndExpression)
                    .Select(operand =>
                    {
                        var propertyName = GetEqualParameterPropertyExpression(operand, parameterName);
                        if (propertyName == null)
                        {
                            return null;
                        }

                        var propertyInfo = context.SemanticModel.GetSymbolInfo(propertyName, context.CancellationToken);
                        return propertyInfo.Symbol as IPropertySymbol;
                    })
                    .Where(operand => operand != null)
                    .ToList()!;

                if (properties.Count == 0)
                {
                    return;
                }

                // TODO(jpg): check if this is true for inherited properties
                Debug.Assert(
                    properties.Select(p => p.ContainingType).Distinct().Count() <= 1,
                    "All properties must be contained in the same type."
                );

                var type = properties[0].ContainingType;
                var declaredPrimaryKeys = type.GetMembers().OfType<IPropertySymbol>().Where(IsPrimaryKey);
                var usedPrimaryKeys = properties.Where(IsPrimaryKey);

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

                var usedNonPrimaryKeys = properties.Where(p => !IsPrimaryKey(p)).ToList();
                if (usedNonPrimaryKeys.Count == 0)
                {
                    return;
                }

                var diagnostic = Diagnostic.Create(
                     descriptor: DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate,
                     location: methodName.GetLocation()
                );

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static IEnumerable<ExpressionSyntax> GetBinaryExpressionOperands(ExpressionSyntax expression, SyntaxKind kind)
        {
            var todo = new Stack<ExpressionSyntax>();
            todo.Push(expression);

            while (todo.Count != 0)
            {
                var currentExpression = IgnoreParenthesisExpression(todo.Pop());

                if (currentExpression != null)
                {
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

        private static bool IsPrimaryKey(IPropertySymbol property)
        {
            return property.GetAttributes().Any(IsPrimaryKeyAttribute);

            bool IsPrimaryKeyAttribute(AttributeData attribute)
            {
                return attribute.AttributeClass.Name == "KeyAttribute"
                    && attribute.AttributeClass.ContainingNamespace?.Name == "DataAnnotations"
                    && attribute.AttributeClass.ContainingNamespace.ContainingNamespace?.Name == "ComponentModel"
                    && attribute.AttributeClass.ContainingNamespace.ContainingNamespace.ContainingNamespace?.Name == "System"
                    && attribute.AttributeClass.ContainingNamespace.ContainingNamespace.ContainingNamespace.ContainingNamespace?.IsGlobalNamespace == true;
            }
        }
    }
}
