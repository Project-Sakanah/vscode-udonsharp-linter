using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using UdonSharpLsp.Server.Contracts;
using UdonSharpLsp.Server.PolicyPacks;

namespace UdonSharpLsp.Server.Handlers;

[Method("udonsharp/rules/documentation")]
public sealed class RuleDocumentationHandler :
    IJsonRpcRequestHandler<RuleDocumentationRequest, RuleDocumentationResponse>,
    IRequestHandler<RuleDocumentationRequest, RuleDocumentationResponse>
{
    private readonly PolicyRepository _policyRepository;

    public RuleDocumentationHandler(PolicyRepository policyRepository)
    {
        _policyRepository = policyRepository;
    }

    public Task<RuleDocumentationResponse> Handle(RuleDocumentationRequest request, CancellationToken cancellationToken)
    {
        var locale = request.Locale;
        var rule = _policyRepository.GetRule(request.RuleId);
        if (rule is null)
        {
            return Task.FromResult(new RuleDocumentationResponse(request.RuleId, locale, request.RuleId, "Documentation not available."));
        }

        var markdown = rule.GetDocumentation(locale) ?? rule.GetDocumentation("en-US") ?? "Documentation not available.";
        return Task.FromResult(new RuleDocumentationResponse(rule.Id, locale, rule.Title, markdown));
    }
}
