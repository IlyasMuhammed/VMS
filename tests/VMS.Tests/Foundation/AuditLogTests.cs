using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-08: every change is written to the audit trail, in the same transaction, and the trail cannot be edited.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(ApiFactory factory)
{
    private const string Password = "Passw0rd!Test";

    private IReadOnlyList<Dictionary<string, object?>> Audit(string entity, object recordId) =>
        factory.Query(
            "SELECT * FROM core.AuditEntries WHERE Entity = @e AND RecordId = @r ORDER BY AuditEntryID",
            ("@e", entity), ("@r", recordId.ToString()!));

    private int AdminUserId() =>
        factory.Scalar<int>("SELECT UserID FROM auth.UserAccounts WHERE Email = @e", ("@e", ApiFactory.AdminEmail));

    private static string Email(string prefix) => $"{prefix}.{Guid.NewGuid():N}@test.local";

    /// <summary>Makes the audit table refuse rows for one entity, to prove the change is undone with it.</summary>
    private IDisposable AuditRefusesRowsFor(string entity)
    {
        var name = "CK_test_refuse_" + Guid.NewGuid().ToString("N")[..8];
        factory.Execute($"ALTER TABLE core.AuditEntries WITH NOCHECK ADD CONSTRAINT {name} CHECK (Entity <> '{entity}')");
        return new Undo(() => factory.Execute($"ALTER TABLE core.AuditEntries DROP CONSTRAINT {name}"));
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>The entries for one record that record its creation (not the activation that follows accepting the invite).</summary>
    private IReadOnlyList<Dictionary<string, object?>> Created(string entity, object recordId) =>
        Audit(entity, recordId).Where(e => (string)e["Action"]! == "Created").ToList();

    // -- What is written ----------------------------------------------------------------

    [Fact]
    public async Task Creating_a_record_writes_one_entry_with_a_snapshot_and_who_did_it()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = Email("created");

        var userId = await admin.CreateActiveUserAsync(email, Password);

        var entry = Assert.Single(Created("UserAccount", userId));
        Assert.Equal("Created", entry["Action"]);
        Assert.Null(entry["Field"]);
        Assert.Null(entry["OldValue"]);
        Assert.Contains(email, (string)entry["NewValue"]!);
        Assert.Equal(AdminUserId(), entry["UserId"]);
        Assert.Equal(TenantDefaults.PlatformTenantId, entry["TenantId"]);
        Assert.NotEqual(Guid.Empty, entry["GroupId"]);
        Assert.InRange((DateTime)entry["OccurredAt"]!, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Work_done_without_signing_in_is_attributed_to_anonymous()
    {
        // Accepting an invite is anonymous by design: whoever holds the link activates the account.
        using var admin = await factory.AsSuperAdminAsync();
        var roleId = await admin.RoleIdAsync("STAFF");
        var created = await (await admin.PostAsJsonAsync("/api/users", new { firstName = "Ina", lastName = "Vitee", email = Email("invitee"), roleId })).DataAsync();
        var userId = created.GetProperty("userId").GetInt32();
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(created.GetProperty("inviteLink").GetString()!).Query)["token"];

        using var nobody = factory.CreateClient();   // no token: not signed in
        (await nobody.PostAsJsonAsync("/api/auth/accept-invite", new { token, newPassword = Password })).EnsureSuccessStatusCode();

        var activation = Assert.Single(Audit("UserAccount", userId), e => (string)e["Action"]! == "Updated");
        Assert.Equal("IsActive", activation["Field"]);
        Assert.Equal("false", activation["OldValue"]);
        Assert.Equal("true", activation["NewValue"]);
        Assert.Null(activation["UserId"]);
        Assert.Equal("Anonymous", activation["UserName"]);
    }

    [Fact]
    public async Task Secrets_never_reach_the_audit_trail()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var userId = await admin.CreateActiveUserAsync(Email("secret"), Password);

        var snapshot = (string)Assert.Single(Created("UserAccount", userId))["NewValue"]!;
        Assert.DoesNotContain("PasswordHash", snapshot);
        Assert.DoesNotContain("InviteToken", snapshot);
        Assert.DoesNotContain("PasswordReset", snapshot);
        Assert.Equal(0, factory.Scalar<int>(
            "SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'UserAccount' AND RecordId = @r AND Field IN ('PasswordHash','InviteTokenHash')",
            ("@r", userId.ToString())));
    }

    [Fact]
    public async Task Everything_saved_in_one_request_shares_a_group()
    {
        using var admin = await factory.AsSuperAdminAsync();

        var userId = await admin.CreateActiveUserAsync(Email("group"), Password);

        var groupId = Assert.Single(Created("UserAccount", userId))["GroupId"];
        var roleRows = factory.Query(
            "SELECT GroupId FROM core.AuditEntries WHERE Entity = 'UserRole' AND Action = 'Created' AND NewValue LIKE @like",
            ("@like", $"%\"UserID\":\"{userId}\"%"));
        Assert.Equal(groupId, Assert.Single(roleRows)["GroupId"]);
    }

    [Fact]
    public async Task Changing_fields_writes_one_entry_per_changed_field_with_old_and_new()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("patch"), Password);
        var afterCreation = Audit("UserAccount", userId).Count;

        var response = await admin.PatchJsonAsync($"/api/users/{userId}", new { phone = "0300-1234567", department = "Fleet" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Only what happened after the account was created and activated.
        List<Dictionary<string, object?>> ByAdmin() => Audit("UserAccount", userId).Skip(afterCreation).ToList();
        var updates = ByAdmin();
        Assert.Equal(["Department", "Phone"], updates.Select(e => (string)e["Field"]!).Order());
        var phone = updates.Single(e => (string)e["Field"]! == "Phone");
        Assert.Null(phone["OldValue"]);
        Assert.Equal("0300-1234567", phone["NewValue"]);

        // Change one field again: only that field is written, and it carries the previous value.
        await admin.PatchJsonAsync($"/api/users/{userId}", new { phone = "0311-7654321" });
        var second = ByAdmin().Last(e => (string)e["Field"]! == "Phone");
        Assert.Equal("0300-1234567", second["OldValue"]);
        Assert.Equal("0311-7654321", second["NewValue"]);
        Assert.Equal(3, ByAdmin().Count);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_writes_nothing()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("same"), Password);
        await admin.PatchJsonAsync($"/api/users/{userId}", new { department = "Fleet" });
        var before = Audit("UserAccount", userId).Count;

        await admin.PatchJsonAsync($"/api/users/{userId}", new { department = "Fleet" });

        Assert.Equal(before, Audit("UserAccount", userId).Count);
    }

    [Fact]
    public async Task Signing_in_is_bookkeeping_not_history()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = Email("signin");
        var userId = await admin.CreateActiveUserAsync(email, Password);
        int Count() => factory.Scalar<int>(
            "SELECT COUNT(*) FROM core.AuditEntries WHERE Entity IN ('UserAccount','UserSession') AND RecordId = @r", ("@r", userId.ToString()));
        var before = Count();

        using var client = factory.CreateClient();
        (await client.TryLoginAsync(email, Password)).EnsureSuccessStatusCode();
        await client.TryLoginAsync(email, "wrong-password");

        Assert.Equal(before, Count());
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'UserSession'"));
    }

    [Fact]
    public async Task Granting_and_revoking_a_permission_is_audited()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var created = await (await admin.PostAsJsonAsync("/api/roles", new { name = "Auditor " + Guid.NewGuid().ToString("N")[..6], roleCode = "AUD_" + Guid.NewGuid().ToString("N")[..8], description = "test" })).DataAsync();
        var roleId = created.GetProperty("roleId").GetInt32();
        var permissionIds = factory.Query("SELECT PermissionID FROM auth.Permissions WHERE Code IN ('USER_VIEW','ROLE_VIEW') ORDER BY Code")
            .Select(r => (int)r["PermissionID"]!).ToList();
        var role = Audit("Role", roleId);
        Assert.Equal("Created", Assert.Single(role)["Action"]);

        // Grant two, then revoke one.
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new { allowedPermissionIds = permissionIds })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions", new { allowedPermissionIds = permissionIds.Take(1) })).EnsureSuccessStatusCode();

        var grants = factory.Query(
            "SELECT Action, NewValue, OldValue FROM core.AuditEntries WHERE Entity = 'RolePermission' AND (NewValue LIKE @like OR OldValue LIKE @like) ORDER BY AuditEntryID",
            ("@like", $"%\"RoleID\":\"{roleId}\"%"));
        Assert.Equal(2, grants.Count(g => (string)g["Action"]! == "Created"));
        var revoked = Assert.Single(grants, g => (string)g["Action"]! == "Deleted");
        Assert.Contains($"\"PermissionID\":\"{permissionIds[1]}\"", (string)revoked["OldValue"]!);
        Assert.Null(revoked["NewValue"]);
    }

    [Fact]
    public async Task Creating_a_tenant_is_audited_under_the_super_admin()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var code = "AUD" + Guid.NewGuid().ToString("N")[..8];

        var response = await admin.PostAsJsonAsync("/api/system/tenants", new
        {
            tenantCode = code, tenantName = "Audit Transport", adminFirstName = "Ada", adminEmail = Email("tenantadmin")
        });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.DataAsync()).GetProperty("tenantId").GetGuid();

        var entry = Assert.Single(Audit("Tenant", tenantId));
        Assert.Equal("Created", entry["Action"]);
        Assert.Contains(code, (string)entry["NewValue"]!, StringComparison.OrdinalIgnoreCase);   // the service normalises the code's case
        Assert.Equal(AdminUserId(), entry["UserId"]);
    }

    // -- Append-only --------------------------------------------------------------------

    [Fact]
    public async Task Audit_rows_cannot_be_changed_or_deleted_even_directly_in_the_database()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("frozen"), Password);
        var count = factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries");

        var update = Assert.Throws<SqlException>(() => factory.Execute("UPDATE core.AuditEntries SET NewValue = 'tampered'"));
        var delete = Assert.Throws<SqlException>(() => factory.Execute("DELETE FROM core.AuditEntries WHERE Entity = 'UserAccount'"));

        Assert.Contains("append-only", update.Message);
        Assert.Contains("append-only", delete.Message);
        Assert.Equal(count, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries"));
        Assert.DoesNotContain("tampered", (string)Assert.Single(Created("UserAccount", userId))["NewValue"]!);
    }

    // -- Same transaction --------------------------------------------------------------

    [Fact]
    public async Task If_the_audit_row_cannot_be_written_a_change_to_an_existing_record_is_undone()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("atomic1"), Password);

        HttpResponseMessage response;
        using (AuditRefusesRowsFor("UserAccount"))
            response = await admin.PatchJsonAsync($"/api/users/{userId}", new { phone = "0300-0000000" });

        Assert.True((int)response.StatusCode >= 500, $"Expected a server error but got {(int)response.StatusCode}.");
        Assert.Null(factory.Scalar<string?>("SELECT Phone FROM auth.UserAccounts WHERE UserID = @u", ("@u", userId)));
    }

    [Fact]
    public async Task If_the_audit_row_cannot_be_written_a_new_record_is_not_created_either()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var email = Email("atomic2");
        var roleId = await admin.RoleIdAsync("STAFF");

        HttpResponseMessage response;
        using (AuditRefusesRowsFor("UserAccount"))
            response = await admin.PostAsJsonAsync("/api/users", new { firstName = "No", lastName = "Trace", email, roleId });

        Assert.True((int)response.StatusCode >= 500, $"Expected a server error but got {(int)response.StatusCode}.");
        // The user, and the role row that went in the same request, are both gone.
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM auth.UserAccounts WHERE Email = @e", ("@e", email)));
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'UserAccount' AND NewValue LIKE @like", ("@like", $"%{email}%")));
    }

    // -- Explicit notes and the acting user --------------------------------------------

    private WebApplicationFactoryWithNote NoteOnEveryRequest() => new(factory);

    private sealed class WebApplicationFactoryWithNote : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;

        public WebApplicationFactoryWithNote(ApiFactory factory) =>
            _host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddTransient<IStartupFilter, NoteFilter>()));

        public HttpClient Client(string token) => _host.CreateClient().WithToken(token);
        public void Dispose() => _host.Dispose();
    }

    /// <summary>Queues an explicit audit note at the start of every request, the way a handler would.</summary>
    private sealed class NoteFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, pipeline) =>
            {
                context.RequestServices.GetRequiredService<IAuditContext>().Note(new AuditNote(
                    "Vehicle", "V-100", "DuplicateOverridden", NewValue: "candidate V-042", Reason: "Second truck, same series"));
                await pipeline();
            });
            next(app);
        };
    }

    private static string TokenOf(HttpClient signedIn) => signedIn.DefaultRequestHeaders.Authorization!.Parameter!;

    [Fact]
    public async Task A_note_is_written_with_the_change_it_accompanies_and_shares_its_group()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("note1"), Password);
        var noteBefore = factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'Vehicle' AND RecordId = 'V-100'");
        using var noted = NoteOnEveryRequest();
        using var client = noted.Client(TokenOf(admin));

        (await client.PatchJsonAsync($"/api/users/{userId}", new { department = "Yard" })).EnsureSuccessStatusCode();

        var note = Assert.Single(Audit("Vehicle", "V-100").Skip(noteBefore));
        Assert.Equal("DuplicateOverridden", note["Action"]);
        Assert.Equal("Second truck, same series", note["Reason"]);
        Assert.Equal("candidate V-042", note["NewValue"]);
        Assert.Equal(AdminUserId(), note["UserId"]);
        var change = Audit("UserAccount", userId).Last(e => (string)e["Field"]! == "Department");
        Assert.Equal(change["GroupId"], note["GroupId"]);
    }

    [Fact]
    public async Task A_note_exists_only_if_its_change_does()
    {
        using var admin = await factory.AsSuperAdminAsync();
        var userId = await admin.CreateActiveUserAsync(Email("note2"), Password);
        var before = factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'Vehicle' AND RecordId = 'V-100'");
        using var noted = NoteOnEveryRequest();
        using var client = noted.Client(TokenOf(admin));

        HttpResponseMessage response;
        using (AuditRefusesRowsFor("UserAccount"))
            response = await client.PatchJsonAsync($"/api/users/{userId}", new { department = "Workshop" });

        Assert.True((int)response.StatusCode >= 500);
        Assert.Equal(before, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE Entity = 'Vehicle' AND RecordId = 'V-100'"));
    }

    [Fact]
    public void Work_with_nobody_signed_in_is_not_audited_unless_it_names_a_system_actor()
    {
        using var client = factory.CreateClient();   // starts the host
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditContext>();

        Assert.Null(audit.Actor);   // no request, no user: start-up seeding and tooling land here

        using (audit.ActAsSystem("Recurring charges"))
        {
            Assert.Equal("Recurring charges", audit.Actor!.UserName);
            Assert.Null(audit.Actor.UserId);
        }

        Assert.Null(audit.Actor);
        Assert.Equal(0, factory.Scalar<int>("SELECT COUNT(*) FROM core.AuditEntries WHERE UserId IS NULL AND UserName IS NULL"));
    }
}
