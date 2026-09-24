using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VMS.Shared.Authorization;

/// <summary>
/// Marks a response property as visible only to callers who hold the permission (BR-SEC-001). The
/// value is left out of the JSON entirely, on the server, so it never reaches the browser: hiding it
/// with CSS would still leave it in the network tab. The UI shows a dash for an absent value, never a
/// zero, so nobody mistakes a permission for a number (BR-SEC-004).
/// <code>
/// [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)]
/// public decimal? PurchasePrice { get; set; }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FieldPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>Who is asking, for the current request. Set by <see cref="FieldPermissionMiddleware"/>.</summary>
public static class FieldPermissionContext
{
    private static readonly AsyncLocal<ClaimsPrincipal?> Holder = new();

    public static ClaimsPrincipal? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }

    /// <summary>
    /// Fails closed: with no caller (a background job, a test) restricted fields are hidden. Code that
    /// legitimately needs the value serialises a model without the attribute.
    /// </summary>
    public static bool CanView(string permission) => Current is { } user && user.HasPermission(permission);
}

public static class FieldPermissionRules
{
    /// <summary>
    /// The properties of <paramref name="type"/> the caller may not see. Exports, printouts and any
    /// non-JSON output use this so they follow exactly the same rules as the screen (BR-SEC-003).
    /// </summary>
    public static IReadOnlyList<PropertyInfo> HiddenProperties(Type type, ClaimsPrincipal user) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<FieldPermissionAttribute>() is { } a && !user.HasPermission(a.Permission))
            .ToList();

    /// <summary>System.Text.Json contract modifier that drops restricted properties from responses.</summary>
    public static void HideRestrictedProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            var attribute = property.AttributeProvider?.GetCustomAttributes(typeof(FieldPermissionAttribute), inherit: true)
                .OfType<FieldPermissionAttribute>().FirstOrDefault();
            if (attribute is null) continue;

            var existing = property.ShouldSerialize;
            var permission = attribute.Permission;
            property.ShouldSerialize = (instance, value) =>
                FieldPermissionContext.CanView(permission) && (existing?.Invoke(instance, value) ?? true);
        }
    }
}

/// <summary>Puts the signed-in user where the JSON serialiser can see them. Place it after authentication.</summary>
public sealed class FieldPermissionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        FieldPermissionContext.Current = context.User;
        try
        {
            await next(context);
        }
        finally
        {
            FieldPermissionContext.Current = null;
        }
    }
}

public static class FieldPermissionExtensions
{
    public static IApplicationBuilder UseFieldPermissions(this IApplicationBuilder app) =>
        app.UseMiddleware<FieldPermissionMiddleware>();
}
