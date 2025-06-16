using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Linq;
using Ookii.Dialogs.Wpf;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Views;

namespace OrganizadorArquivosWPF
{
    /// <summary>
    /// Janela principal do Organizador de Arquivos.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Usuário autenticado
        private readonly UsuarioRecord _usuario;

        // Nome da pasta padrão na área de trabalho
        private const string PastaDefaultNome = "SALVAR AQUI";

        #region Progresso UI
        private sealed class BarProgress : IProgress<int>
        {
            private readonly MainWindow _wnd;
            public BarProgress(MainWindow wnd) => _wnd = wnd;
            public void Report(int value)
                => _wnd.Dispatcher.Invoke(() => _wnd.Progress.Value = value);
        }

        private sealed class DownloadProgress : IProgress<int>
        {
            private readonly MainWindow _wnd;
            public DownloadProgress(MainWindow wnd) => _wnd = wnd;
            public void Report(int value)
            {
                _wnd.Dispatcher.Invoke(() =>
                {
                    if (value >= 100)
                    {
                        _wnd.DownloadBar.Value = 100;
                        _wnd.DownloadBar.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        if (_wnd.DownloadBar.Visibility != Visibility.Visible)
                            _wnd.DownloadBar.Visibility = Visibility.Visible;
                        _wnd.DownloadBar.Value = value;
                    }
                });
            }
        }
        #endregion

        #region Campos e serviços (mais “leves”)
        private ExcelService _excel;               // passado a instanciar só quando precisar
        private LoggerService _log;
        private RenamerService _renamer;
        private AtualizadorService _update;
        private ManutencoesService _manutencoes;
        private List<ClientRecord> _cachedRecords;
        private readonly ObservableCollection<LogEntry> _logs;
        private readonly DownloadProgress _downloadReporter;
        private string _pastaOrigem;
        #endregion

        /// <summary>
        /// Construtor: inicializa componentes e configurações mínimas de UI.
        /// Não faz nada pesado aqui.
        /// </summary>
        public MainWindow(UsuarioRecord usuario)
        {
            _usuario = usuario ?? throw new ArgumentNullException(nameof(usuario));
            InitializeComponent();

            // Inicializa coleção de logs e serviço de log visual
            _logs = new ObservableCollection<LogEntry>();
            _log = new LoggerService(_logs, Dispatcher);
            _downloadReporter = new DownloadProgress(this);

            // Ainda não instanciamos ExcelService, RenamerService, etc., para não travar
            // Essas instâncias serão criadas em background em 'Window_Loaded'.

            // Configura UI (labels, título e versões)
            LblUsuario.Text = $"Usuário: {_usuario.NomeUsuario}";
            var versao = Assembly.GetExecutingAssembly().GetName().Version;
            Title = $"One Engenharia LTDA – Organizador de Arquivos (v{versao})";
            LblVersao.Text = $"v{versao}";

            // Configura Grid de logs para rolar automaticamente
            GridLog.ItemsSource = _logs;
            _logs.CollectionChanged += (s, e) =>
            {
                if (GridLog.Items.Count > 0)
                    GridLog.ScrollIntoView(GridLog.Items[GridLog.Items.Count - 1]);
            };

            // Barras de progresso começam escondidas
            ConfigurarProgressBar();
            ConfigurarDownloadBar();

            // Exibe texto-padrão nas labels que serão preenchidas
            TxtSyncStatus.Text = "Verificando dados de manutenção...";
            TxtSyncStatus.Foreground = Brushes.Gray;

            TxtLastUpdate.Text = "Carregando dados de manutenção...";
            TxtLastUpdate.Foreground = Brushes.Gray;

            // Define pasta padrão (pode ser rápido o suficiente para rodar no construtor)
            DefinirPastaPadrao();

            // Assina evento Loaded para disparar o que é pesado em background
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// Método que dispara quando a janela está prestes a aparecer.
        /// Aqui entramos em background para tarefas de I/O e, depois, atualizamos a UI.
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1) Cria instâncias “pesadas” em background
            //    - Instanciar ExcelService pode carregar bibliotecas COM do Excel, etc.
            await Task.Run(() =>
            {
                _excel = new ExcelService();
                var logFileService = new LogFileService();
                _renamer = new RenamerService(_log, logFileService);
                _update = new AtualizadorService();
                _manutencoes = new ManutencoesService();
            });

            // Carrega registros em memória (pode demorar um pouco)
            _cachedRecords = await _manutencoes.ObterClientRecordsAsync(_downloadReporter);

            // 2) Atualiza status de sincronização
            await AtualizarStatusSincronizacaoAsync();

            // 3) Atualiza data da base de dados
            await AtualizarDataPlanilhaAsync();

