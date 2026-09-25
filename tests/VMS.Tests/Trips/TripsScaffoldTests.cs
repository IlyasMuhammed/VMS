using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VMS.Shared.Authorization;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-01: the Trip/Billing/Invoicing/Customer Ledger module's cross-cutting scaffolding — the
/// Idempotency-Key convention, the additive error-shape fields, and the permission catalogue's new codes.
/// The audit-row guarantee (AC-50) is not re-proven here: it is the same, already-proven `SaveAuditedAsync`
/// mechanism every other module's DbContext reuses unchanged, and is re-verified per module by CC-46.</summary>
[Collection(ApiCollection.Name)]
public sealed class TripsScaffoldTests(ApiFactory factory)
{
    [Fact]
    public async Task A_replay_with_the_same_key_and_body_returns_the_first_response_unrun()
    {
        using var host = factory.With<IdempotentProbeController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("Rider", 7301, "ANY"));

        var key = Guid.NewGuid().ToString();
        Task<HttpResponseMessage> Send() => client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/test/idempotent")
        {
            Content = JsonContent.Create(new IdempotentEchoBody { Note = "same" }),
            Headers = { { "Idempotency-Key", key } }
        });

        var first = await Send();
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadAsStringAsync();

        var second = await Send();
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadAsStringAsync();

        // Same "ran" guid both times: the second call never executed the action again.
        Assert.Equal(firstBody, secondBody);
    }

    [Fact]
    public async Task A_different_key_runs_its_own_fresh_action()
    {
        using var host = factory.With<IdempotentProbeController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("Rider", 7302, "ANY"));

        async Task<string> SendWithKey(string key)
        {
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/test/idempotent")
            {
                Content = JsonContent.Create(new IdempotentEchoBody { Note = "same" }),
                Headers = { { "Idempotency-Key", key } }
            });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        var first = await SendWithKey(Guid.NewGuid().ToString());
        var second = await SendWithKey(Guid.NewGuid().ToString());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_refused_not_replayed()
    {
        using var host = factory.With<IdempotentProbeController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("Rider", 7303, "ANY"));

        var key = Guid.NewGuid().ToString();
        var first = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/test/idempotent")
        {
            Content = JsonContent.Create(new IdempotentEchoBody { Note = "one" }),
            Headers = { { "Idempotency-Key", key } }
        });
        first.EnsureSuccessStatusCode();

        var second = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/test/idempotent")
        {
            Content = JsonContent.Create(new IdempotentEchoBody { Note = "two" }),
            Headers = { { "Idempotency-Key", key } }
        });
        Assert.Equal((HttpStatusCode)422, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_missing_idempotency_key_header_is_refused()
    {
        using var host = factory.With<IdempotentProbeController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("Rider", 7304, "ANY"));

        var response = await client.PostAsJsonAsync("/test/idempotent", new IdempotentEchoBody { Note = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VAL-GEN-021", doc.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_403_carries_the_same_response_shape_as_every_other_refusal()
    {
        using var host = factory.With<IdempotentProbeController>().Build();
        using var client = host.CreateClient().WithToken(TestTokens.For("NoPerms", 7305)); // no permissions at all

        var response = await client.GetAsync("/test/idempotent/restricted");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("FORBIDDEN", doc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public void The_permission_catalogue_holds_every_trp_code_the_register_names()
    {
        var expected = new[]
        {
            PermissionCodes.TRP_CURRENCY_MANAGE, PermissionCodes.TRP_EXCHANGERATE_MANAGE,
            PermissionCodes.TRP_CUSTOMER_VIEW, PermissionCodes.TRP_CUSTOMER_EDIT, PermissionCodes.TRP_TAXRULE_EDIT,
            PermissionCodes.TRP_TEMPLATE_EDIT, PermissionCodes.TRP_CITY_EDIT, PermissionCodes.TRP_ROUTE_EDIT,
            PermissionCodes.TRP_TRIPCONFIG_VIEW, PermissionCodes.TRP_TRIPCONFIG_EDIT, PermissionCodes.TRP_RATE_VIEW, PermissionCodes.TRP_RATE_CONFIGURE, PermissionCodes.TRP_RATE_REPRICE,
            PermissionCodes.TRP_TRIP_VIEW, PermissionCodes.TRP_TRIP_CREATE, PermissionCodes.TRP_TRIP_EDIT,
            PermissionCodes.TRP_TRIP_STATUS, PermissionCodes.TRP_TRIP_INACTIVATE, PermissionCodes.TRP_TRIP_SKIPSTATUS, PermissionCodes.TRP_TRIP_REOPEN, PermissionCodes.TRP_TRIP_DOCUMENTS,
            PermissionCodes.TRP_TRIP_REVIEW, PermissionCodes.TRP_TRIP_OVERRIDE_DRIVER, PermissionCodes.TRP_POD_APPROVE, PermissionCodes.TRP_EXPENSE_EDIT,
            PermissionCodes.TRP_EXPENSE_APPROVE, PermissionCodes.TRP_FUEL_EDIT, PermissionCodes.TRP_INCOME_EDIT,
            PermissionCodes.TRP_PNL_VIEW, PermissionCodes.TRP_FUELCARD_EDIT, PermissionCodes.TRP_INVOICE_GENERATE,
            PermissionCodes.TRP_INVOICE_VIEW, PermissionCodes.TRP_INVOICE_SUBMIT, PermissionCodes.TRP_INVOICE_CANCEL,
            PermissionCodes.TRP_INVOICE_REGENERATE, PermissionCodes.TRP_INVOICE_RERENDER,
            PermissionCodes.TRP_INVOICE_EVIDENCE_RETRY, PermissionCodes.TRP_INVOICE_EVIDENCE_RERENDER,
            PermissionCodes.TRP_BANKACCOUNT_MANAGE,
            PermissionCodes.TRP_PAYMENT_CREATE, PermissionCodes.TRP_PAYMENT_REVERSE,
            PermissionCodes.TRP_PAYMENT_CARRYFORWARD, PermissionCodes.TRP_PAYMENT_REFUND, PermissionCodes.TRP_PAYMENT_WRITEOFF,
            PermissionCodes.TRP_PAYMENT_DISCOUNT, PermissionCodes.TRP_PAYMENT_ADVANCE, PermissionCodes.TRP_LEDGER_VIEW,
            PermissionCodes.TRP_LEDGER_OPENINGBALANCE, PermissionCodes.TRP_LEDGER_PERIODLOCK, PermissionCodes.TRP_REPORT_VIEW
        };
        Assert.Equal(50, expected.Length);
        foreach (var code in expected)
            Assert.Contains(PermissionCodes.Catalog, d => d.Code == code);
    }
}
