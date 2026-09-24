using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;
using Xunit;

namespace VMS.Tests.Infrastructure;

/// <summary>Adds exactly one probe controller to a test host, so the others never leak into the real API.</summary>
public sealed class SingleControllerProvider(Type controller) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
        feature.Controllers.Add(controller.GetTypeInfo());
}

public static class ProbeHosts
{
    public static WebApplicationFactoryBuilder With<T>(this ApiFactory factory) where T : ControllerBase => new(factory, typeof(T));

    public sealed class WebApplicationFactoryBuilder(ApiFactory factory, Type controller)
    {
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Build() =>
            factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
                services.AddControllers().ConfigureApplicationPartManager(pm => pm.FeatureProviders.Add(new SingleControllerProvider(controller)))));
    }
}

public sealed class SampleLine
{
    public string Item { get; set; } = "Container";

    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)]
    public decimal Cost { get; set; } = 450_000m;
}

public sealed class SampleFieldsDto
{
    public string Name { get; set; } = "Hino 500";

    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)]
    public decimal? PurchasePrice { get; set; } = 5_000_000m;

    [FieldPermission(PermissionCodes.VEH_FIELD_PROFIT_VIEW)]
    public decimal? Profit { get; set; } = 120_000m;

    public List<SampleLine> Lines { get; set; } = [new()];
}

/// <summary>A protected endpoint that returns restricted fields. Used only by tests.</summary>
[ApiController]
[Route("test/fields")]
[AuthenticatedOnly]
public sealed class SampleFieldsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(ApiResponse<SampleFieldsDto>.Ok(new SampleFieldsDto()));
}

public sealed class TimeOut
{
    public DateTime Stamped { get; set; } = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Unspecified);   // how EF used to hand back a UTC column
    public DateTime FromLocal { get; set; } = new(2026, 10, 10, 14, 30, 0, DateTimeKind.Local);
    public DateTime WithFraction { get; set; } = new(2026, 10, 10, 9, 30, 0, 123, DateTimeKind.Utc);
    public DateTime? Missing { get; set; }
    public DateOnly Day { get; set; } = new(2026, 10, 10);
    public DateTimeOffset WithOffset { get; set; } = new(2026, 10, 10, 14, 30, 0, TimeSpan.FromHours(5));
}

public sealed class TimeIn
{
    public DateTime At { get; set; }
    public DateTime? MaybeAt { get; set; }
    public DateOnly Day { get; set; }
}

/// <summary>Echoes dates in and out, to test the API's date and time contract. Used only by tests.</summary>
[ApiController]
[Route("test/time")]
[AuthenticatedOnly]
public sealed class TimeProbeController(VMS.Shared.Time.IClientTimeZone clientZone) : ControllerBase
{
    [HttpGet("out")]
    public IActionResult Out() => Ok(ApiResponse<TimeOut>.Ok(new TimeOut()));

    [HttpPost("in")]
    public IActionResult In([FromBody] TimeIn body) =>
        Ok(new { at = body.At, kind = body.At.Kind.ToString(), maybeAt = body.MaybeAt, day = body.Day });

    [HttpGet("zone")]
    public IActionResult Zone() => Ok(new { offsetHours = clientZone.Current?.BaseUtcOffset.TotalHours });
}

public sealed class NestedBody
{
    public string? City { get; set; }
}

public sealed class ValidatedBody
{
    [System.ComponentModel.DataAnnotations.Required] public string? LegalName { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(5)] public string? Code { get; set; }
    [System.ComponentModel.DataAnnotations.EmailAddress] public string? Email { get; set; }
    public DateTime? At { get; set; }
    public DateOnly? Day { get; set; }
    public NestedBody? Address { get; set; }
}

/// <summary>Raises validation errors the ways a service will, and takes a body the framework validates. Used only by tests.</summary>
[ApiController]
[Route("test/validation")]
[AuthenticatedOnly]
public sealed class ValidationProbeController(VMS.Shared.Messages.IMessageCatalogue messages) : ControllerBase
{
    [HttpGet("one")]
    public IActionResult One() => throw new VMS.Shared.Exceptions.ValidationException(
        messages.Error("cnic", VMS.Shared.Messages.Msg.BpCnicUsed, ("BPCode", "BP-26-00147"), ("LegalName", "Ali Traders")));

    [HttpGet("many")]
    public IActionResult Many() => throw new VMS.Shared.Exceptions.ValidationException(
    [
        messages.Error("cnic", VMS.Shared.Messages.Msg.BpCnicFormat),
        messages.Error("mobile", VMS.Shared.Messages.Msg.BpMobileFormat),
        messages.Error(null, VMS.Shared.Messages.Msg.BpRoleRequired)
    ]);

    [HttpPost("body")]
    public IActionResult Body([FromBody] ValidatedBody body) => Ok(new { body.LegalName });

    [HttpGet("plain")]
    public IActionResult Plain() => throw new VMS.Shared.Exceptions.BadRequestException("Just a message.");
}

/// <summary>What another module sees through <c>ILookupReader</c>. Used only by tests.</summary>
[ApiController]
[Route("test/lookups")]
[AuthenticatedOnly]
public sealed class LookupProbeController(VMS.Shared.Lookups.ILookupReader lookups) : ControllerBase
{
    [HttpGet("{lookupType}/{id:int}")]
    public async Task<IActionResult> Get(string lookupType, int id) =>
        Ok(new
        {
            found = await lookups.FindAsync(lookupType, id) is not null,
            active = await lookups.IsActiveAsync(lookupType, id)
        });
}

/// <summary>An endpoint that forgot to say how it is protected. Must stop the application starting.</summary>
[ApiController]
[Route("test/unprotected")]
public sealed class UnprotectedProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