            // 4) Baixa dados de manutenção se possível
            try
            {
                _manutencoes.UpdateCompleted += Manutencoes_UpdateCompleted;
                await _manutencoes.ObterDadosAsync(_downloadReporter);
                _manutencoes.StartAutoUpdate(TimeSpan.FromMinutes(1), _downloadReporter);
            }
            catch (Exception ex)
            {
                _log.Warning("Falha ao atualizar dados de manutenção: " + ex.Message);
            }

        }

        #region Botão Processar
        private async void BtnProcessar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pastaOrigem) || string.IsNullOrWhiteSpace(TxtOS.Text))
            {
                MessageBox.Show("Informe pasta e Nº OS.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ToggleUIBusy(true);
            var reporter = new BarProgress(this);

            // Monta chave de busca: UF + número da OS
            var osInput = TxtOS.Text.Trim();

            // Caso o usuário digite o prefixo da UF junto ao número,
            // removemos esse prefixo para evitar duplicação.
            string typedUf = null;
            if (osInput.Length >= 2 && char.IsLetter(osInput[0]) && char.IsLetter(osInput[1]))
            {
                typedUf = osInput.Substring(0, 2).ToUpperInvariant();
                osInput = osInput.Substring(2);
            }

            var ufItem = CmbUF.SelectedItem as ComboBoxItem;
            var uf = ufItem?.Content.ToString() ?? typedUf ?? (osInput.Length >= 2 ? osInput.Substring(0, 2).ToUpperInvariant() : string.Empty);
            var osNum = osInput;
            var fullOS = uf + osNum;

            _log.Info("Buscando na base de dados…");

            ClientRecord record;
            try
            {
                record = await Task.Run(() =>
                {
                    int total = _cachedRecords.Count;
                    for (int i = 0; i < total; i++)
                    {
                        reporter.Report((int)((i + 1) * 100.0 / total));
                        var r = _cachedRecords[i];
                        if (r.NumOS.Equals(fullOS, StringComparison.OrdinalIgnoreCase))
                            return r;
                    }
                    return null;
                });
            }
            catch (Exception ex)
            {
                _log.Error($"Erro ao ler base de dados: {ex.Message}");
                MessageBox.Show($"Erro ao ler base de dados: {ex.Message}",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                ToggleUIBusy(false);
                return;
            }

            if (record == null)
            {
                _log.Warning("OS não encontrada – solicitando dados manuais…");
                record = await MostrarFallbackAsync(fullOS, uf);
                if (record == null)
                {
                    ToggleUIBusy(false);
                    return;
                }
            }

            if (!ConfirmarCliente(record.NomeCliente))
            {
                ToggleUIBusy(false);
                return;
            }

            try
            {
                await _renamer.RenameAsync(
                    _pastaOrigem,
                    record,
                    record.Empresa,
                    record.TipoDesigfi,
                    false,
                    string.Empty,
                    _usuario.NomeUsuario,
                    _usuario.Matricula,
                    reporter);

                BtnAbrir.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _log.Error($"Erro no renomeio: {ex.Message}");
                MessageBox.Show($"Erro no renomeio: {ex.Message}",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ToggleUIBusy(false);
            }
        }

        private Task<ClientRecord> MostrarFallbackAsync(string fullOS, string uf)
        {
            // Chamamos ShowDialog de forma síncrona, mas todo o trabalho de GetRouteList() 
            // (pesado) pode rodar em background antes de abrir a janela.
            return Task.Run(() =>
            {
                var rotaList = _cachedRecords
                                .Select(r => r.Rota)
                                .Where(r => !string.IsNullOrEmpty(r))
                                .Distinct()
                                .OrderBy(r => r)
                                .ToList();
                ClientRecord fallbackResult = null;

                // Precisamos chamar ShowDialog na thread de UI → Dispatcher.Invoke
                Dispatcher.Invoke(() =>
                {
                    var fb = new FallbackWindow(fullOS, rotaList, uf, _cachedRecords) { Owner = this };
                    if (fb.ShowDialog() == true)
                    {
                        fallbackResult = new ClientRecord
                        {
                            NumOS = fullOS,
                            UF = uf,
                            Rota = fb.Rota,
                            IdSigfi = fb.IdSigfi,
                            Empresa = "HOPPECKE",
                            TipoDesigfi = fb.TipoSistema,
                            NomeCliente = fb.ClienteEncontrado ?? "[NOME DESCONHECIDO]"
                        };
                    }
                });

                return fallbackResult;
            });
        }

        private bool ConfirmarCliente(string nomeCliente)
        {
            var result = MessageBox.Show(
                $"Cliente:\n\n{nomeCliente}\n\nContinuar?",
                "Confirmação",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _log.Info($"Usuário confirmou o cliente: {nomeCliente}");
                return true;
            }

            _log.Info($"Usuário NÃO confirmou o cliente: {nomeCliente}");
            return false;
        }
        #endregion

        #region Helpers UI
        private void ToggleUIBusy(bool ativo)
        {
            BtnProcessar.IsEnabled = !ativo;
            Progress.Visibility = ativo ? Visibility.Visible : Visibility.Collapsed;
            if (!ativo) Progress.Value = 0;
        }

        private void DefinirPastaPadrao()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var defaultPath = Path.Combine(desktop, PastaDefaultNome);
            Directory.CreateDirectory(defaultPath);

            var ultima = Properties.Settings.Default.LastFolder;
            _pastaOrigem =
                !string.IsNullOrWhiteSpace(ultima) && Directory.Exists(ultima)
                ? ultima
                : defaultPath;

            TxtPasta.Text = Path.GetFileName(_pastaOrigem);
            _log.Info($"Pasta de origem: {_pastaOrigem}");
        }

        private void ConfigurarProgressBar()
        {
            Progress.Minimum = 0;
            Progress.Maximum = 100;
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Collapsed;
        }

        private void ConfigurarDownloadBar()
        {
            DownloadBar.Minimum = 0;
            DownloadBar.Maximum = 100;
            DownloadBar.IsIndeterminate = false;
            DownloadBar.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Atualiza o status de sincronização (I/O leve: File.Exists ou rede).
        /// </summary>
        private async Task AtualizarStatusSincronizacaoAsync()
        {
            bool atualizado = false;
            DateTime? cacheTime = null;
            try
            {
                cacheTime = await Task.Run(() => ManutencoesService.GetCacheTimestamp());
                atualizado = cacheTime.HasValue && (DateTime.Now - cacheTime.Value).TotalDays < 1;
            }
            catch
            {
                atualizado = false;
            }

            Dispatcher.Invoke(() =>
            {
                TxtSyncStatus.Text = atualizado ? "Dados de manutenção atualizados" : "Dados de manutenção desatualizados";
                TxtSyncStatus.Foreground = atualizado ? Brushes.LimeGreen : Brushes.Red;
            });
        }

        /// <summary>
        /// Atualiza a data da base de dados consultando via ExcelService (pesado).
        /// </summary>
        private async Task AtualizarDataPlanilhaAsync()
        {
            DateTime? cacheTime = null;
            try
            {
                cacheTime = await Task.Run(() => ManutencoesService.GetCacheTimestamp());
            }
            catch (Exception ex)
            {
                _log.Warning($"Falha ao obter data do cache: {ex.Message}");
            }

            Dispatcher.Invoke(() =>
            {
                if (cacheTime.HasValue)
                {
                    TxtLastUpdate.Text = $"Última atualização: {cacheTime.Value:dd/MM/yyyy HH:mm}";
                    TxtLastUpdate.Foreground = Brushes.Black;
                    _log.Info($"Última atualização do cache: {cacheTime.Value}");
                }
                else
                {
                    TxtLastUpdate.Text = "Última atualização: --";
                    TxtLastUpdate.Foreground = Brushes.Red;
                    _log.Warning("Cache de manutenção não encontrado");
                }
            });
        }
        #endregion

        #region Botões auxiliares e Atualização
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new UpdatePromptWindow { Owner = this };
            if (prompt.ShowDialog() == true && prompt.ShouldUpdate)
                await RunUpdateAsync();
        }

        private async Task RunUpdateAsync()
        {
            var batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                       "win-x64",
                                       "AtualizadorSilencioso.bat");
            if (!File.Exists(batPath))
            {
                MessageBox.Show($"Arquivo de atualização não encontrado:\n{batPath}",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(batPath)
            });

            await Task.Delay(500);
        }

        private void BtnSelecionar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog
            {
                Description = "Selecione a pasta de origem",
                UseDescriptionForTitle = true,
                SelectedPath = _pastaOrigem,
                ShowNewFolderButton = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                _pastaOrigem = dlg.SelectedPath;
                TxtPasta.Text = Path.GetFileName(_pastaOrigem);
                _log.Info($"Pasta selecionada: {_pastaOrigem}");
                Properties.Settings.Default.LastFolder = _pastaOrigem;
                Properties.Settings.Default.Save();
            }
        }

        private void BtnAbrir_Click(object sender, RoutedEventArgs e)
        {
            if (_renamer != null && Directory.Exists(_renamer.LastDestination))
                Process.Start("explorer.exe", _renamer.LastDestination);
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_manutencoes != null)
            {
                _manutencoes.UpdateCompleted -= Manutencoes_UpdateCompleted;
                _manutencoes.StopAutoUpdate();
            }
        }

        private void Manutencoes_UpdateCompleted(DateTime time, bool fromInternet)
        {
            Dispatcher.Invoke(() =>
            {
                if (fromInternet)
                    _log.Info($"Dados de manutenção atualizados em {time:HH:mm:ss}");
                else
                    _log.Warning($"Falha ao atualizar dados de manutenção - usando cache ({time:HH:mm:ss})");
            });

            _ = AtualizarDataPlanilhaAsync();
        }
        #endregion
    }
}
