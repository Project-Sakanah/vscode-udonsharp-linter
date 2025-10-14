using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UdonSharpLsp.Server.Diagnostics.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UshFieldChangeCallbackAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0040,
        UshRuleDescriptors.Ush0041,
        UshRuleDescriptors.Ush0042);

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

        var seenTargets = new Dictionary<string, IFieldSymbol>(StringComparer.Ordinal);

        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var attribute = UshAnalyzerUtilities.GetFieldChangeCallbackAttribute(field, context.CancellationToken);
            if (attribute is null)
            {
                continue;
            }

            if (!TryGetCallbackTarget(context, attribute, out var targetName, out var attributeSyntax))
            {
                continue;
            }

            if (seenTargets.ContainsKey(targetName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0040,
                    attributeSyntax.GetLocation(),
                    targetName));
                continue;
            }

            seenTargets[targetName] = field;

            var propertySymbol = type
                .GetMembers(targetName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            if (propertySymbol is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0041,
                    attributeSyntax.GetLocation(),
                    targetName));
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(field.Type, propertySymbol.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UshRuleDescriptors.Ush0042,
                    attributeSyntax.GetLocation(),
                    targetName,
                    field.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }
        }
    }

    private static bool TryGetCallbackTarget(
        SymbolAnalysisContext context,
        AttributeData attribute,
        out string targetName,
        out AttributeSyntax attributeSyntax)
    {
        targetName = string.Empty;
        attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) as AttributeSyntax;
        if (attributeSyntax is null)
        {
            return false;
        }

        var semanticModel = context.Compilation.GetSemanticModel(attributeSyntax.SyntaxTree);
        return UshAnalyzerUtilities.TryGetAttributeStringArgument(attribute, attributeSyntax, semanticModel, context.CancellationToken, out targetName);
    }
}
