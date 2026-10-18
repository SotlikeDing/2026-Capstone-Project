using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MobileApi.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireMobileCapabilityAttribute : TypeFilterAttribute
{
    public RequireMobileCapabilityAttribute(string capability)
        : base(typeof(RequireMobileCapabilityFilter)) => Arguments = [capability];
}

public sealed class RequireMobileCapabilityFilter : IAsyncAuthorizationFilter
{
    private readonly string _capability;
    private readonly IMobileAccessService _access;
    public RequireMobileCapabilityFilter(string capability, IMobileAccessService access)
    { _capability = capability; _access = access; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var caller = await _access.ResolveAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        if (!caller.Can(_capability)) context.Result = new ForbidResult();
    }
}
