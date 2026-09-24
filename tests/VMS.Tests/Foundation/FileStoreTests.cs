using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Core.Files;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Tests.Infrastructure;
using Xunit;

namespace VMS.Tests.Foundation;

/// <summary>S0-FND-10: files on disk, encrypted, scanned, hashed, and served only through short-lived links.</summary>
[Collection(ApiCollection.Name)]
public sealed class FileStoreTests(ApiFactory factory)
{
    private static readonly FileOwner Vehicle = new("Vehicle", "VH-26-0001");

    // ── Sample content ──────────────────────────────────────────────────────────────

    private static byte[] Pdf(int length = 400) => Fill("%PDF-1.4\n"u8, length);
    private static byte[] Png(int length = 400) => Fill([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);
    private static byte[] Zip(int length = 400) => Fill([0x50, 0x4B, 0x03, 0x04], length);

    private static byte[] Fill(ReadOnlySpan<byte> header, int length)
    {
        var bytes = new byte[Math.Max(length, header.Length)];
        Random.Shared.NextBytes(bytes);
        header.CopyTo(bytes);
        return bytes;
    }

    /// <summary>The EICAR antivirus test string, assembled here so this file is not itself flagged by an antivirus.</summary>
    private static byte[] Eicar() =>
        Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}" + "$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!" + "$H+H*");

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // ── Plumbing ────────────────────────────────────────────────────────────────────

    private T Service<T>(out IServiceScope scope) where T : notnull
    {
        factory.CreateClient();
        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    private async Task<StoredFile> SaveAsync(Guid tenant, byte[] content, string name = "Registration Book.pdf", FileRules? rules = null, FileOwner? owner = null)
    {
        var store = Service<IFileStore>(out var scope);
        using (scope)
        {
            using var stream = new MemoryStream(content);
            return await store.SaveAsync(new FileUpload(stream, name, owner ?? Vehicle, rules ?? FileRules.Scans, tenant));
        }
    }

    private string PathOf(StoredFile file) => Path.Combine(factory.FileRoot, file.StorageKey.Replace('/', Path.DirectorySeparatorChar));

    private int FilesOnDisk() => Directory.Exists(factory.FileRoot) ? Directory.GetFiles(factory.FileRoot, "*", SearchOption.AllDirectories).Length : 0;

    private DownloadLink LinkFor(Guid tenant, StoredFile file, string? name = null, bool inline = false, TimeSpan? lifetime = null)
    {
        var links = Service<IFileDownloadLinks>(out var scope);
        using (scope) return links.Create(tenant, file, name, inline, lifetime);
    }

    // ── Storing ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_file_is_stored_under_a_generated_name_in_a_dated_folder_by_owner()
    {
        var tenant = Guid.NewGuid();
        var content = Pdf();

        var stored = await SaveAsync(tenant, content, "Registration Book.pdf");

        var now = DateTime.UtcNow;
        Assert.Matches($"^{tenant:N}/{now:yyyy}/{now:MM}/Vehicle/VH-26-0001/[0-9a-f]{{32}}$", stored.StorageKey);
        Assert.DoesNotContain("Registration", stored.StorageKey);
        Assert.Equal("Registration Book.pdf", stored.OriginalFileName);   // the user's name survives as metadata
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.True(File.Exists(PathOf(stored)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(PathOf(stored))!, "*.tmp"));
    }

    [Fact]
    public async Task The_sha256_of_the_original_is_returned_so_it_can_be_shown_unaltered()
    {
        var content = Pdf(1234);

        var stored = await SaveAsync(Guid.NewGuid(), content);

        Assert.Equal(Sha256Hex(content), stored.Sha256);
    }

    [Fact]
    public async Task What_is_on_disk_is_encrypted_and_reads_back_exactly()
    {
        var tenant = Guid.NewGuid();
        var content = Pdf(2000);
        var stored = await SaveAsync(tenant, content);

        var onDisk = await File.ReadAllBytesAsync(PathOf(stored));
        Assert.NotEqual(content, onDisk);
        Assert.False(onDisk.AsSpan().IndexOf("%PDF-"u8) >= 0, "The plain content must not be readable on disk.");

        var store = Service<IFileStore>(out var scope);
        using (scope)
        {
            await using var read = await store.OpenReadAsync(tenant, stored.StorageKey);
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            Assert.Equal(content, copy.ToArray());
        }
    }

    [Fact]
    public async Task Any_change_to_the_bytes_on_disk_is_caught()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var onDisk = await File.ReadAllBytesAsync(PathOf(stored));
        onDisk[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(PathOf(stored), onDisk);

        var store = Service<IFileStore>(out var scope);
        using (scope)
            await Assert.ThrowsAsync<FileIntegrityException>(() => store.OpenReadAsync(tenant, stored.StorageKey));
    }

    [Fact]
    public async Task A_file_moved_to_another_path_does_not_open()
    {
        // Copying one document over another's path must not make it show up as the other document.
        var tenant = Guid.NewGuid();
        var first = await SaveAsync(tenant, Pdf(), "a.pdf", owner: new FileOwner("Vehicle", "VH-A"));
        var second = await SaveAsync(tenant, Pdf(), "b.pdf", owner: new FileOwner("Vehicle", "VH-B"));
        File.Copy(PathOf(first), PathOf(second), overwrite: true);

        var store = Service<IFileStore>(out var scope);
        using (scope)
            await Assert.ThrowsAsync<FileIntegrityException>(() => store.OpenReadAsync(tenant, second.StorageKey));
    }

    // ── Refusing ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("notes.pdf", "this is plain text pretending to be a pdf")]
    [InlineData("setup.pdf", "MZ\u0090\u0000\u0003\u0000\u0000\u0000")]   // a Windows executable
    [InlineData("page.pdf", "<html><script>alert(1)</script></html>")]
    public async Task What_a_file_is_decides_not_what_it_is_called(string name, string text)
    {
        var before = FilesOnDisk();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            SaveAsync(Guid.NewGuid(), Encoding.Latin1.GetBytes(text), name));

        Assert.Equal("Only PDF, PNG or JPEG files are accepted.", ex.Message);
        Assert.Equal(before, FilesOnDisk());   // nothing was left behind
    }

