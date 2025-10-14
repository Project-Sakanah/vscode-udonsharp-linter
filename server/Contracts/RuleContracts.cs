using System.Collections.Generic;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace UdonSharpLsp.Server.Contracts;

public sealed record RuleDescriptorDto(
    string Id,
    string Title,
    string Category,
    DiagnosticSeverity DefaultSeverity,
    string Description,
    string? HelpLink,
    bool HasCodeFix,
    Dictionary<string, DiagnosticSeverity>? ProfileSeverity
);

public sealed record ListRulesRequest : IRequest<RuleDescriptorDto[]>, IJsonRpcRequest
{
    public string Method => "udonsharp/rules/list";
}

public sealed record RuleDocumentationRequest : IRequest<RuleDocumentationResponse>, IJsonRpcRequest
{
    public string Method => "udonsharp/rules/documentation";
    public required string RuleId { get; init; }
    public required string Locale { get; init; }
}

public sealed record RuleDocumentationResponse(
    string Id,
    string Locale,
    string Title,
    string Markdown
);

public sealed record ServerStatusRequest : IRequest<ServerStatusResponse>, IJsonRpcRequest
{
    public string Method => "udonsharp/server/status";
}

public sealed record ServerStatusResponse(
    string Profile,
    int DisabledRuleCount,
    int TotalRuleCount,
    string ServerVersion
);
