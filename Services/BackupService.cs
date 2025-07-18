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

    private const int MaxConcurrentUploads = 8;
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
    public async Task<IReadOnlyList<FileUploadResult>> SincronizarTudoAsync(
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new UploadProgressInfo(-1, 0, 0, null));
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
                catch (Exception)
                {
                    RegistrarPendente(dir);
                }
            }
        });

        await Task.WhenAll(workers).ConfigureAwait(false);

        _cache.Save();
        int ok = resultados.Count(r => r.Verificado);
        var falhas = resultados.Where(r => !r.Verificado)
                               .Select(r => r.Nome)
                               .ToList();
        int fail = falhas.Count;
        progress?.Report(new UploadProgressInfo(100, ok + fail, ok + fail, null));
        _log.Info($"Backup: OK={ok} | Falhas={fail}{(fail > 0 ? $" ({string.Join(", ", falhas)})" : string.Empty)}");
        return resultados.ToList();
    }


    public Task<IReadOnlyList<FileUploadResult>> SincronizarPastasRenomeacaoAsync(
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken ct = default) =>
        SincronizarTudoAsync(progress, ct);

    public Task<IReadOnlyList<FileUploadResult>> SincronizarPastasAsync(string _ = null,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken ct = default) =>
        SincronizarTudoAsync(progress, ct);

    #endregion

    #region Enviar pasta
    public async Task<IReadOnlyList<FileUploadResult>> EnviarBackupAsync(
        string pasta,
        string? numOs = null,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var res = new List<FileUploadResult>();
        if (!Directory.Exists(pasta)) return res;

        progress?.Report(new UploadProgressInfo(-1, 0, 0, null)); // inicia barra indeterminada

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
        progress?.Report(total == 0 ?
            new UploadProgressInfo(100, total, total, null) :
            new UploadProgressInfo(0, 0, total, null));
        var tasks = locais.Select(async file =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var fileProgress = new Progress<double>(p =>
                {
                    double global = ((completed + p / 100.0) * 100.0) / total;
                    progress?.Report(new UploadProgressInfo(global, completed, total, Path.GetFileName(file)));
                });
                await _uploader.UploadFileAsync(file, $"{numOs}/{Path.GetFileName(file)}", fileProgress, ct).ConfigureAwait(false);
                _cache.Add(numOs, Path.GetFileName(file));
                res.Add(new(Path.GetFileName(file), true, CalcularSha1(file)));
            }
            catch (Exception)
            {
                res.Add(new(Path.GetFileName(file), false, string.Empty));
            }
            finally
            {
                sem.Release();
                int done = Interlocked.Increment(ref completed);
                progress?.Report(new UploadProgressInfo(done * 100.0 / total,
                                                     done, total,
                                                     Path.GetFileName(file)));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // ZIP
        string zip;
        string zipName;
        try
        {
            zip = CriarZipTemporario(pasta, out zipName);
        }
        catch (Exception ex)
        {
            _log.Error($"Falha ao criar ZIP '{pasta}': {ex.Message}");
            progress?.Report(new UploadProgressInfo(100, total, total, null));
            return res;
        }
        if (!_cache.Contains(numOs, zipName))
        {
            try
            {
                var zipProgress = new Progress<double>(p =>
                {
                    double global = ((completed + p / 100.0) * 100.0) / total;
                    progress?.Report(new UploadProgressInfo(global, completed, total, zipName));
                });
                await _uploader.UploadFileAsync(zip, $"{numOs}/{zipName}", zipProgress, ct).ConfigureAwait(false);
                _cache.Add(numOs, zipName);
                res.Add(new(zipName, true, CalcularSha1(zip)));
            }
            catch (Exception)
            {
                res.Add(new(zipName, false, string.Empty));
            }
            finally
            {
                int done = Interlocked.Increment(ref completed);
                progress?.Report(new UploadProgressInfo(done * 100.0 / total,
                                                     done, total, zipName));
            }
        }
        try { File.Delete(zip); } catch (Exception ex) { _log.Warning($"Falha ao remover arquivo temporário '{zip}': {ex.Message}"); }

        progress?.Report(new UploadProgressInfo(100, total, total, null));

        _cache.Save();
        return res;
    }
    #endregion

    #region Listagem & helpers
    private static IEnumerable<string> EnumerarPastasParaBackup()
    {
        foreach (var baseDir in RenamerService.EnumerarPastasBase())
        {
            foreach (var dir in SafeEnumerateDirectories(baseDir))
            {
                if (HasFiles(dir))
                    yield return dir;
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                    stack.Push(sub);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LoggerService.Instance.Warning($"Falha ao enumerar '{current}': {ex.Message}");
            }
        }
    }

    private static bool HasFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir).Any(); }
        catch (Exception ex)
        {
            LoggerService.Instance.Warning($"Falha ao ler '{dir}': {ex.Message}");
            return false;
        }
    }

    private async Task<HashSet<string>> ArquivosRemotosAsync(
        string driveId, string folderId, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var page = await _graph.Drives[driveId]
                                   .Items[folderId].Children
                                   .GetAsync(cancellationToken: ct)
                                   .ConfigureAwait(false);
            foreach (var it in page?.Value ?? Enumerable.Empty<DriveItem>())
                if (it.File != null) set.Add(it.Name);
            return set;
        }
        catch (Exception ex)
        {
            _log.Error($"Falha ao listar arquivos remotos: {ex.Message}");
            throw;
        }
    }

    private async Task<string> ObterDriveIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_driveId)) return _driveId;
        try
        {
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
        catch (Exception ex)
        {
            _log.Error($"Falha ao obter DriveId: {ex.Message}");
            throw;
        }
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
        catch (Exception ex)
        {
            _log.Error($"Falha ao garantir pasta '{nome}': {ex.Message}");
            throw;
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
