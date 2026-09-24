using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using VMS.Shared.Pagination;

namespace VMS.Shared.Messages;

/// <summary>
/// Makes the framework's own "the request is not valid" answer (a missing required property, text too long, a date
/// in the wrong shape) look like every other rejection: HTTP 400, every problem at once, each tied to its field,
/// with message IDs from the catalogue where one applies.
/// </summary>
public static partial class ModelStateErrors
{
    [GeneratedRegex(@"^The (?<f>.+) field is required\.$")]
    private static partial Regex RequiredText();

    [GeneratedRegex(@"maximum length of '?(?<n>\d+)'?")]
    private static partial Regex MaxLengthText();

    [GeneratedRegex(@"minimum length of '?(?<n>\d+)'?")]
    private static partial Regex MinLengthText();

    [GeneratedRegex(@"is not a valid e-?mail address", RegexOptions.IgnoreCase)]
    private static partial Regex EmailText();

    /// <summary>Plug in with <c>ConfigureApiBehaviorOptions(o =&gt; o.InvalidModelStateResponseFactory = ModelStateErrors.Response)</c>.</summary>
    public static IActionResult Response(ActionContext context)
    {
        var messages = context.HttpContext.RequestServices.GetRequiredService<IMessageCatalogue>();
        var errors = new List<ApiError>();

        foreach (var (key, entry) in context.ModelState)
        {
            var field = FieldName(key);
            foreach (var error in entry.Errors)
                errors.Add(ToApiError(messages, field, error.ErrorMessage, error.Exception));
        }

        // A body that could not be read at all leaves no entries: still say something useful.
        if (errors.Count == 0) errors.Add(ToApiError(messages, null, string.Empty, null));

        var response = new ApiResponse
        {
            Success = false,
            Message = errors.Count == 1 ? errors[0].Message : messages.Text(Msg.CorrectFields),
            Errors = errors
        };
        return new BadRequestObjectResult(response);
    }

    private static ApiError ToApiError(IMessageCatalogue messages, string? field, string errorMessage, Exception? exception)
    {
        var label = Humanize(field);
        var text = string.IsNullOrWhiteSpace(errorMessage) ? exception?.Message ?? string.Empty : errorMessage;

        ApiError From(string code, params (string Name, object? Value)[] values) => new()
        {
            Field = field, Code = code, Message = messages.Text(code, values),
            Params = values.Length == 0 ? null : MessageFormat.Values(values)
        };

        if (RequiredText().IsMatch(text)) return From(Msg.Required, ("Field", label));
        if (MaxLengthText().Match(text) is { Success: true } max) return From(Msg.MaxLength, ("Field", label), ("Max", max.Groups["n"].Value));
        if (MinLengthText().Match(text) is { Success: true } min) return From(Msg.MinLength, ("Field", label), ("Min", min.Groups["n"].Value));
        if (EmailText().IsMatch(text)) return From(Msg.Email);

        // The serialiser's own message (with a path and a byte offset) means nothing to a user; a message our own
        // converters wrote (for example "must be ISO 8601 with a UTC offset") does.
        // MVC may carry that message as an exception or as plain text, so it is recognised by what it says.
        var framework = text.Contains("Path:", StringComparison.Ordinal) || text.Contains("LineNumber", StringComparison.Ordinal)
                        || text.StartsWith("The JSON value", StringComparison.Ordinal) || text.Contains("System.", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(text) || framework || text == "The input was not valid.") return From(Msg.Invalid, ("Field", label));

        return new ApiError { Field = field, Code = Msg.Invalid, Message = text };
    }

    /// <summary><c>Name</c> → <c>name</c>, <c>Address.City</c> → <c>address.city</c>, <c>$.legalName</c> → <c>legalName</c>; empty for the whole body.</summary>
    public static string? FieldName(string key)
    {
        var trimmed = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key == "$" ? string.Empty : key;
        if (trimmed.Length == 0) return null;
        return string.Join('.', trimmed.Split('.').Select(s => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..]));
    }

    /// <summary><c>legalName</c> → <c>Legal name</c>: for a message when only the field's name is known.</summary>
    public static string Humanize(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "This field";
        var last = field.Split('.')[^1];
        var spaced = Regex.Replace(last, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
