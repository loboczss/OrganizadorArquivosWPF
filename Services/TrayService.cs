// File: Services/TrayService.cs
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using OrganizadorArquivosWPF.Utils;

namespace OrganizadorArquivosWPF.Services;

/// <summary>
/// Ícone de bandeja + sincronizações em segundo plano
/// (manutenções, instalações e backups “tipo OneDrive”).
/// </summary>
public sealed class TrayService : IDisposable, IAsyncDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ManutencoesService _manutencoes = new();
    private readonly InstalacaoService _instalacao = new();
    private readonly BackupService _backup = new();

    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);
    private IProgress<double>? _progress;
    private readonly LoggerService _log = LoggerService.Instance;

    private CancellationTokenSource? _ctsUpdates;
    private Task? _backupLoop;

    public TrayService()
    {
        // ─── Ícone + menu ───────────────────────────────────────────────────────
        var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico-app.ico");
        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application,
            Text = "Organizador de Arquivos"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(MenuAbrirJanela());
        menu.Items.Add(MenuSincronizarAgora());
        menu.Items.Add(MenuSair());
        _icon.ContextMenuStrip = menu;
    }

    #region Público
    /// <summary>Sincronização inicial e agenda loops de atualização.</summary>
    public async Task StartAsync(IProgress<double>? progress)
    {
        _progress = progress;
        var tracker = new ProgressTracker(progress, 2);

        try
        {
            // ① Dados
            await _manutencoes.ObterDadosAsync(tracker.NextSegment());
            _manutencoes.ClearData();
            await _instalacao.AtualizarArquivoAsync(tracker.NextSegment());

            // ② Backup
            await SincronizarBackupInicialAsync();

            progress?.Report(100);
        }
        catch (Exception ex) { _log.Error($"StartAsync: {ex.Message}"); }

        // ─── loops contínuos ────────────────────────────────────────────────────
        _ctsUpdates = new CancellationTokenSource();
        _manutencoes.StartAutoUpdate(_interval);      // ← só intervalo
        _instalacao.StartAutoUpdate(_interval);      // idem
        _backupLoop = LoopBackupAsync(_ctsUpdates.Token);
    }
    #endregion

    #region Menus
    private ToolStripMenuItem MenuAbrirJanela()
    {
        var item = new ToolStripMenuItem("Abrir");
        item.Click += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;

                if (app.MainWindow == null)
                {
                    var login = new Views.LoginWindow();
                    if (login.ShowDialog() == true)
                    {
                        var main = new MainWindow(login.Usuario);
                        app.MainWindow = main;
                        main.Show();
                    }
                    return;
                }

                var win = app.MainWindow;
                win.Show();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
                win.Activate();
            });
        };
        return item;
    }

    private ToolStripMenuItem MenuSincronizarAgora()
    {
        var item = new ToolStripMenuItem("Sincronizar dados agora");
        item.Click += async (_, _) =>
        {
            try
            {
                if (System.Windows.Application.Current?.MainWindow is MainWindow wnd)
                {
                    await wnd.BaixarDadosAgoraAsync();
                }
                else
                {
                    var tracker = new ProgressTracker(_progress, 2);
                    await _manutencoes.ObterDadosAsync(tracker.NextSegment());
                    _manutencoes.ClearData();
                    await _instalacao.AtualizarArquivoAsync(tracker.NextSegment());

                    await _backup.SincronizarTudoAsync();
                    _progress?.Report(100);
                }
            }
            catch (Exception ex) { _log.Error($"Download manual: {ex.Message}"); }
        };
        return item;
    }

    private ToolStripMenuItem MenuSair()
    {
        var item = new ToolStripMenuItem("Sair");
        item.Click += (_, _) =>
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow win)
                win.AllowClose = true;
            System.Windows.Application.Current?.Shutdown();
            Environment.Exit(0);
        };
        return item;
    }
    #endregion

    #region Backups
    private async Task SincronizarBackupInicialAsync()
    {
        if (!string.IsNullOrWhiteSpace(Config.BackupFolder) &&
            Directory.Exists(Config.BackupFolder))
        {
            try { await _backup.SincronizarTudoAsync(); }
            catch (Exception ex) { _log.Error($"Backup inicial (fixa): {ex.Message}"); }
        }

        try { await _backup.SincronizarTudoAsync(); }
        catch (Exception ex) { _log.Error($"Backup inicial renomeação: {ex.Message}"); }
    }

    private async Task LoopBackupAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, token);
                await _backup.SincronizarTudoAsync(null, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.Warning($"Backup loop: {ex.Message}"); }
        }
    }
    #endregion

    #region IDisposable
    public void Dispose() => DisposeAsync().AsTask().Wait();

    public async ValueTask DisposeAsync()
    {
        _icon.Visible = false;
        _icon.Dispose();

        if (_ctsUpdates != null)
        {
            _ctsUpdates.Cancel();
            try { if (_backupLoop != null) await _backupLoop; } catch { }
            _ctsUpdates.Dispose();
        }

        _manutencoes.StopAutoUpdate();
        _instalacao.StopAutoUpdate();
    }
    #endregion
}
