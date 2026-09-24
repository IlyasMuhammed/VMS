namespace VMS.Shared.Exceptions;

/// <summary>
/// One thing wrong with what the user entered: which field (null for the form as a whole), the message ID, the
/// text in the user's language, and the values that filled the text (so the screen can reword it from its own
/// catalogue).
/// </summary>
public sealed record ValidationError(string? Field, string Code, string Message, IReadOnlyDictionary<string, string?>? Params = null);

/// <summary>
/// Thrown when input is not acceptable. Answered as HTTP 400 with every problem listed at once, each tied to its
/// field, so the screen can mark them all rather than reveal them one at a time.
/// Build the errors with <c>IMessageCatalogue.Error(...)</c>.
/// </summary>
public class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(IEnumerable<ValidationError> errors) : base(Summarise(errors.ToList())) => Errors = errors.ToList();

    public ValidationException(ValidationError error) : this([error]) { }

    private static string Summarise(IReadOnlyList<ValidationError> errors) =>
        errors.Count == 1 ? errors[0].Message : string.Join(" ", errors.Select(e => e.Message));
}