    [Fact]
    public async Task An_image_named_as_a_pdf_is_stored_as_the_image_it_is()
    {
        var stored = await SaveAsync(Guid.NewGuid(), Png(), "scan.pdf");

        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal("scan.pdf", stored.OriginalFileName);
    }

    [Fact]
    public async Task Office_documents_are_accepted_only_where_the_caller_allows_them()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => SaveAsync(Guid.NewGuid(), Zip(), "agreement.docx", FileRules.Scans));

        var word = await SaveAsync(Guid.NewGuid(), Zip(), "agreement.docx", FileRules.Documents);
        var excel = await SaveAsync(Guid.NewGuid(), Zip(), "fleet.xlsx", FileRules.Documents);
        Assert.Contains("wordprocessingml", word.ContentType);
        Assert.Contains("spreadsheetml", excel.ContentType);

        await Assert.ThrowsAsync<BadRequestException>(() => SaveAsync(Guid.NewGuid(), Zip(), "archive.zip", FileRules.Documents));
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => SaveAsync(Guid.NewGuid(), []));

        Assert.Equal("The file is empty.", ex.Message);
    }

    [Fact]
    public async Task A_file_over_the_callers_limit_is_refused_with_the_limit_in_the_message()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            SaveAsync(Guid.NewGuid(), Pdf(200_000), rules: FileRules.Scans with { MaxBytes = 100 * 1024 }));

        Assert.Equal("The file is larger than the 100 KB limit.", ex.Message);
    }

    [Fact]
    public async Task A_caller_cannot_raise_the_platform_limit()
    {
        var before = FilesOnDisk();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            SaveAsync(Guid.NewGuid(), Pdf(11 * 1024 * 1024), rules: FileRules.Scans with { MaxBytes = 500L * 1024 * 1024 }));

        Assert.Equal("The file is larger than the 10 MB limit.", ex.Message);
        Assert.Equal(before, FilesOnDisk());
    }

    [Theory]
    [InlineData("../etc", "1")]
    [InlineData("Vehicle", "..\\..\\x")]
    [InlineData("Vehicle", "a/b")]
    [InlineData("", "1")]
    public async Task A_file_owner_cannot_reach_outside_its_folder(string ownerType, string ownerId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(Guid.NewGuid(), Pdf(), owner: new FileOwner(ownerType, ownerId)));
    }

    [Fact]
    public async Task A_file_name_with_folders_in_it_is_kept_only_as_its_last_part()
    {
        var stored = await SaveAsync(Guid.NewGuid(), Pdf(), @"C:\Users\ali\Desktop\..\Insurance Certificate.pdf");

        Assert.Equal("Insurance Certificate.pdf", stored.OriginalFileName);
    }

    // ── Virus scan ──────────────────────────────────────────────────────────────────

    private sealed class CapturingAlert : IFileScanAlert
    {
        public static readonly ConcurrentBag<FileThreat> Threats = [];
        public Task ThreatFoundAsync(FileThreat threat)
        {
            Threats.Add(threat);
            return Task.CompletedTask;
        }
    }

    private sealed class BrokenAlert : IFileScanAlert
    {
        public Task ThreatFoundAsync(FileThreat threat) => throw new InvalidOperationException("Mail server down.");
    }

    [Fact]
    public async Task An_infected_file_is_rejected_stores_nothing_and_alerts_the_administrator()
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IFileScanAlert, CapturingAlert>()));
        host.CreateClient();
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var tenant = Guid.NewGuid();
        var before = FilesOnDisk();
        var infected = Pdf(300).Concat(Eicar()).ToArray();   // a genuine-looking PDF carrying the test signature

        var ex = await Assert.ThrowsAsync<BadRequestException>(async () =>
        {
            using var stream = new MemoryStream(infected);
            await store.SaveAsync(new FileUpload(stream, "invoice.pdf", Vehicle, FileRules.Scans, tenant));
        });

        Assert.Equal("The file was rejected because it may contain a virus. The administrator has been notified.", ex.Message);
        Assert.Equal(before, FilesOnDisk());
        var alert = Assert.Single(CapturingAlert.Threats, t => t.TenantId == tenant);
        Assert.Equal("EICAR-Test-File", alert.Threat);
        Assert.Equal("invoice.pdf", alert.FileName);
        Assert.Equal("Vehicle", alert.OwnerType);
    }

    [Fact]
    public async Task A_broken_alert_channel_never_lets_an_infected_file_through()
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IFileScanAlert, BrokenAlert>()));
        host.CreateClient();
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var before = FilesOnDisk();

        await Assert.ThrowsAsync<BadRequestException>(async () =>
        {
            using var stream = new MemoryStream(Pdf(300).Concat(Eicar()).ToArray());
            await store.SaveAsync(new FileUpload(stream, "invoice.pdf", Vehicle, FileRules.Scans, Guid.NewGuid()));
        });

        Assert.Equal(before, FilesOnDisk());
    }

    // ── Who can reach what ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_file_belongs_to_its_tenant_and_no_other()
    {
        var owner = Guid.NewGuid();
        var stored = await SaveAsync(owner, Pdf());
        var store = Service<IFileStore>(out var scope);
        using var _ = scope;

        await Assert.ThrowsAsync<NotFoundException>(() => store.OpenReadAsync(Guid.NewGuid(), stored.StorageKey));
        Assert.False(await store.ExistsAsync(Guid.NewGuid(), stored.StorageKey));
        Assert.True(await store.ExistsAsync(owner, stored.StorageKey));
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("0123456789abcdef0123456789abcdef/2026/03/Vehicle/../../../../x")]
    [InlineData("")]
    public async Task A_key_that_is_not_one_we_issued_never_touches_the_disk(string key)
    {
        var store = Service<IFileStore>(out var scope);
        using var _ = scope;

        await Assert.ThrowsAsync<NotFoundException>(() => store.OpenReadAsync(Guid.NewGuid(), key));
        await Assert.ThrowsAsync<NotFoundException>(() => store.DeleteAsync(Guid.NewGuid(), key));
    }

    [Fact]
    public async Task A_deleted_file_is_gone_and_deleting_twice_is_harmless()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var store = Service<IFileStore>(out var scope);
        using var _ = scope;

        await store.DeleteAsync(tenant, stored.StorageKey);
        await store.DeleteAsync(tenant, stored.StorageKey);

        Assert.False(await store.ExistsAsync(tenant, stored.StorageKey));
        Assert.False(File.Exists(PathOf(stored)));
    }

    // ── Download links ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_link_serves_the_file_without_a_sign_in_and_without_giving_away_where_it_is()
    {
        var tenant = Guid.NewGuid();
        var content = Pdf(3000);
        var stored = await SaveAsync(tenant, content, "Registration Book.pdf");
        var link = LinkFor(tenant, stored);

        Assert.StartsWith("/api/files/download/", link.Url);
        Assert.DoesNotContain(stored.StorageKey, link.Url);
        Assert.DoesNotContain(tenant.ToString("N"), link.Url);
        Assert.DoesNotContain("Vehicle", link.Url);
        Assert.DoesNotContain("Registration", link.Url);

        using var browser = factory.CreateClient();   // no token, like a browser opening a link
        var response = await browser.GetAsync(link.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("Registration Book.pdf", response.Content.Headers.ContentDisposition.FileName?.Trim('"') ?? response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_link_can_ask_for_a_preview_and_a_different_download_name()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var link = LinkFor(tenant, stored, name: "VH-26-0001 registration.pdf", inline: true);

        using var browser = factory.CreateClient();
        var response = await browser.GetAsync(link.Url);

        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("VH-26-0001 registration.pdf", response.Content.Headers.ContentDisposition.ToString().Replace("%20", " "));
    }

    [Fact]
    public async Task A_link_stops_working_when_its_time_is_up()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var link = LinkFor(tenant, stored, lifetime: TimeSpan.FromSeconds(1));
        using var browser = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(link.Url)).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(link.Url)).StatusCode);
    }

    [Fact]
    public async Task A_link_is_short_lived_by_default_and_never_lasts_more_than_fifteen_minutes()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());

        var normal = LinkFor(tenant, stored);
        var greedy = LinkFor(tenant, stored, lifetime: TimeSpan.FromDays(1));

        Assert.InRange(normal.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(1), DateTime.UtcNow.AddMinutes(2).AddSeconds(5));
        Assert.InRange(greedy.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(15).AddSeconds(5));
    }

    [Fact]
    public async Task A_link_that_was_altered_or_invented_is_refused_the_same_way_as_an_expired_one()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var link = LinkFor(tenant, stored);
        using var browser = factory.CreateClient();
        var altered = link.Url[..^4] + (link.Url.EndsWith("AAAA") ? "BBBB" : "AAAA");

        foreach (var url in new[] { altered, "/api/files/download/not-a-token", "/api/files/download/" + new string('A', 300) })
        {
            var response = await browser.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("This download link is not valid or has expired.", await response.MessageAsync());
        }
    }

    [Fact]
    public async Task A_file_that_no_longer_matches_its_recorded_hash_is_not_served()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var link = LinkFor(tenant, stored with { Sha256 = new string('0', 64) });
        using var browser = factory.CreateClient();

        var response = await browser.GetAsync(link.Url);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("checksum", await response.MessageAsync());
    }

    [Fact]
    public async Task A_file_altered_on_disk_is_not_served()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var onDisk = await File.ReadAllBytesAsync(PathOf(stored));
        onDisk[^2] ^= 0x55;
        await File.WriteAllBytesAsync(PathOf(stored), onDisk);
        using var browser = factory.CreateClient();

        var response = await browser.GetAsync(LinkFor(tenant, stored).Url);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_link_to_a_file_that_has_since_been_deleted_is_not_found()
    {
        var tenant = Guid.NewGuid();
        var stored = await SaveAsync(tenant, Pdf());
        var link = LinkFor(tenant, stored);
        File.Delete(PathOf(stored));
        using var browser = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(link.Url)).StatusCode);
    }

    // ── Configuration ───────────────────────────────────────────────────────────────

    [Fact]
    public void Outside_development_the_application_will_not_start_without_a_folder_and_a_key()
    {
        var key = Convert.ToBase64String(new byte[32]);

        Assert.Throws<InvalidOperationException>(() => new FileStorageOptions().Validate(isDevelopment: false));
        Assert.Throws<InvalidOperationException>(() => new FileStorageOptions { RootPath = @"D:\files" }.Validate(isDevelopment: false));
        Assert.Throws<InvalidOperationException>(() => new FileStorageOptions { EncryptionKey = key }.Validate(isDevelopment: false));
        new FileStorageOptions { RootPath = @"D:\files", EncryptionKey = key }.Validate(isDevelopment: false);   // no throw
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]   // valid base64, far too short
    public void A_key_must_be_256_bits_of_base64_even_in_development(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new FileStorageOptions { EncryptionKey = key }.Validate(isDevelopment: true));
    }

    [Fact]
    public void Development_needs_no_setup()
    {
        new FileStorageOptions().Validate(isDevelopment: true);   // no throw
    }
}
