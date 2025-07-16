namespace OrganizadorArquivosWPF.Models;

/// <summary>
/// Represents progress information for backup uploads.
/// </summary>
public readonly record struct UploadProgressInfo(double Percent, int Completed, int Total, string? FileName);
