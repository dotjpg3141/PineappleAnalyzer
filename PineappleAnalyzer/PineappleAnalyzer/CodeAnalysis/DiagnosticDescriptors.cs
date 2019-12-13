using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace PineappleAnalyzer.CodeAnalysis
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor RequiredDefaultSwitchLabel = new DiagnosticDescriptor(
            id: "PA0001",
            title: "Use default switch label.",
            messageFormat: "Use default switch label.",
            category: "Maintainability",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        public static readonly DiagnosticDescriptor RemoveUnnecessaryConditionsFromPredicate = new DiagnosticDescriptor(
            id: "PA0002",
            title: "Remove unnecessary conditions from predicate",
            messageFormat: "Remove unnecessary conditions from predicate",
            category: "Simplification",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true
        );
    }
}
