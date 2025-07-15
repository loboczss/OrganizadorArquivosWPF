using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrganizadorArquivosWPF.Services;


namespace SyncWorker;

public class Worker
{
    private readonly ILogger<Worker> _logger;
    private readonly ManutencoesService _manutencoes;
    private readonly InstalacaoService _instalacao;
    private readonly BackupService _backup;
    private readonly LoggerService _log;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _log = new LoggerService();
        _manutencoes = new ManutencoesService();
        _instalacao = new InstalacaoService();
        _backup = new BackupService();
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Synchronizing at: {time}", DateTimeOffset.Now);

            try { await _manutencoes.ObterDadosAsync(); } catch (Exception ex) { _log.Error($"Manutencoes: {ex.Message}"); }
            try { await _instalacao.AtualizarArquivoAsync(); } catch (Exception ex) { _log.Error($"Instalacao: {ex.Message}"); }

            if (!string.IsNullOrWhiteSpace(Config.BackupFolder) && Directory.Exists(Config.BackupFolder))
            {
                try { await _backup.EnviarBackupAsync(Config.BackupFolder, null, null); } catch (Exception ex) { _log.Error($"Backup: {ex.Message}"); }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
