using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PineappleAnalyzer.CodeAnalysis.CodeFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequiredDefaultSwitchLabelCodeFixProvider)), Shared]
    public class RequiredDefaultSwitchLabelCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(DiagnosticDescriptors.RequiredDefaultSwitchLabel.Id);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);

            foreach (var diagostic in context.Diagnostics)
            {
                var location = diagostic.Location;

                var switchStatement = root?.FindNode(location.SourceSpan)?.FirstAncestorOrSelf<SwitchStatementSyntax>();
                if (switchStatement != null)
                {
                    var codeAction = CodeAction.Create(
                        title: DiagnosticDescriptors.RequiredDefaultSwitchLabel.Title.ToString(),
                        createChangedDocument: c => AddDefaultCaseAsync(context.Document, root!, switchStatement, c),
                        equivalenceKey: DiagnosticDescriptors.RequiredDefaultSwitchLabel.Title.ToString(CultureInfo.InvariantCulture)
                    );

                    context.RegisterCodeFix(codeAction, diagostic);
                }
            }
        }

        private Task<Document> AddDefaultCaseAsync(Document document, SyntaxNode root, SwitchStatementSyntax switchStatement, CancellationToken cancellationToken)
        {
            var defaultCase = SwitchSection()
                .WithLabels(SingletonList<SwitchLabelSyntax>(DefaultSwitchLabel()))
                .WithStatements(SingletonList<StatementSyntax>(BreakStatement()));

            var newSwitchStatement = switchStatement.AddSections(defaultCase);
            var newRoot = root.ReplaceNode(switchStatement, newSwitchStatement);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return Task.FromResult(newDocument);
        }
    }
}
