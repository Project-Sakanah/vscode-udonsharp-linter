using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshStructureAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0044,
        UshRuleDescriptors.Ush0045);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type ||
            !UshAnalyzerUtilities.IsUdonSharpBehaviour(type))
        {
            return;
        }

        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax syntax)
            {
                continue;
            }

            if (syntax.Parent is not NamespaceDeclarationSyntax && syntax.Parent is not FileScopedNamespaceDeclarationSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0044,
                    syntax.Identifier.GetLocation()));
            }

            if (type.IsAbstract)
            {
                continue;
            }

            var filePath = syntax.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!string.Equals(fileName, type.Name, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0045,
                    syntax.Identifier.GetLocation(),
                    type.Name,
                    fileName));
            }
        }
    }
}
