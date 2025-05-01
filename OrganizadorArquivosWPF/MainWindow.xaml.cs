using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;   // EnableWindow
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Views;
using Ookii.Dialogs.Wpf;

namespace OrganizadorArquivosWPF
{
    public partial class MainWindow : Window
    {
        // Win32: bloqueia/solta completamente a janela
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

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

            // título + versão
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            Title = $"One Engenharia LTDA – Organizador de Arquivos (v{ver})";
            LblVersao.Text = $"v{ver}";

            // logs + auto-scroll
            _logs = new ObservableCollection<LogEntry>();
            GridLog.ItemsSource = _logs;
            _logs.CollectionChanged += (_, __) =>
            {
                if (GridLog.Items.Count > 0)
                    GridLog.ScrollIntoView(GridLog.Items[GridLog.Items.Count - 1]);
            };

            // serviços
            _log = new LoggerService(_logs, Dispatcher);
            _excel = new ExcelService();
            _renamer = new RenamerService(_log);
            _update = new AtualizadorService();

            RegisterForStartup();

            // update silencioso no startup
            if (Environment.GetCommandLineArgs().Contains("-startup"))
            {
                Loaded += async (_, __) =>
                {
                    await RunUpdateAsync();  // sem argumento
                    Application.Current.Shutdown();
                };
            }
        }

        private void RegisterForStartup()
        {
            try
            {
                using (var rk = Registry.CurrentUser.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var updaterPath = Path.Combine(baseDir, "win-x64", "OneEngUpdater.exe");
                    rk.SetValue("CompillerLogUpdater", $"\"{updaterPath}\" -startup");
                }
            }
            catch { /* silencioso */ }
        }

        private async Task RunUpdateAsync()
        {
            // 1) bloqueia completamente a janela
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            EnableWindow(hwnd, false);
            IsEnabled = false;

            try
            {
                // 2) localiza e dispara o updater externo
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var updaterPath = Path.Combine(baseDir, "win-x64", "OneEngUpdater.exe");

                if (!File.Exists(updaterPath))
                {
                    MessageBox.Show(
                        $"Atualizador não encontrado:\n{updaterPath}",
                        "Erro",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo(updaterPath)
                {
                    UseShellExecute = true
                });

                // 3) espera breve para feedback
                await Task.Delay(500);
            }
            finally
            {
                // 4) reabilita a janela principal
                EnableWindow(hwnd, true);
                IsEnabled = true;
            }
        }

        // 2) Chamadas atualizadas

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateAsync();  // sem argumento
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var desk = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                var file = Path.Combine(desk,
                    $"Log_{DateTime.Now:yyyyMMdd_HHmm}.txt");
                File.WriteAllLines(file,
                    _logs.Select(l => $"{l.Hora:HH:mm:ss}\t{l.Tipo}\t{l.Mensagem}"));
                MessageBox.Show($"Log salvo em:\n{file}",
                                "Exportar Log",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao exportar: {ex.Message}",
                                "Erro",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void BtnSelecionar_Click(object sender, RoutedEventArgs e)
        {
            // 1) Carrega o último caminho ou Documentos
            var last = Properties.Settings.Default.LastFolder;
            if (string.IsNullOrWhiteSpace(last) || !Directory.Exists(last))
                last = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // 2) Prepara o diálogo Vista
            var dlg = new VistaFolderBrowserDialog
            {
                Description = "Selecione a pasta de origem",
                UseDescriptionForTitle = true,
                SelectedPath = last,
                ShowNewFolderButton = true
            };

            // 3) Exibe e trata o resultado
            if (dlg.ShowDialog(this) == true)
            {
                _pastaOrigem = dlg.SelectedPath;
                TxtPasta.Text = Path.GetFileName(_pastaOrigem);
                _log.Info($"Pasta selecionada: {_pastaOrigem}");

                // 4) Salva para próxima vez
                Properties.Settings.Default.LastFolder = _pastaOrigem;
                Properties.Settings.Default.Save();
            }
        }

        private async void BtnProcessar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pastaOrigem) ||
                string.IsNullOrWhiteSpace(TxtOS.Text))
            {
                MessageBox.Show("Informe pasta e Nº OS.",
                                "Aviso",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            BtnProcessar.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            _log.Info("Buscando na planilha…");

            // monta fullOS = UF + número puro
            var osNum = TxtOS.Text.Trim();
            var ufItem = CmbUF.SelectedItem as ComboBoxItem;
            var uf = ufItem != null
                        ? ufItem.Content.ToString()
                        : osNum.Substring(0, 2).ToUpper();
            var fullOS = uf + osNum;

            ClientRecord record = null;
            try
            {
                record = await Task.Run(() => _excel.GetRecord(fullOS, uf));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao ler planilha: {ex.Message}",
                                "Erro",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }

            if (record == null)
            {
                _log.Warning("OS não encontrada, abrindo dados manuais…");
                var rotas = await Task.Run(() => _excel.GetRouteList());
                var fb = new FallbackWindow(fullOS, rotas, uf)
                { Owner = this };

                if (fb.ShowDialog() != true)
                {
                    BtnProcessar.IsEnabled = true;
                    Progress.Visibility = Visibility.Collapsed;
                    return;
                }

                // preenche record em modo manual
                record = new ClientRecord
                {
                    NumOS = fullOS,
                    UF = uf,
                    Rota = fb.Rota,
                    IdSigfi = fb.IdSigfi,
                    Empresa = "HOPPECKE",
                    TipoDesigfi = fb.Is160 ? "SIGFI160" : "",
                    NomeCliente = ""    // vazio sinaliza modo manual
                };
            }

            // confirmação
            if (MessageBox.Show($"Cliente:\n\n{record.NomeCliente}\n\nContinuar?",
                                "Confirmação",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                BtnProcessar.IsEnabled = true;
                Progress.Visibility = Visibility.Collapsed;
                return;
            }

            // executa organização + renomeio
            try
            {
                await _renamer.RenameAsync(
                    _pastaOrigem,
                    record,
                    record.Empresa,
                    record.TipoDesigfi == "SIGFI160",
                    _devMode,
                    _devPath
                );
                BtnDesfazer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _log.Error($"Erro no renomeio: {ex.Message}");
            }
            finally
            {
                BtnProcessar.IsEnabled = true;
                Progress.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnDesfazer_Click(object sender, RoutedEventArgs e)
        {
            // agora abre a pasta onde os arquivos foram salvos
            if (Directory.Exists(_renamer.LastDestination))
                Process.Start("explorer.exe", _renamer.LastDestination);
        }
    }
}
