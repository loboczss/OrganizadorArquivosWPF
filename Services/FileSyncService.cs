// File: Services/FileSyncService.cs
// Monitora AC/MT/Documentos em tempo real e enfileira novos/alterados.
// Requer .NET 8.0 e o BackupService já implementado.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizadorArquivosWPF.Services;

public sealed class FileSyncService : IAsyncDisposable
{
    private readonly BackupService _backup;
    private readonly LoggerService _log = LoggerService.Instance;
    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<string> _queue = new();
    private readonly Task _worker;
    private readonly FileSystemWatcher[] _watchers;
    private bool HasInternet => NetworkInterface.GetIsNetworkAvailable();

    public FileSyncService(BackupService backup)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));

        // 1) Cria watchers para cada base
        _watchers = RenamerService.EnumerarPastasBase()
                                  .Select(TryCreateWatcher)
                                  .Where(w => w != null)
                                  .ToArray()!;

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        // 2) Worker que processa a fila até o Cancel
        _worker = Task.Run(ProcessQueueAsync);
    }

    // --- Começa a vigiar uma raiz recursivamente
    private FileSystemWatcher? TryCreateWatcher(string path)
    {
        try
        {
            var fsw = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
            };

            fsw.Created += OnChanged;
            fsw.Changed += OnChanged;
            fsw.Renamed += OnRenamed;
            fsw.EnableRaisingEvents = true;
            return fsw;
        }
        catch (Exception ex)
        {
            _log.Warning($"Watcher falhou em '{path}': {ex.Message}");
            return null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (File.Exists(e.FullPath))
        {
            try { _queue.Add(e.FullPath); }
            catch (InvalidOperationException ex)
            {
                _log.Warning($"Fila encerrada ao adicionar '{e.FullPath}': {ex.Message}");
            }
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (File.Exists(e.FullPath))
        {
            try { _queue.Add(e.FullPath); }
            catch (InvalidOperationException ex)
            {
                _log.Warning($"Fila encerrada ao adicionar '{e.FullPath}': {ex.Message}");
            }
        }
    }

    private async Task WaitForInternetAsync(CancellationToken ct)
    {
        while (!HasInternet && !ct.IsCancellationRequested)
            await Task.Delay(5_000, ct);
    }

    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            try { await _backup.SincronizarTudoAsync(_cts.Token); }
            catch (Exception ex) { _log.Warning($"Sync on network: {ex.Message}"); }
        }
    }

    // --- Loop infinito que agrupa por pasta e chama BackupService
    private async Task ProcessQueueAsync()
    {
        var buffer = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        while (!_cts.IsCancellationRequested)
        {
            // 1) Bloqueia até chegar algo
            string file;
            try { file = _queue.Take(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (InvalidOperationException ex)
            {
                _log.Warning($"Fila encerrada: {ex.Message}");
                break;
            }

            var pasta = Directory.GetParent(file)?.FullName;
            if (pasta == null) continue;

            buffer[pasta] = 0;

            // 2) Aguarda um “silêncio” de 3 s antes de disparar
            await Task.Delay(3_000, _cts.Token);

            foreach (var dir in buffer.Keys.ToArray())
            {
                await WaitForInternetAsync(_cts.Token);
                try
                {
                    await _backup.EnviarBackupAsync(dir, null, null, _cts.Token);
                }
                catch (Exception ex)
                {
                    _log.Error($"Sync falhou em '{dir}': {ex.Message}");
                }
                buffer.TryRemove(dir, out _);
            }
        }
    }

    // --- Parada elegante
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var w in _watchers) w.Dispose();
        _queue.CompleteAdding();

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

        try { await _worker; }
        catch (Exception ex)
        {
            _log.Warning($"Worker finalizado com erro: {ex.Message}");
        }
        _cts.Dispose();
    }
}
