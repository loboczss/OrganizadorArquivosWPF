using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Threading.Tasks;
using OrganizadorArquivosWPF;

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
        // Intervalo padrão para atualizações automáticas
        // A primeira sincronização já ocorre na tela splash. Por isso, a
        // próxima tentativa de download só deve acontecer após 10 minutos.
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);
        private IProgress<int> _progress;
        private LoggerService _log => LoggerService.Instance;

        public TrayService()
        {
            _manutencoes = new ManutencoesService();

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
                                await _manutencoes.ObterDadosAsync(_progress);
                                _manutencoes.ClearData();
                            }
                            catch (Exception ex) { _log.Error($"Download manual: {ex.Message}"); }
                        }
                    });
                }
            };

            var exitItem = new ToolStripMenuItem("Sair");
            exitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            menu.Items.Add(openItem);
            menu.Items.Add(downloadItem);
            menu.Items.Add(exitItem);
            _icon.ContextMenuStrip = menu;
        }

        public async Task StartAsync(IProgress<int> progress)
        {

            _progress = progress;
            try
            {
                await _manutencoes.ObterDadosAsync(progress);
                _manutencoes.ClearData();
            }
            catch { }
            _manutencoes.StartAutoUpdate(_interval, progress);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _manutencoes.StopAutoUpdate();
        }
    }
}
