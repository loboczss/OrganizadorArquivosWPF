// File: Services/ReliableSharePointService.cs
using System;
using System.IO;
using System.Linq;
using System.Net;
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

        var site = await _graph.Sites[$"{_domain}:{_sitePath}"].GetAsync();
        var drive = (await _graph.Sites[site.Id].Drives.GetAsync())!
                    .Value.FirstOrDefault(d => d.Name == _libraryName)
                    ?? throw new InvalidOperationException($"Biblioteca '{_libraryName}' não encontrada.");

        _driveId = drive.Id;
    }
    #endregion

    #region Upload
    public async Task UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();

        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                using var fs = File.OpenRead(localPath);
                await _graph!
                    .Drives[_driveId!]
                    .Root
                    .ItemWithPath(remotePath)
                    .Content
                    .PutAsync(fs, cancellationToken: ct);

                // opcional: verifica hash
                _ = VerifyHashAsync(localPath, remotePath, ct).ConfigureAwait(false);
                return;
            }
            catch (ServiceException ex) when (
                   ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests ||
                   ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable)
            {
                int delay = _baseDelay * (int)Math.Pow(2, attempt - 1);
                _log.Info($"Throttle {ex.ResponseStatusCode} '{remotePath}' (tentativa {attempt}/{_maxRetries}) – aguardando {delay} ms.");
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                if (attempt == _maxRetries)
                    throw; // estoura de vez

                int delay = _baseDelay * attempt;
                _log.Warning($"Erro upload '{remotePath}' tent. {attempt}: {ex.Message}. Retry em {delay} ms.");
                await Task.Delay(delay, ct);
            }
        }
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
                .GetAsync(q => q.QueryParameters.Select = new[] { "file", "size" }, ct);

            var remoteHash = item.File?.Hashes?.Sha1Hash;
            if (!string.IsNullOrEmpty(remoteHash) &&
                !string.Equals(remoteHash, localHash, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warning($"Hash divergente em '{remotePath}' – poss. corrupção!");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Falha ao verificar hash '{remotePath}': {ex.Message}");
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
