using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VMS.Shared.Authorization;
using VMS.Tests.BusinessPartners;
using VMS.Tests.Infrastructure;
using VMS.Tests.Vehicles;
using Xunit;

namespace VMS.Tests.Trips;

/// <summary>CC-16: Trip events, documents, POD, issues (§23, §25, AC-54).</summary>
[Collection(ApiCollection.Name)]
public sealed class TripEventsDocumentsPodIssuesTests(ApiFactory factory)
{
    private static async Task<(VehicleWorld Vehicles, HttpClient Admin)> WorldAsync(ApiFactory factory)
    {
        var vehicles = await VehicleWorld.CreateAsync(factory);
        var admin = vehicles.As("Combined Admin", 1,
            [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything]);
        return (vehicles, admin);
    }

    private static async Task<(JsonElement Trip, int CustomerId)> DraftTripAsync(VehicleWorld vehicles, HttpClient admin, bool podRequired = false)
    {
        var cities = await (await admin.GetAsync("/api/cities")).DataAsync();
        var byAbbr = cities.EnumerateArray().ToDictionary(c => c.GetProperty("abbreviation").GetString()!, c => c.GetProperty("cityId").GetInt32());

        var customer = await (await admin.PostAsJsonAsync("/api/customers", new { customerName = $"Acme {Guid.NewGuid():N}", addressLine1 = "Line 1" })).DataAsync();
        var customerId = customer.GetProperty("customerId").GetInt32();
        (await admin.PostAsJsonAsync($"/api/customers/{customerId}/activate", new { rowVersion = customer.GetProperty("rowVersion").GetString(), reason = "Test override" })).EnsureSuccessStatusCode();
        if (podRequired)
            (await admin.PutAsJsonAsync($"/api/customers/{customerId}/billing-configuration", new { podRequired = true })).EnsureSuccessStatusCode();

        var route = await (await admin.PostAsJsonAsync("/api/routes", new
        {
            routeName = "Lahore - Faisalabad",
            stops = new[] { new { cityId = byAbbr["LHR"], stopType = "Origin" }, new { cityId = byAbbr["FSD"], stopType = "Destination" } }
        })).DataAsync();
        var config = await (await admin.PostAsJsonAsync("/api/trip-configurations",
            new { customerId, name = "Daily", routeId = route.GetProperty("routeId").GetInt32(), directionType = "OneWay" })).DataAsync();
        var configId = config.GetProperty("tripConfigurationId").GetInt64();

        var truck = await vehicles.ActiveAsync();
        var vehicleId = VehicleWorld.Id(truck);
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/vehicles", new { vehicleId, effectiveFrom = "2020-01-01" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/activate", new { rowVersion = config.GetProperty("rowVersion").GetString() })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/trip-configurations/{configId}/rates", new { effectiveFrom = "2026-07-01", rateAmount = 25000 })).EnsureSuccessStatusCode();

