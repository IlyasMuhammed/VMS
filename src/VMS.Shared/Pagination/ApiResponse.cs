namespace VMS.Shared.Pagination;

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

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
