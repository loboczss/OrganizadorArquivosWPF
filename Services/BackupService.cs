// File: Services/BackupService.cs
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using OrganizadorArquivosWPF.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizadorArquivosWPF.Services;

public sealed class BackupService
{
    #region Constantes
    private const string SPDomain = "oneengenharia.sharepoint.com";
    private const string SPSitePath = "OneEngenharia";
    private const string DocumentLibrary = "DatalogGERAL";

    private const int MaxConcurrentUploads = 4;
    private const string CachePath =
        @"%LOCALAPPDATA%\OneEngRenamer\BackupCache.json";
    private const string PendingCsv =
        @"%TEMP%\BackupPendentes.csv";
    #endregion

    private readonly GraphServiceClient _graph;
    private readonly ReliableSharePointService _uploader;
    private readonly BackupCache _cache;
    private string? _driveId;

    private readonly LoggerService _log = LoggerService.Instance;

    public BackupService()
    {
        _uploader = new ReliableSharePointService(
            Config.TenantId, Config.ClientId, Config.ClientSecret,
            SPDomain, $"/sites/{SPSitePath}", DocumentLibrary);

        var cred = new ClientSecretCredential(
            Config.TenantId, Config.ClientId, Config.ClientSecret);
        _graph = new GraphServiceClient(cred, new[] { "https://graph.microsoft.com/.default" });

        _cache = new BackupCache(
            Environment.ExpandEnvironmentVariables(CachePath));
    }

    #region Ponto de entrada público
    public async Task SincronizarTudoAsync(CancellationToken ct = default)
    {
        _ = await ObterDriveIdAsync(ct).ConfigureAwait(false);

        var pendentes = CarregarPendentes();
        var fila = new ConcurrentQueue<string>(pendentes
            .Concat(EnumerarPastasParaBackup())
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var resultados = new ConcurrentBag<FileUploadResult>();

        var workers = Enumerable.Range(0, MaxConcurrentUploads).Select(async _ =>
        {
            while (fila.TryDequeue(out var dir))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var list = await EnviarBackupAsync(dir, null, null, ct).ConfigureAwait(false);
                    foreach (var r in list) resultados.Add(r);
                    RemoverPendente(dir);
                }
                catch (Exception ex)
                {
                    _log.Error($"Falha em '{dir}': {ex.Message}");
                    RegistrarPendente(dir);
                }
            }
        });

        await Task.WhenAll(workers).ConfigureAwait(false);

