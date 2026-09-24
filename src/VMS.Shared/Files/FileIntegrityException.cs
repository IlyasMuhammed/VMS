namespace VMS.Shared.Files;

/// <summary>A stored file no longer matches what was written: it was altered, replaced or corrupted on disk.</summary>
public sealed class FileIntegrityException(string message, Exception? inner = null) : Exception(message, inner);
