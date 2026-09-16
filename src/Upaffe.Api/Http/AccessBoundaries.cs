using Microsoft.AspNetCore.Authorization;
using Upaffe.Application.Failures;
using Upaffe.Domain.Access;

namespace Upaffe.Api.Http;

/// <summary>The one access boundary every routed endpoint must declare.</summary>
public enum AccessBoundary
{
    Public,
    Browser,
    Management,
}

/// <summary>Records a boundary in endpoint metadata and applies its authorization contract.</summary>
public static class AccessBoundaryExtensions
{
    public static Identity ActingIdentity(this HttpContext context) =>
        context.Features.Get<Identity>() ?? throw Refusal.AuthenticationRequired();

    public static T PublicAccess<T>(this T builder) where T : IEndpointConventionBuilder
    {
        builder.AllowAnonymous();
        return Classified(builder, AccessBoundary.Public);
    }

    public static T BrowserAccess<T>(this T builder) where T : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(BrowserAuthentication.Policy);
        return Classified(builder, AccessBoundary.Browser);
    }

    public static T ManagementAccess<T>(this T builder) where T : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(BrowserAuthentication.ManagementPolicy);
        return Classified(builder, AccessBoundary.Management);
    }

    private static T Classified<T>(T builder, AccessBoundary boundary)
        where T : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(boundary));
        return builder;
    }
}
