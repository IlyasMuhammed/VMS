using System.Text.Json;

namespace VMS.Shared.Common;

/// <summary>
/// The connection string "dotnet ef" uses in every module's design-time DbContext factory.
/// Resolves to the same database the API uses (<c>Data:mainOrg</c> in VMS.API's appsettings), so
/// migrations are listed and scripted against the real database. <c>VMS_DB_CONNECTION</c>, when set,
/// overrides it. There is deliberately no silent fallback: a fallback reports migrations as pending
/// on the wrong database.
/// </summary>
public static class DesignTimeConnection
{
    public const string EnvironmentVariable = "VMS_DB_CONNECTION";

    public static string Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var settingsPath = FindApiSettings()
            ?? throw new InvalidOperationException(
                $"Could not find VMS.API/appsettings.json above '{Directory.GetCurrentDirectory()}'. " +
                $"Run dotnet ef from inside the repository, or set {EnvironmentVariable}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (doc.RootElement.TryGetProperty("Data", out var data)
            && data.TryGetProperty("mainOrg", out var mainOrg)
            && mainOrg.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(mainOrg.GetString()))
            return mainOrg.GetString()!;

        throw new InvalidOperationException($"'Data:mainOrg' not found in {settingsPath}.");
    }

    private static string? FindApiSettings()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir.FullName, "VMS.API", "appsettings.json"),
                         Path.Combine(dir.FullName, "src", "VMS.API", "appsettings.json")
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
