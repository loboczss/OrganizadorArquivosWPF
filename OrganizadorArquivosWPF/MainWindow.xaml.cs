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
using Squirrel;

namespace OrganizadorArquivosWPF
{
    public partial class MainWindow : Window
    {
        // Serviços principais
        private readonly ExcelService _excel;
        private readonly LoggerService _log;
        private readonly RenamerService _renamer;
        private readonly AtualizadorService _update;

        // Coleção de logs vinculada ao DataGrid
        private readonly ObservableCollection<LogEntry> _logs;

        // Flags de modo desenvolvedor
        private bool _devMode;
        private string _devPath = string.Empty;
        private string _pastaOrigem = string.Empty;

        /// <summary>
        /// Construtor: inicializa componentes, exibe versão, config de logs e serviços, e dispara update automático.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // 1) Exibe a versão atual no título e no rodapé
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            this.Title += $" (v{ver})";
            LblVersao.Text = $"v{ver}";

            // 2) Configura a lista de logs e auto-scroll
            _logs = new ObservableCollection<LogEntry>();
            GridLog.ItemsSource = _logs;
            _logs.CollectionChanged += (_, __) =>
            {
                int count = GridLog.Items.Count;
                if (count > 0)
                    GridLog.ScrollIntoView(GridLog.Items[count - 1]);
            };

            // 3) Inicializa serviços
            _log = new LoggerService(_logs, Dispatcher);
            _excel = new ExcelService();
            _renamer = new RenamerService(_log);
            _update = new AtualizadorService();

            // 4) Ao carregar a janela, dispara update silencioso
            Loaded += async (_, __) => await RunUpdateAsync(manual: false);
        }

        #region Auto‑Update com Squirrel

        /// <summary>
        /// Exibe o UpdateWindow e executa a checagem/aplicação de update.
        /// </summary>
        /// <param name="manual">Se true, mostra mensagens mesmo sem update.</param>
        private async Task RunUpdateAsync(bool manual)
        {
            // Cria e exibe splash de atualização
            var splash = new UpdateWindow { Owner = this };
            splash.SetStatus(manual ? "Procurando atualização..." : "Verificando nova versão...");
            splash.Show();

            // Progress<string> repassa cada mensagem ao splash
            var progress = new Progress<string>(splash.SetStatus);

            // Chama o serviço de atualização (Squirrel)
            var result = await _update.CheckForUpdateAsync(manual, progress);

            // Se não reiniciou (AlreadyLatest, NoInternet, Error), espera 2s para leitura
            if (result == UpdateOutcome.AlreadyLatest ||
                result == UpdateOutcome.NoInternet ||
                result == UpdateOutcome.Error)
            {
                await Task.Delay(2000);
            }

            // Fecha o splash
            splash.Close();
        }

        /// <summary>
        /// Handler do botão "Procurar Atualização" na UI.
        /// </summary>
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateAsync(manual: true);
        }

        #endregion

        #region Exportar e Selecionar Pasta

        /// <summary>
        /// Exporta o log para um arquivo TXT na área de trabalho.
        /// </summary>
        private void BtnExportar_Click(object sender, RoutedEventArgs e)
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
                MessageBox.Show($"Falha ao exportar log:\n{ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Abre diálogo para selecionar pasta de origem dos arquivos.
        /// </summary>
        private void BtnSelecionar_Click(object sender, RoutedEventArgs e)
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

        #endregion

        #region Processar, Desfazer e Modo Dev

        /// <summary>
        /// Valida campos e executa o renomeio via RenamerService.
        /// </summary>
        private async void BtnProcessar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validações básicas
                var pasta = _pastaOrigem;
                var os = TxtOS.Text.Trim();
                var uf = (CmbUF.SelectedItem as ComboBoxItem)?.Content.ToString();
                var sistema = (CmbSistema.SelectedItem as ComboBoxItem)?.Content.ToString();

                if (string.IsNullOrWhiteSpace(pasta) ||
                    string.IsNullOrWhiteSpace(os) ||
                    string.IsNullOrWhiteSpace(uf) ||
                    string.IsNullOrWhiteSpace(sistema))
                {
                    MessageBox.Show("Preencha todos os campos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Verifica planilha de clientes
                var registro = _excel.GetRecord(os, uf);
                if (registro == null)
                {
                    MessageBox.Show("Nº OS não corresponde ao estado selecionado!", "Planilha Clientes",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Confirmação de cliente
                if (MessageBox.Show($"Cliente:\n\n{registro.NomeCliente}\n\nContinuar?",
                                    "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes)
                    return;

                // Valida arquivos de controlador
                var arquivos = Directory.GetFiles(pasta).Select(Path.GetFileName).ToList();
                bool TemCtrl(int qtd) =>
                    arquivos.Count(f => f.StartsWith("con", StringComparison.OrdinalIgnoreCase) ||
                                        f.StartsWith("c0n", StringComparison.OrdinalIgnoreCase)) >= qtd;

                bool is160 = false;
                if (sistema.Equals("Intelbras", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TemCtrl(1))
                    {
                        MessageBox.Show("Intelbras requer ao menos 1 arquivo de controlador (con*).",
                                        "Arquivos ausentes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    // Hoppecker: pergunta sobre Sistema 160
                    is160 = MessageBox.Show("Sistema 160 (2 controladores)?", "Hoppecker",
                                            MessageBoxButton.YesNo, MessageBoxImage.Question)
                             == MessageBoxResult.Yes;

                    if (is160 && !TemCtrl(2))
                    {
                        MessageBox.Show("Sistema 160 requer 2 arquivos de controlador.",
                                        "Arquivos ausentes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    if (!is160 && !TemCtrl(1))
                    {
                        MessageBox.Show("Hoppecker requer 1 arquivo de controlador.",
                                        "Arquivos ausentes", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Executa renomeio
                Progress.Visibility = Visibility.Visible;
                BtnProcessar.IsEnabled = false;

                await _renamer.RenameAsync(pasta, registro, sistema,
                                           is160, _devMode, _devPath);

                BtnDesfazer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message);
            }
            finally
            {
                Progress.Visibility = Visibility.Collapsed;
                BtnProcessar.IsEnabled = true;
            }
        }

        /// <summary>
        /// Desfaz a última renomeação.
        /// </summary>
        private void BtnDesfazer_Click(object sender, RoutedEventArgs e)
        {
            _renamer.Undo();
            BtnDesfazer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Alterna modo desenvolvedor (destino de teste).
        /// </summary>
        private void BtnDev_Click(object sender, RoutedEventArgs e)
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

        #endregion
    }
}
