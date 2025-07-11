using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;
using OrganizadorArquivosWPF;
using OrganizadorArquivosWPF.Utils;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Serviço para exibir ícone na bandeja do sistema e manter
    /// atualização automática em segundo plano.
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ManutencoesService _manutencoes;
        private readonly InstalacaoService _instalacao;
        private readonly BackupService _backup;
        // Intervalo padrão para atualizações automáticas
        // A primeira sincronização já ocorre na tela splash. Por isso, a
        // próxima tentativa de download só deve acontecer após 10 minutos.
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);
        private IProgress<double> _progress;
        private LoggerService _log => LoggerService.Instance;

        public TrayService()
        {
            _manutencoes = new ManutencoesService();
            _instalacao = new InstalacaoService();
            _backup = new BackupService();

            var icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico-app.ico");
            _icon = new NotifyIcon
            {
                Visible = true,
                Icon = new Icon(icoPath),
                Text = "Organizador de Arquivos"
            };

            var menu = new ContextMenuStrip();

            // Botão Abrir
            var openItem = new ToolStripMenuItem("Abrir");
            openItem.Click += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    var app = System.Windows.Application.Current;
                    if (app == null)
                        return;

                    var window = app.MainWindow;
                    if (window == null)
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

                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                });
            };
            var downloadItem = new ToolStripMenuItem("Sincronizar dados agora");
            downloadItem.Click += (s, e) =>
            {
                var app = System.Windows.Application.Current;
                if (app != null)
                {
                    app.Dispatcher.InvokeAsync(async () =>
                    {
                        if (app.MainWindow is MainWindow wnd)
                        {
                            await wnd.BaixarDadosAgoraAsync();
                        }
                        else
                        {
                            try
                            {
                                var tracker = new Utils.ProgressTracker(_progress, 2);
                                await _manutencoes.ObterDadosAsync(tracker.NextSegment());
                                _manutencoes.ClearData();

                                await _instalacao.AtualizarArquivoAsync(tracker.NextSegment());

                                _progress?.Report(100);
                            }
                            catch (Exception ex) { _log.Error($"Download manual: {ex.Message}"); }
                        }
                    });
                }
            };

            var exitItem = new ToolStripMenuItem("Sair");
            exitItem.Click += (s, e) =>
            {
                var app = System.Windows.Application.Current;
                if (app?.MainWindow is MainWindow wnd)
                    wnd.AllowClose = true;
                app?.Shutdown();
            };
            menu.Items.Add(openItem);
            menu.Items.Add(downloadItem);
            menu.Items.Add(exitItem);
            _icon.ContextMenuStrip = menu;
        }

        public async Task StartAsync(IProgress<double> progress)
        {
            _progress = progress;
            try
            {
                var tracker = new Utils.ProgressTracker(progress, 2);
                await _manutencoes.ObterDadosAsync(tracker.NextSegment());
                _manutencoes.ClearData();

                await _instalacao.AtualizarArquivoAsync(tracker.NextSegment());

                if (!string.IsNullOrWhiteSpace(Config.BackupFolder) &&
                    Directory.Exists(Config.BackupFolder))
                {
                    try
                    {
                        await _backup.SincronizarPastasAsync(Config.BackupFolder);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Backup inicial: {ex.Message}");
                    }
                }

                try
                {
                    await _backup.SincronizarPastasRenomeacaoAsync();
                }
                catch (Exception ex)
                {
                    _log.Error($"Backup inicial renomeação: {ex.Message}");
                }

                progress?.Report(100);
            }
            catch { }

            _manutencoes.StartAutoUpdate(_interval, null);
            _instalacao.StartAutoUpdate(_interval);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _manutencoes.StopAutoUpdate();
            _instalacao.StopAutoUpdate();
        }
    }
}
