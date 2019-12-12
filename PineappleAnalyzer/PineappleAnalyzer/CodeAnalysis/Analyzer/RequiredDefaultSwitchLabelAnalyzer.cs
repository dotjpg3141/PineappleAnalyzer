using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PineappleAnalyzer.CodeAnalysis.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RequiredDefaultSwitchLabelAnalyzer : DiagnosticAnalyzerBase
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.RequiredDefaultSwitchLabel);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(AnalyzeSwitchStatement, SyntaxKind.SwitchStatement);
        }

        private static void AnalyzeSwitchStatement(SyntaxNodeAnalysisContext context)
        {
            var switchStatement = (SwitchStatementSyntax)context.Node;
            if (!switchStatement.Sections.Any(HasDefaultSwitchLabel))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.RequiredDefaultSwitchLabel,
                    switchStatement.SwitchKeyword.GetLocation()
                );

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static bool HasDefaultSwitchLabel(SwitchSectionSyntax switchSection)
        {
            return switchSection.Labels.Any(IsDefaultLabel);
        }

        private static bool IsDefaultLabel(SwitchLabelSyntax switchLabel)
        {
            switch (switchLabel.Kind())
            {
                case SyntaxKind.DefaultSwitchLabel:
                    return true;

                default:
                    return false;
            }
        }
    }
}
