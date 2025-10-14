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
public sealed class UshSynchronizationAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Supported = ImmutableArray.Create(
        UshRuleDescriptors.Ush0007,
        UshRuleDescriptors.Ush0008,
        UshRuleDescriptors.Ush0009,
        UshRuleDescriptors.Ush0010,
        UshRuleDescriptors.Ush0011,
        UshRuleDescriptors.Ush0012);

    private static readonly HashSet<string> SupportedSyncedTypes = new(StringComparer.Ordinal)
    {
        "bool",
        "byte",
        "sbyte",
        "short",
        "ushort",
        "int",
        "uint",
        "long",
        "ulong",
        "float",
        "double",
        "string",
        "UnityEngine.Vector2",
        "UnityEngine.Vector3",
        "UnityEngine.Vector4",
        "UnityEngine.Quaternion",
        "UnityEngine.Color",
        "UnityEngine.Color32",
        "UnityEngine.GameObject",
        "UnityEngine.Transform",
        "UnityEngine.Animator",
        "VRC.SDKBase.VRCPlayerApi"
    };

    private static readonly HashSet<string> LinearSupportedTypes = new(StringComparer.Ordinal)
    {
        "float",
        "UnityEngine.Vector2",
        "UnityEngine.Vector3",
        "UnityEngine.Vector4",
        "UnityEngine.Quaternion"
    };

    private static readonly HashSet<string> SmoothSupportedTypes = new(StringComparer.Ordinal)
    {
        "float",
        "int",
        "UnityEngine.Vector2",
        "UnityEngine.Vector3",
        "UnityEngine.Quaternion"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
    }

    private static void AnalyzeField(SyntaxNodeAnalysisContext context)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;
        var containingType = UshAnalyzerUtilities.GetContainingType(fieldDeclaration, context.SemanticModel, context.CancellationToken);
        if (containingType is null || !UshAnalyzerUtilities.IsUdonSharpBehaviour(containingType))
        {
            return;
        }

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) as IFieldSymbol;
            if (fieldSymbol is null)
            {
                continue;
            }

            var syncedAttribute = UshAnalyzerUtilities.GetAttribute(fieldSymbol, "UdonSynced");
            if (syncedAttribute is null)
            {
                continue;
            }

            AnalyzeSyncedField(context, fieldDeclaration, variable, fieldSymbol, containingType, syncedAttribute);
        }
    }

    private static void AnalyzeSyncedField(
        SyntaxNodeAnalysisContext context,
        FieldDeclarationSyntax declaration,
        VariableDeclaratorSyntax variable,
        IFieldSymbol fieldSymbol,
        INamedTypeSymbol behaviour,
        AttributeData syncedAttribute)
    {
        var fieldName = fieldSymbol.Name;
        var syncMode = GetSyncMode(syncedAttribute);
        var behaviourSyncMode = UshAnalyzerUtilities.GetBehaviourSyncMode(behaviour);
        var fieldTypeName = GetTypeName(fieldSymbol.Type);

        if (string.Equals(behaviourSyncMode, "BehaviourSyncMode.NoVariableSync", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0007,
                variable.GetLocation(),
                fieldName));
        }

        if (!IsSupportedSyncedType(fieldSymbol.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0008,
                variable.GetLocation(),
                fieldName,
                fieldTypeName));
        }

        if (fieldSymbol.Type is IArrayTypeSymbol && !string.Equals(behaviourSyncMode, "BehaviourSyncMode.Manual", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0009,
                variable.GetLocation(),
                fieldName));
        }

        if (string.Equals(behaviourSyncMode, "BehaviourSyncMode.Manual", StringComparison.Ordinal) &&
            (string.Equals(syncMode, "UdonSyncMode.Linear", StringComparison.Ordinal) ||
             string.Equals(syncMode, "UdonSyncMode.Smooth", StringComparison.Ordinal)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0010,
                variable.GetLocation(),
                syncMode,
                fieldName));
        }

        if (string.Equals(syncMode, "UdonSyncMode.Linear", StringComparison.Ordinal) &&
            !IsSupportedLinearType(fieldSymbol.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0011,
                variable.GetLocation(),
                fieldTypeName));
        }

        if (string.Equals(syncMode, "UdonSyncMode.Smooth", StringComparison.Ordinal) &&
            !IsSupportedSmoothType(fieldSymbol.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UshRuleDescriptors.Ush0012,
                variable.GetLocation(),
                fieldTypeName));
        }
    }

    private static bool IsSupportedSyncedType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedSyncedType(array.ElementType);
        }

        if (type is INamedTypeSymbol named && named.IsGenericType && named.Name.Equals("Nullable", StringComparison.Ordinal))
        {
            type = named.TypeArguments[0];
        }

        var name = GetTypeName(type);
        return SupportedSyncedTypes.Contains(name);
    }

    private static bool IsSupportedLinearType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedLinearType(array.ElementType);
        }

        return LinearSupportedTypes.Contains(GetTypeName(type));
    }

    private static bool IsSupportedSmoothType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return IsSupportedSmoothType(array.ElementType);
        }

        return SmoothSupportedTypes.Contains(GetTypeName(type));
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static string GetSyncMode(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length > 0)
        {
            var value = attribute.ConstructorArguments[0].Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                return value!;
            }
        }

        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, "SyncMode", StringComparison.Ordinal) && named.Value.Value is object value)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
