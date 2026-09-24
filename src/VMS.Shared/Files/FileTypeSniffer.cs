namespace VMS.Shared.Files;

/// <summary>
/// Decides what a file is from its first bytes. The name and the browser's content type are what the
/// sender says; the bytes are what the file is.
/// </summary>
public static class FileTypeSniffer
{
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>The single kind the content is, or <see cref="FileKinds.None"/> when it is not one we know.</summary>
    /// <remarks>
    /// An office document is a zip archive, and the first bytes cannot tell a .docx from a .xlsx or from any
    /// other zip, so those two are decided by the extension as well. An archive that is not named as one of
    /// them is not accepted.
    /// </remarks>
    public static FileKinds Detect(ReadOnlySpan<byte> head, string fileName)
    {
        if (head.StartsWith(Pdf)) return FileKinds.Pdf;
        if (head.StartsWith(Png)) return FileKinds.Png;
        if (head.StartsWith(Jpeg)) return FileKinds.Jpeg;

        if (head.StartsWith(Zip))
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".docx" => FileKinds.Word,
                ".xlsx" => FileKinds.Excel,
                _ => FileKinds.None
            };
        }

        return FileKinds.None;
    }

    public static string ContentType(FileKinds kind) => kind switch
    {
        FileKinds.Pdf => "application/pdf",
        FileKinds.Png => "image/png",
        FileKinds.Jpeg => "image/jpeg",
        FileKinds.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        FileKinds.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    /// <summary>For messages: "PDF, PNG or JPEG".</summary>
    public static string Describe(FileKinds allowed)
    {
        var names = new List<string>();
        if (allowed.HasFlag(FileKinds.Pdf)) names.Add("PDF");
        if (allowed.HasFlag(FileKinds.Png)) names.Add("PNG");
        if (allowed.HasFlag(FileKinds.Jpeg)) names.Add("JPEG");
        if (allowed.HasFlag(FileKinds.Word)) names.Add("Word (.docx)");
        if (allowed.HasFlag(FileKinds.Excel)) names.Add("Excel (.xlsx)");
        return names.Count switch
        {
            0 => "no file type",
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " or " + names[^1]
        };
    }
}
