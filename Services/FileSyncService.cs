// File: Services/FileSyncService.cs
// Monitora AC/MT/Documentos em tempo real e enfileira novos/alterados.
// Requer .NET 8.0 e o BackupService já implementado.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizadorArquivosWPF.Services;

public sealed class FileSyncService : IAsyncDisposable
{
    private readonly BackupService _backup;
    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<string> _queue = new();
    private readonly Task _worker;
    private readonly FileSystemWatcher[] _watchers;

    public FileSyncService(BackupService backup)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));

        // 1) Cria watchers para cada base
        _watchers = RenamerService.EnumerarPastasBase()
                                  .Select(CreateWatcher)
                                  .ToArray();

        // 2) Worker que processa a fila até o Cancel
        _worker = Task.Run(ProcessQueueAsync);
    }

    // --- Começa a vigiar uma raiz recursivamente
    private FileSystemWatcher CreateWatcher(string path)
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

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Considera apenas arquivos concretos
        if (File.Exists(e.FullPath))
            _queue.Add(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (File.Exists(e.FullPath))
            _queue.Add(e.FullPath);
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

            var pasta = Directory.GetParent(file)?.FullName;
            if (pasta == null) continue;

            buffer[pasta] = 0;

            // 2) Aguarda um “silêncio” de 3 s antes de disparar
            await Task.Delay(3_000, _cts.Token);

            foreach (var dir in buffer.Keys.ToArray())
            {
                try
                {
                    await _backup.EnviarBackupAsync(dir, null, _cts.Token);
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Warning($"Sync falhou em '{dir}': {ex.Message}");
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

        try { await _worker; } catch { /* ignore */ }
        _cts.Dispose();
    }
}
