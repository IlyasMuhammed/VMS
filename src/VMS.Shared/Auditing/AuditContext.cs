using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Authorization;

namespace VMS.Shared.Auditing;

/// <summary>Who is acting. <see cref="UserId"/> is null for the system and for a caller who is not signed in.</summary>
public sealed record AuditActor(int? UserId, string? UserName, string? IpAddress);

/// <summary>An event with no row change of its own, such as a duplicate override or a status change reason.</summary>
public sealed record AuditNote(
    string Entity,
    string RecordId,
    string Action,
    string? Field = null,
    string? OldValue = null,
    string? NewValue = null,
    string? Reason = null,
    string? RequiredPermission = null,
    string? RootEntity = null,
    string? RootRecordId = null);

/// <summary>The record an audited entity is part of. See <see cref="IAuditRooted"/>.</summary>
public sealed record AuditRoot(string Entity, string RecordId);

/// <summary>
/// Implemented by an audited entity that belongs to a larger record, so its audit rows carry that record's identity
/// (<see cref="AuditEntry.RootEntity"/>). The root record itself returns itself. A method, so EF never tries to map it.
/// </summary>
public interface IAuditRooted
{
    AuditRoot GetAuditRoot();
}

/// <summary>
/// The audit trail's view of the current unit of work: who is acting and which explicit notes are
/// waiting to be written. Row changes need nothing from callers — the DbContexts capture them.
/// </summary>
public interface IAuditContext
{
    /// <summary>Null means nobody is attributable (start-up seeding, tooling): nothing is audited.</summary>
    AuditActor? Actor { get; }

    /// <summary>Shared by every entry written in this scope, that is by one request.</summary>
    Guid GroupId { get; }

    /// <summary>
    /// Queues an event to be written in the same transaction as the next save of any audited DbContext,
    /// so it exists if and only if the change it accompanies does.
    /// </summary>
    void Note(AuditNote note);

    IReadOnlyList<AuditNote> PendingNotes { get; }

    /// <summary>Drops the first <paramref name="count"/> queued notes once they have been saved.</summary>
    void ClearNotes(int count);

    /// <summary>
    /// Attributes work done with no signed-in user (a scheduled job) to a named system actor, so it is
    /// audited instead of skipped. Dispose to stop.
    /// </summary>
    IDisposable ActAsSystem(string name);
}

internal sealed class AuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    private readonly List<AuditNote> _notes = [];
    private string? _system;

    public Guid GroupId { get; } = Guid.NewGuid();

    public AuditActor? Actor
    {
        get
        {
            if (_system is not null) return new AuditActor(null, _system, null);

            var http = accessor.HttpContext;
            if (http is null) return null;

            var ip = http.Connection.RemoteIpAddress?.ToString();
            var user = http.User;
            return user.Identity?.IsAuthenticated == true
                ? new AuditActor(user.GetUserId(), user.FindFirst("user_name")?.Value, ip)
                : new AuditActor(null, "Anonymous", ip);
        }
    }

    public void Note(AuditNote note) => _notes.Add(note);

    public IReadOnlyList<AuditNote> PendingNotes => _notes;

    public void ClearNotes(int count) => _notes.RemoveRange(0, Math.Min(count, _notes.Count));

    public IDisposable ActAsSystem(string name)
    {
        var previous = _system;
        _system = name;
        return new Restore(() => _system = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>For design-time tooling and anything else that must never write audit rows.</summary>
public sealed class NoAuditContext : IAuditContext
{
    public static readonly NoAuditContext Instance = new();

    public AuditActor? Actor => null;
    public Guid GroupId { get; } = Guid.Empty;
    public IReadOnlyList<AuditNote> PendingNotes => [];
    public void Note(AuditNote note) { }
    public void ClearNotes(int count) { }
    public IDisposable ActAsSystem(string name) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}

public static class AuditServiceExtensions
{
    /// <summary>Registered once, centrally, before any module — every audited DbContext injects <see cref="IAuditContext"/>.</summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContext, AuditContext>();
        return services;
    }
}
