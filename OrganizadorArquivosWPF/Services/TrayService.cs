using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

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

            // Botão Sair
            var exitItem = new ToolStripMenuItem("Sair");
            exitItem.Click += (s, e) =>
            {
                System.Windows.Application.Current.Shutdown();
            };

            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);
            _icon.ContextMenuStrip = menu;
        }

        public async void Start(IProgress<int> progress)
        {
            try
            {
                await _manutencoes.ObterDadosAsync(progress);
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