        _cache.Save();
        _log.Info($"Backup: OK={resultados.Count(r => r.Verificado)} | Falhas={resultados.Count(r => !r.Verificado)}");
    }

    public Task SincronizarPastasRenomeacaoAsync() =>
    SincronizarTudoAsync();

    public Task SincronizarPastasAsync(string _ = null) =>
        SincronizarTudoAsync();

    #endregion

    #region Enviar pasta
    public async Task<IReadOnlyList<FileUploadResult>> EnviarBackupAsync(
        string pasta,
        string? numOs = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var res = new List<FileUploadResult>();
        if (!Directory.Exists(pasta)) return res;

        progress?.Report(-1); // inicia barra indeterminada

        numOs ??= ExtrairOs(pasta);
        if (string.IsNullOrWhiteSpace(numOs)) return res;

        string driveId = await ObterDriveIdAsync(ct).ConfigureAwait(false);
        string folderId = await EnsureFolderAsync(driveId, numOs, ct).ConfigureAwait(false);

        // preenche cache com o que já existe remoto
        foreach (var n in await ArquivosRemotosAsync(driveId, folderId, ct).ConfigureAwait(false))
            _cache.Add(numOs, n);

        // arquivos locais faltantes
        var locais = Directory.GetFiles(pasta)
                              .Where(f => !_cache.Contains(numOs, Path.GetFileName(f)))
                              .ToArray();

        var sem = new SemaphoreSlim(MaxConcurrentUploads);
        int completed = 0;
        int total = locais.Length + 1;
        progress?.Report(total == 0 ? 100 : 0);
        var tasks = locais.Select(async file =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _uploader.UploadFileAsync(file, $"{numOs}/{Path.GetFileName(file)}", ct).ConfigureAwait(false);
                _cache.Add(numOs, Path.GetFileName(file));
                res.Add(new(Path.GetFileName(file), true, CalcularSha1(file)));
            }
            catch (Exception ex)
            {
                _log.Error($"Upload '{file}': {ex.Message}");
                res.Add(new(Path.GetFileName(file), false, string.Empty));
            }
            finally
            {
                sem.Release();
                int done = Interlocked.Increment(ref completed);
                progress?.Report(done * 100.0 / total);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // ZIP
        string zip = CriarZipTemporario(pasta, out string zipName);
        if (!_cache.Contains(numOs, zipName))
        {
            try
            {
                await _uploader.UploadFileAsync(zip, $"{numOs}/{zipName}", ct).ConfigureAwait(false);
                _cache.Add(numOs, zipName);
                res.Add(new(zipName, true, CalcularSha1(zip)));
            }
            catch (Exception ex)
            {
                _log.Error($"Upload zip '{zip}': {ex.Message}");
                res.Add(new(zipName, false, string.Empty));
            }
        }
        try { File.Delete(zip); } catch (Exception ex) { _log.Warning($"Falha ao remover arquivo temporário '{zip}': {ex.Message}"); }

        progress?.Report(100);

        _cache.Save();
        return res;
    }
    #endregion

    #region Listagem & helpers
    private static IEnumerable<string> EnumerarPastasParaBackup() =>
        RenamerService.EnumerarPastasBase()
            .SelectMany(d => Directory.EnumerateDirectories(d, "*", SearchOption.AllDirectories))
            .Where(d => Directory.EnumerateFiles(d).Any());

    private async Task<HashSet<string>> ArquivosRemotosAsync(
        string driveId, string folderId, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = await _graph.Drives[driveId]
                               .Items[folderId].Children
                               .GetAsync(cancellationToken: ct)
                               .ConfigureAwait(false);
        foreach (var it in page?.Value ?? Enumerable.Empty<DriveItem>())
            if (it.File != null) set.Add(it.Name);
        return set;
    }

    private async Task<string> ObterDriveIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_driveId)) return _driveId;

        var site = await _graph.Sites[$"{SPDomain}:/sites/{SPSitePath}"]
                               .GetAsync(cancellationToken: ct)
                               .ConfigureAwait(false);
        var drive = (await _graph.Sites[site.Id].Drives
                                  .GetAsync(cancellationToken: ct)
                                  .ConfigureAwait(false))
                    .Value.First(d => d.Name == DocumentLibrary);
        _driveId = drive.Id;
        return _driveId;
    }

    private async Task<string> EnsureFolderAsync(
        string driveId, string nome, CancellationToken ct)
    {
        try
        {
            return (await _graph.Drives[driveId].Root
                                 .ItemWithPath(nome)
                                 .GetAsync(cancellationToken: ct)
                                 .ConfigureAwait(false)).Id;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            var item = new DriveItem
            {
                Name = nome,
                Folder = new Folder(),
                AdditionalData = new Dictionary<string, object>
                {
                    ["@microsoft.graph.conflictBehavior"] = "fail"
                }
            };
            return (await _graph.Drives[driveId].Items["root"].Children
                                   .PostAsync(item, cancellationToken: ct)
                                   .ConfigureAwait(false))!.Id;
        }
    }

    private static string ExtrairOs(string pasta)
    {
        var dir = Path.GetFileName(pasta);
        if (dir == null) return null;
        var p = dir.Split('_');
        var os = p[0];
        return os.Length > 2 && os[2..].All(c => c == '0')
            ? (p.Length > 1 ? $"{p[1]}_instalacao" : null)
            : os;
    }

    private static string CriarZipTemporario(string pasta, out string nome)
    {
        nome = $"{Path.GetFileName(pasta)}.zip";
        string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(pasta, tmp, CompressionLevel.SmallestSize, false);
        return tmp;
    }

    private static string CalcularSha1(string file)
    {
        using var sha1 = SHA1.Create();
        using var fs = File.OpenRead(file);
        return Convert.ToBase64String(sha1.ComputeHash(fs));
    }
    #endregion

    #region CSV pendentes
    private static readonly ConcurrentDictionary<string, byte> _pend = new(StringComparer.OrdinalIgnoreCase);

    private static void RegistrarPendente(string pasta)
    {
        if (_pend.TryAdd(pasta, 0))
            File.AppendAllLines(Environment.ExpandEnvironmentVariables(PendingCsv), new[] { pasta });
    }

    private static void RemoverPendente(string pasta)
    {
        if (_pend.TryRemove(pasta, out _))
            File.WriteAllLines(Environment.ExpandEnvironmentVariables(PendingCsv), _pend.Keys);
    }

    private static List<string> CarregarPendentes()
    {
        var path = Environment.ExpandEnvironmentVariables(PendingCsv);
        if (!File.Exists(path)) return new();
        var list = File.ReadAllLines(path)
                       .Where(l => !string.IsNullOrWhiteSpace(l))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();
        foreach (var p in list) _pend.TryAdd(p, 0);
        return list;
    }
    #endregion
}

/// Resultado individual
public sealed record FileUploadResult(string Nome, bool Verificado, string Sha1Hash);
