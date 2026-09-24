using System.Text.Json.Serialization;

namespace VMS.Shared.Pagination;

/// <summary>One problem with the input, in an error response: see <see cref="VMS.Shared.Exceptions.ValidationError"/>.</summary>
public sealed class ApiError
{
    /// <summary>The field, in the request's own (camelCase) spelling. Null when the problem is with the request as a whole.</summary>
    public string? Field { get; set; }
    /// <summary>The message ID, for example <c>VAL-BP-003</c>. The screen may reword or translate from it.</summary>
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>The values that filled the message's placeholders.</summary>
    public IReadOnlyDictionary<string, string?>? Params { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Every problem, when the input was rejected. Absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiError>? Errors { get; set; }

    /// <summary>
    /// A machine-readable failure code (for example <c>RATE_MISSING</c> or <c>CONCURRENCY_CONFLICT</c>), for a
    /// caller that branches on the failure rather than only showing <see cref="Message"/>. Null (and so absent
    /// from the JSON) for the ordinary success/validation responses every module already returns — set only where
    /// a service throws <see cref="VMS.Shared.Exceptions.BusinessRuleException"/> or a request is refused for
    /// lacking a permission. Additive: existing responses are unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    /// <summary>
    /// The same id already written to every audit row for this request (<c>IAuditContext.GroupId</c>), so a
    /// failure report can be matched back to the audit trail. Additive; null unless the middleware or the
    /// permission-denial handler populates it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; set; }

    public static ApiResponse Ok(string message = "") => new() { Success = true, Message = message };
    public static ApiResponse Fail(string message) => new() { Success = false, Message = message };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "") =>
        new() { Success = true, Message = message, Data = data };
}

public class PaginatedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
