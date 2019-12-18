using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PineappleAnalyzer.CodeAnalysis.CodeFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveUnnecessaryConditionsFromPredicateCodeFixProvider)), Shared]
    public class RemoveUnnecessaryConditionsFromPredicateCodeFixProvider : CodeFixProviderBase
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.Id);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);

            foreach (var diagnostic in context.Diagnostics)
            {
                var location = diagnostic.AdditionalLocations.ElementAt(0);
                var rootExpression = root?.FindNode(location.SourceSpan).FirstAncestorOrSelf<ExpressionSyntax>();
                if (rootExpression != null)
                {
                    var codeAction = CodeAction.Create(
                        title: DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.Title.ToString(),
                        createChangedDocument: c => RemoveUnnecessaryConditionsFromPredicateAsync(context.Document, root!, rootExpression, diagnostic, c),
                        equivalenceKey: DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.Title.ToString(CultureInfo.InvariantCulture)
                    );

                    context.RegisterCodeFix(codeAction, diagnostic);
                }
            }
        }

        private Task<Document> RemoveUnnecessaryConditionsFromPredicateAsync(Document document, SyntaxNode root, ExpressionSyntax rootExpression, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var expressionsToRemove = new HashSet<ExpressionSyntax>();
            foreach (var location in diagnostic.AdditionalLocations.Skip(1))
            {
                var expression = rootExpression.FindNode(location.SourceSpan).FirstAncestorOrSelf<ExpressionSyntax>();

                while (true)
                {
                    expression = IncludeParenthesisExpression(expression);
                    expressionsToRemove.Add(expression);

                    if (expression.Parent is BinaryExpressionSyntax binaryExpression
                        && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression)
                        && expressionsToRemove.Contains(binaryExpression.Left)
                        && expressionsToRemove.Contains(binaryExpression.Right))
                    {
                        expressionsToRemove.Remove(binaryExpression.Left);
                        expressionsToRemove.Remove(binaryExpression.Right);
                        expression = binaryExpression;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var newRootExpression = rootExpression.ReplaceNodes(expressionsToRemove.Select(expr => expr.Parent), (original, modified) =>
            {
                if (original is BinaryExpressionSyntax originalExpression
                    && modified is BinaryExpressionSyntax modifiedExpression)
                {
                    Debug.Assert(expressionsToRemove.Contains(originalExpression.Left) ^ expressionsToRemove.Contains(originalExpression.Right));

                    if (expressionsToRemove.Contains(originalExpression.Left))
                    {
                        modified = modifiedExpression.Right.WithoutTrivia();
                    }
                    else if (expressionsToRemove.Contains(originalExpression.Right))
                    {
                        modified = modifiedExpression.Left.WithoutTrivia();
                    }
                }

                return modified;
            });

            var newRoot = root.ReplaceNode(rootExpression, newRootExpression);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return Task.FromResult(newDocument);
        }

        private static ExpressionSyntax IncludeParenthesisExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            {
                expression = parenthesizedExpression.Expression;
            }
            return expression;
        }
    }
}