        var driverId = await vehicles.DriverAsync();
        var created = await admin.PostAsJsonAsync("/api/trips", new { customerId, tripConfigurationId = configId, vehicleId, driverId, tripDate = "2026-07-10" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return (await created.DataAsync(), customerId);
    }

    private static async Task<JsonElement> TransitionAsync(HttpClient client, long tripId, string toStatus, string rowVersion, object? body = null)
    {
        var dict = body is null ? new Dictionary<string, object?>() : JsonSerializer.SerializeToElement(body).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        dict["rowVersion"] = rowVersion;
        var response = await client.PostAsJsonAsync($"/api/trips/{tripId}/status/{toStatus}", dict);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.DataAsync();
    }

    private static byte[] Pdf()
    {
        var bytes = new byte[400];
        Random.Shared.NextBytes(bytes);
        "%PDF-1.4\n"u8.CopyTo(bytes);
        return bytes;
    }

    private static System.Net.Http.MultipartFormDataContent PdfForm(string? fieldName = null, string? fieldValue = null)
    {
        var form = new System.Net.Http.MultipartFormDataContent();
        if (fieldName is not null) form.Add(new StringContent(fieldValue ?? string.Empty), fieldName);
        var part = new System.Net.Http.ByteArrayContent(Pdf());
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", "evidence.pdf");
        return form;
    }

    [Fact]
    public async Task Creating_a_trip_records_a_created_event()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var events = await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync();
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "Created");
    }

    [Fact]
    public async Task Each_normal_transition_records_its_own_matching_event()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var rv = trip.GetProperty("rowVersion").GetString()!;

        var planned = await TransitionAsync(admin, tripId, "Planned", rv);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        await TransitionAsync(admin, tripId, "Started", assigned.GetProperty("rowVersion").GetString()!, new { startOdometer = 100 });

        var events = (await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["Created", "Planned", "Assigned", "Started"], events);
    }

    [Fact]
    public async Task Skipping_records_a_statusskipped_event_alongside_the_destination_event()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);

        var withSkip = vehicles.As("Can Skip", 7, [.. VehicleWorld.VehiclePermissions, .. PartnerWorld.Everything, .. TripsWorld.Everything, PermissionCodes.TRP_TRIP_SKIPSTATUS]);
        // Planned -> Assigned is normal; Assigned -> Delivered is not (skips Started/InTransit/AtPickup/Loaded/AtDelivery).
        var assigned = await TransitionAsync(withSkip, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        await TransitionAsync(withSkip, tripId, "Delivered", assigned.GetProperty("rowVersion").GetString()!, new { endOdometer = 900 });

        var events = (await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["Created", "Planned", "Assigned", "StatusSkipped", "Delivered"], events);
    }

    [Fact]
    public async Task Hold_resume_and_cancel_each_record_their_own_event()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);

        var held = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/hold", new { reason = "Waiting", rowVersion = planned.GetProperty("rowVersion").GetString() })).DataAsync();
        var resumed = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/resume", new { rowVersion = held.GetProperty("rowVersion").GetString() })).DataAsync();
        await admin.PostAsJsonAsync($"/api/trips/{tripId}/cancel", new { reason = "Customer cancelled", rowVersion = resumed.GetProperty("rowVersion").GetString() });

        var events = (await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["Created", "Planned", "OnHold", "Resumed", "Cancelled"], events);
    }

    [Fact]
    public async Task AC_54_a_retried_client_event_id_is_not_duplicated()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var clientEventId = Guid.NewGuid();

        var first = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/events", new { eventType = "Note", remarks = "Offline note", clientEventId })).DataAsync();
        var second = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/events", new { eventType = "Note", remarks = "Offline note", clientEventId })).DataAsync();
        Assert.Equal(first.GetProperty("tripEventId").GetInt64(), second.GetProperty("tripEventId").GetInt64());

        var events = await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync();
        Assert.Equal(1, events.EnumerateArray().Count(e => e.GetProperty("eventType").GetString() == "Note"));
    }

    [Fact]
    public async Task A_manual_event_is_restricted_to_note_type()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/events", new { eventType = "Fuel", remarks = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_far_future_event_time_is_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var response = await admin.PostAsJsonAsync($"/api/trips/{tripId}/events", new { eventType = "Note", remarks = "x", eventDateTime = DateTime.UtcNow.AddHours(1) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_document_can_be_uploaded_listed_and_downloaded()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var uploaded = await admin.PostAsync($"/api/trips/{tripId}/documents", PdfForm("documentType", "GatePass"));
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        var doc = await uploaded.DataAsync();
        Assert.Equal("GatePass", doc.GetProperty("documentType").GetString());

        var list = await (await admin.GetAsync($"/api/trips/{tripId}/documents")).DataAsync();
        Assert.Equal(1, list.GetArrayLength());

        var link = await (await admin.PostAsync($"/api/trip-documents/{doc.GetProperty("tripDocumentId").GetInt64()}/download-link", null)).DataAsync();
        Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task Uploading_a_pod_records_a_poduploaded_event_and_can_be_approved_or_rejected()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var uploaded = await admin.PostAsync($"/api/trips/{tripId}/pod", PdfForm());
        Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        var pod = await uploaded.DataAsync();
        Assert.Equal("Uploaded", pod.GetProperty("status").GetString());

        var events = (await (await admin.GetAsync($"/api/trips/{tripId}/events")).DataAsync()).EnumerateArray().Select(e => e.GetProperty("eventType").GetString());
        Assert.Contains("PODUploaded", events);

        var approved = await (await admin.PostAsync($"/api/trip-pods/{pod.GetProperty("tripPODId").GetInt64()}/approve", null)).DataAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        var alreadyDecided = await admin.PostAsync($"/api/trip-pods/{pod.GetProperty("tripPODId").GetInt64()}/reject", JsonContent.Create(new { reason = "too late" }));
        Assert.Equal(HttpStatusCode.Conflict, alreadyDecided.StatusCode);
    }

    [Fact]
    public async Task Completing_a_trip_for_a_pod_required_customer_needs_an_approved_pod()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin, podRequired: true);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        var started = await TransitionAsync(admin, tripId, "Started", assigned.GetProperty("rowVersion").GetString()!, new { startOdometer = 100 });
        var inTransit = await TransitionAsync(admin, tripId, "InTransit", started.GetProperty("rowVersion").GetString()!);
        var atDelivery = await TransitionAsync(admin, tripId, "AtDelivery", inTransit.GetProperty("rowVersion").GetString()!);
        var delivered = await TransitionAsync(admin, tripId, "Delivered", atDelivery.GetProperty("rowVersion").GetString()!, new { endOdometer = 900 });

        // No POD at all yet.
        var refused = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);

        var pod = await (await admin.PostAsync($"/api/trips/{tripId}/pod", PdfForm())).DataAsync();
        // Uploaded, not yet Approved — still refused.
        var stillRefused = await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, stillRefused.StatusCode);

        await admin.PostAsync($"/api/trip-pods/{pod.GetProperty("tripPODId").GetInt64()}/approve", null);
        var completed = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_trips_own_driver_needs_at_least_an_uploaded_pod_to_complete_it_themselves()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);   // PodRequired = false for this customer
        var tripId = trip.GetProperty("tripId").GetInt64();
        var driverId = trip.GetProperty("driverId").GetInt32();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);
        var assigned = await TransitionAsync(admin, tripId, "Assigned", planned.GetProperty("rowVersion").GetString()!);
        var started = await TransitionAsync(admin, tripId, "Started", assigned.GetProperty("rowVersion").GetString()!, new { startOdometer = 100 });
        var inTransit = await TransitionAsync(admin, tripId, "InTransit", started.GetProperty("rowVersion").GetString()!);
        var atDelivery = await TransitionAsync(admin, tripId, "AtDelivery", inTransit.GetProperty("rowVersion").GetString()!);
        var delivered = await TransitionAsync(admin, tripId, "Delivered", atDelivery.GetProperty("rowVersion").GetString()!, new { endOdometer = 900 });

        var driverClient = factory.CreateClient().WithToken(TestTokens.ForScoped(vehicles.Tenant, "Own Driver", driverId, null, null, driverId));
        var refused = await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);

        await driverClient.PostAsync($"/api/trips/{tripId}/pod", PdfForm());
        var completed = await (await driverClient.PostAsJsonAsync($"/api/trips/{tripId}/status/Completed", new { rowVersion = delivered.GetProperty("rowVersion").GetString() })).DataAsync();
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reporting_an_issue_can_put_the_trip_on_hold_and_the_issue_can_be_resolved()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();
        var planned = await TransitionAsync(admin, tripId, "Planned", trip.GetProperty("rowVersion").GetString()!);

        var issue = await (await admin.PostAsJsonAsync($"/api/trips/{tripId}/issues",
            new { issueType = "Breakdown", severity = "High", description = "Flat tyre near Sukheki", putOnHold = true })).DataAsync();
        Assert.False(issue.GetProperty("isResolved").GetBoolean());

        var afterReport = await (await admin.GetAsync($"/api/trips/{tripId}")).DataAsync();
        Assert.Equal("OnHold", afterReport.GetProperty("status").GetString());

        var resolved = await (await admin.PostAsJsonAsync($"/api/trip-issues/{issue.GetProperty("tripIssueId").GetInt64()}/resolve", new { resolutionNotes = "Tyre changed" })).DataAsync();
        Assert.True(resolved.GetProperty("isResolved").GetBoolean());

        var resolveAgain = await admin.PostAsJsonAsync($"/api/trip-issues/{issue.GetProperty("tripIssueId").GetInt64()}/resolve", new { });
        Assert.Equal(HttpStatusCode.Conflict, resolveAgain.StatusCode);
    }

    [Fact]
    public async Task Viewing_or_acting_on_a_trip_s_events_needs_permission_or_being_its_own_driver()
    {
        var (vehicles, admin) = await WorldAsync(factory);
        var (trip, _) = await DraftTripAsync(vehicles, admin);
        var tripId = trip.GetProperty("tripId").GetInt64();

        var stranger = vehicles.As("Stranger", 99);   // no permissions, not this trip's driver
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/trips/{tripId}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/trips/{tripId}/events", new { eventType = "Note", remarks = "x" })).StatusCode);
    }
}
