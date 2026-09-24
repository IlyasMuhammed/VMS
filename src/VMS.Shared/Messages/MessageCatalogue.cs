using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Exceptions;

namespace VMS.Shared.Messages;

internal sealed partial class MessageCatalogue : IMessageCatalogue
{
    private const string DefaultLocale = "en";
    private static readonly System.Text.RegularExpressions.Regex LocaleName = LocalePattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")]
    private static partial System.Text.RegularExpressions.Regex LocalePattern();

    private readonly string? _directory;
    private readonly Dictionary<string, string> _builtIn;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Stamp, Dictionary<string, string> Messages)> _files = [];

    public MessageCatalogue(IConfiguration configuration)
    {
        _directory = configuration["Messages:Directory"] is { Length: > 0 } dir ? Path.GetFullPath(dir) : null;

        using var stream = typeof(MessageCatalogue).Assembly.GetManifestResourceStream("VMS.Shared.Messages.messages.en.json")
            ?? throw new InvalidOperationException("The built-in messages resource is missing.");
        _builtIn = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The built-in messages resource is empty.");
    }

    public bool Has(string code) => Merged(DefaultLocale).ContainsKey(code);

    public string Text(string code, params (string Name, object? Value)[] values) => Text(DefaultLocale, code, values);

    public string Text(string locale, string code, params (string Name, object? Value)[] values)
    {
        if (!Merged(locale).TryGetValue(code, out var template))
            throw new ArgumentException($"There is no message '{code}'.", nameof(code));
        return MessageFormat.Format(template, MessageFormat.Values(values));
    }

    public IReadOnlyDictionary<string, string> All(string locale = DefaultLocale) => Merged(locale);

    public ValidationError Error(string? field, string code, params (string Name, object? Value)[] values) =>
        new(field, code, Text(code, values), values.Length == 0 ? null : MessageFormat.Values(values));

    /// <summary>English, then any English rewording from the folder, then the requested language on top.</summary>
    private Dictionary<string, string> Merged(string locale)
    {
        locale = LocaleName.IsMatch(locale ?? string.Empty) ? locale!.ToLowerInvariant() : DefaultLocale;
        var merged = new Dictionary<string, string>(_builtIn);
        foreach (var (code, text) in File(DefaultLocale)) merged[code] = text;
        if (locale != DefaultLocale)
            foreach (var (code, text) in File(locale)) merged[code] = text;
        return merged;
    }

    /// <summary>The folder's file for a language, re-read when it changes. A missing or broken file is skipped, never fatal.</summary>
    private Dictionary<string, string> File(string locale)
    {
        if (_directory is null) return [];
        var path = Path.Combine(_directory, $"messages.{locale}.json");
        if (!System.IO.File.Exists(path)) return [];

        lock (_gate)
        {
            var stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (_files.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Messages;

            try
            {
                var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(path)) ?? [];
                // A message whose placeholders the code would not fill would fail on use; ignore that one rather than break the rest.
                messages = messages.Where(kv => _builtIn.TryGetValue(kv.Key, out var original) &&
                    MessageFormat.Placeholders(kv.Value).All(MessageFormat.Placeholders(original).Contains)).ToDictionary(kv => kv.Key, kv => kv.Value);
                _files[path] = (stamp, messages);
                return messages;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _files[path] = (stamp, []);
                return [];
            }
        }
    }
}

public static class MessageServiceExtensions
{
    public static IServiceCollection AddMessages(this IServiceCollection services)
    {
        services.AddSingleton<IMessageCatalogue, MessageCatalogue>();
        return services;
    }
}
