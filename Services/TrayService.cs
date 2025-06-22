using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
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
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
        private IProgress<int> _progress;

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
                    var window = System.Windows.Application.Current.MainWindow;
                    if (window != null)
                    {
                        window.Show();
                        if (window.WindowState == WindowState.Minimized)
                            window.WindowState = WindowState.Normal;
                        window.Activate();
                    }
                });
            };
            var downloadItem = new ToolStripMenuItem("Baixar dados agora");
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
                            catch (Exception ex) { Console.WriteLine($"[ERRO] Download manual: {ex.Message}"); }
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

        public async void Start(IProgress<int> progress)
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
