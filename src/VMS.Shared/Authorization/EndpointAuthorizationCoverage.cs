using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace VMS.Shared.Authorization;

/// <summary>
/// Default deny, enforced (BR-SEC-005, BR-SEC-006). Every controller action must state how it is
/// protected. Nothing here grants access; it only refuses to start the application when an action
/// forgot to say, so a missing attribute is caught in development and in CI, never in production.
/// </summary>
public static class EndpointAuthorizationCoverage
{
    /// <summary>True when the metadata carries an explicit statement of who may call the endpoint.</summary>
    public static bool IsExplicit(IEnumerable<object> endpointMetadata)
    {
        var metadata = endpointMetadata as IReadOnlyCollection<object> ?? endpointMetadata.ToList();

        if (metadata.OfType<IAllowAnonymous>().Any()) return true;
        if (metadata.OfType<AuthenticatedOnlyAttribute>().Any()) return true;

        return metadata.OfType<IAuthorizeData>().Any(a =>
            a.Policy is { } policy &&
            (policy == RequireSuperAdminAttribute.PolicyName ||
             policy.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal)));
    }

    /// <summary>The controller actions that do not say how they are protected, as "Controller.Action".</summary>
    public static IReadOnlyList<string> FindUnprotected(IEnumerable<ActionDescriptor> actions) =>
        actions.OfType<ControllerActionDescriptor>()
            .Where(a => !IsExplicit(a.EndpointMetadata))
            .Select(a => $"{a.ControllerName}.{a.ActionName} ({a.AttributeRouteInfo?.Template ?? a.ControllerName})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>Call once after the app is built. Throws, listing every offender, if any action is unprotected.</summary>
    public static void VerifyEndpointAuthorization(this IApplicationBuilder app)
    {
        var actions = app.ApplicationServices.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        var unprotected = FindUnprotected(actions);
        if (unprotected.Count == 0) return;

        throw new InvalidOperationException(
            "Every endpoint must declare how it is protected: [RequirePermission(...)], [RequireSuperAdmin], " +
            "[AuthenticatedOnly] or [AllowAnonymous]. These do not:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", unprotected));
    }
}
