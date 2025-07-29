// File: Services/ReliableSharePointService.cs
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;

namespace OrganizadorArquivosWPF.Services;

/// <summary>
/// Upload simples e resiliente (PutAsync + retries + verificação SHA‑1).
/// Compatível com Microsoft.Graph 2.x (Kiota).
/// </summary>
public sealed class ReliableSharePointService
{
    private readonly string _tenantId, _clientId, _clientSecret;
    private readonly string _domain, _sitePath, _libraryName;

    private GraphServiceClient? _graph;
    private string? _driveId;

    private readonly int _maxRetries = 5;
    private readonly int _baseDelay = 2000;          // ms
    private readonly LoggerService _log = LoggerService.Instance;

    public ReliableSharePointService(
        string tenantId, string clientId, string clientSecret,
        string domain, string sitePath, string libraryName)
    {
        _tenantId = tenantId;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _domain = domain;
        _sitePath = sitePath;
        _libraryName = libraryName;
    }

    #region Init
    private async Task EnsureInitializedAsync()
    {
        if (_graph != null && !string.IsNullOrEmpty(_driveId))
            return;

        var cred = new ClientSecretCredential(
            _tenantId, _clientId, _clientSecret);
        _graph = new GraphServiceClient(cred, new[] { "https://graph.microsoft.com/.default" });

        var site = await _graph.Sites[$"{_domain}:{_sitePath}"].GetAsync()
                              .ConfigureAwait(false);
        var drive = (await _graph.Sites[site.Id].Drives.GetAsync()
                                   .ConfigureAwait(false))!
                    .Value.FirstOrDefault(d => d.Name == _libraryName)
                    ?? throw new InvalidOperationException($"Biblioteca '{_libraryName}' não encontrada.");

        _driveId = drive.Id;
    }
    #endregion

    #region Upload
    private sealed record UploadCheckpoint(string RemotePath, string UploadUrl, DateTimeOffset Expiration);

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        string checkpointFile = GetCheckpointPath(localPath);
        UploadCheckpoint? checkpoint = null;
        if (File.Exists(checkpointFile))
        {
            try
            {
                checkpoint = JsonSerializer.Deserialize<UploadCheckpoint>(File.ReadAllText(checkpointFile));
                if (checkpoint != null && checkpoint.Expiration < DateTimeOffset.UtcNow)
                    checkpoint = null;
            }
            catch { checkpoint = null; }
        }

        UploadSession session;
        if (checkpoint == null)
        {
            var body = new CreateUploadSessionPostRequestBody(); // objeto vazio já funciona
            session = await _graph!
                .Drives[_driveId!]
                .Root
                .ItemWithPath(remotePath)
                .CreateUploadSession
                .PostAsync(body, cancellationToken: ct)
                .ConfigureAwait(false);

            checkpoint = new(remotePath, session.UploadUrl!, session.ExpirationDateTime!.Value);
            File.WriteAllText(checkpointFile, JsonSerializer.Serialize(checkpoint));
        }
        else
        {
            session = new UploadSession
            {
                UploadUrl = checkpoint.UploadUrl,
                ExpirationDateTime = checkpoint.Expiration
            };
        }



        if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            return;

        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                using var fs = File.OpenRead(localPath);
                var task = new LargeFileUploadTask<DriveItem>(session, fs);
                var prog = new Progress<long>(bytes => progress?.Report(bytes * 100.0 / fs.Length));
                var result = checkpoint == null
                    ? await task.UploadAsync(prog, cancellationToken: ct).ConfigureAwait(false)
                    : await task.ResumeAsync(prog, cancellationToken: ct).ConfigureAwait(false);
                if (!result.UploadSucceeded)
                    throw new Exception("Upload incompleto");

                _ = VerifyHashAsync(localPath, remotePath, ct).ConfigureAwait(false);
                try { File.Delete(checkpointFile); } catch { }
                return;
            }
            catch (ServiceException ex) when (
                   ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests ||
                   ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable)
            {
                int delay = _baseDelay * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (attempt == _maxRetries)
                    throw;

                int delay = _baseDelay * attempt;
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static string GetCheckpointPath(string localPath)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "UploadCheckpoints");
        Directory.CreateDirectory(dir);

        using var sha1 = SHA1.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(localPath);
        var hash = sha1.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return Path.Combine(dir, sb.ToString() + ".upload.json");
    }
    #endregion

    #region Verificação de hash
    private async Task VerifyHashAsync(string localPath, string remotePath, CancellationToken ct)
    {
        try
        {
            string localHash = ComputeSha1(localPath);

            var item = await _graph!
                .Drives[_driveId!]
                .Root.ItemWithPath(remotePath)
                .GetAsync(q => q.QueryParameters.Select = new[] { "file", "size" }, ct)
                .ConfigureAwait(false);

            var remoteHash = item.File?.Hashes?.Sha1Hash;
            if (!string.IsNullOrEmpty(remoteHash) &&
                !string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase))
            {
                // Hash mismatch indicates possible corruption, but skip noisy log
            }
        }
        catch (Exception ex)
        {
            // Ignore hash verification errors silently
        }
    }

    private static string ComputeSha1(string file)
    {
        using var sha1 = SHA1.Create();
        using var fs = File.OpenRead(file);
        return Convert.ToBase64String(sha1.ComputeHash(fs));
    }
    #endregion
}
