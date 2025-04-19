using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Services;

namespace OrganizadorArquivosWPF
{
    public partial class MainWindow : Window
    {
        private readonly ExcelService _excel;
        private readonly LoggerService _log;
        private readonly RenamerService _renamer;
        private readonly AtualizadorService _update;
        private readonly ObservableCollection<LogEntry> _logs;

        private bool _devMode;
        private string _devPath = "";
        private string _pastaOrigem = "";

        public MainWindow()
        {
            InitializeComponent();

            /* versão */
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            this.Title += $" (v{ver})";
            LblVersao.Text = $"v{ver}";

            /* logs */
            _logs = new ObservableCollection<LogEntry>();
            GridLog.ItemsSource = _logs;
            _logs.CollectionChanged += (_, __) =>
            {
                int count = GridLog.Items.Count;
                if (count > 0)
                    GridLog.ScrollIntoView(GridLog.Items[count - 1]);
            };

            /* serviços */
            _log = new LoggerService(_logs, Dispatcher);
            _excel = new ExcelService();
            _renamer = new RenamerService(_log);
            _update = new AtualizadorService();

            Loaded += async (_, __) => await RunUpdateAsync(false);
        }

        /*──────────────── update (auto / manual) ────────────────*/
        private async Task RunUpdateAsync(bool manual)
        {
            var splash = new UpdateWindow { Owner = this };
            splash.SetStatus(manual ? "Procurando atualização…" : "Verificando nova versão…");
            splash.Show();

            var progress = new Progress<string>(splash.SetStatus);
            var result = await _update.CheckForUpdateAsync(manual, progress);

            /* se não reiniciou, manter mensagem por 2 s */
            if (result == UpdateOutcome.AlreadyLatest ||
                result == UpdateOutcome.NoInternet ||
                result == UpdateOutcome.Error)
            {
                await Task.Delay(2000);
            }
            splash.Close();
        }

        private async void BtnCheckUpdate_Click(object s, RoutedEventArgs e) =>
            await RunUpdateAsync(true);

        /*──────── exportar log ────────*/
        private void BtnExportar_Click(object s, RoutedEventArgs e)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string file = Path.Combine(desktop, $"Log_{DateTime.Now:yyyyMMdd_HHmm}.txt");
                File.WriteAllLines(file,
                    _logs.Select(l => $"{l.Hora:HH:mm:ss}\t{l.Tipo}\t{l.Mensagem}"));
                MessageBox.Show($"Log salvo em:\n{file}", "Exportar Log",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao exportar log:\n{ex.Message}",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /*──────── selecionar pasta ────────*/
        private void BtnSelecionar_Click(object s, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Selecione a pasta que contém os arquivos do sistema" };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _pastaOrigem = dlg.SelectedPath;
                TxtPasta.Text = Path.GetFileName(_pastaOrigem);
                _log.Info("Pasta selecionada: " + _pastaOrigem);
            }
        }

        /*──────── processar ────────*/
        private async void BtnProcessar_Click(object s, RoutedEventArgs e)
        {
            try
            {
                /* validações */
                var pasta = _pastaOrigem;
                var os = TxtOS.Text.Trim();
                var uf = (CmbUF.SelectedItem as ComboBoxItem)?.Content.ToString();
                var sistema = (CmbSistema.SelectedItem as ComboBoxItem)?.Content.ToString();

                if (string.IsNullOrWhiteSpace(pasta) ||
                    string.IsNullOrWhiteSpace(os) ||
                    string.IsNullOrWhiteSpace(uf) ||
                    string.IsNullOrWhiteSpace(sistema))
                {
                    MessageBox.Show("Preencha todos os campos.",
                                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                /* planilha */
                var registro = _excel.GetRecord(os, uf);
                if (registro == null)
                {
                    MessageBox.Show("Nº OS não corresponde ao estado selecionado!",
                                    "Planilha Clientes", MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                    return;
                }

                if (MessageBox.Show($"Cliente:\n\n{registro.NomeCliente}\n\nContinuar?",
                                    "Confirmação",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                /* arquivos */
                var arquivos = Directory.GetFiles(pasta)
                                        .Select(Path.GetFileName)
                                        .ToList();

                bool TemCtrl(int qtd) =>
                    arquivos.Count(f => f.StartsWith("con", StringComparison.OrdinalIgnoreCase) ||
                                        f.StartsWith("c0n", StringComparison.OrdinalIgnoreCase)) >= qtd;

                bool is160 = false;

                if (sistema.Equals("Intelbras", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TemCtrl(1))
                    {
                        MessageBox.Show("Intelbras requer ao menos 1 arquivo de controlador (con*).",
                                        "Arquivos ausentes", MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                        return;
                    }
                }
                else /* Hoppecker */
                {
                    is160 = MessageBox.Show("Sistema 160 (2 controladores)?",
                                            "Hoppecker",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Question) == MessageBoxResult.Yes;

                    if (is160 && !TemCtrl(2))
                    {
                        MessageBox.Show("Sistema 160 requer 2 arquivos de controlador.",
                                        "Arquivos ausentes", MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                        return;
                    }
                    if (!is160 && !TemCtrl(1))
                    {
                        MessageBox.Show("Hoppecker requer 1 arquivo de controlador.",
                                        "Arquivos ausentes", MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                        return;
                    }
                }

                /* renomeia */
                Progress.Visibility = Visibility.Visible;
                BtnProcessar.IsEnabled = false;

                await _renamer.RenameAsync(pasta, registro, sistema,
                                           is160, _devMode, _devPath);

                BtnDesfazer.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { _log.Error(ex.Message); }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
                BtnProcessar.IsEnabled = true;
            }
        }

        /*──────── desfazer ────────*/
        private void BtnDesfazer_Click(object s, RoutedEventArgs e)
        {
            _renamer.Undo();
            BtnDesfazer.Visibility = Visibility.Collapsed;
        }

        /*──────── botão Dev ────────*/
        private void BtnDev_Click(object s, RoutedEventArgs e)
        {
            var senha = Interaction.InputBox("Senha do modo Dev:", "Modo Desenvolvedor", "");
            if (senha != "dev123")
            {
                MessageBox.Show("Senha incorreta!");
                return;
            }

            _devMode = !_devMode;
            if (_devMode)
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog
                { Description = "Selecione a pasta de destino de teste" };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    _devPath = dlg.SelectedPath;
                _log.Info("Modo Dev ON: " + _devPath);
            }
            else
            {
                _log.Info("Modo Dev OFF");
            }
        }
    }
}
