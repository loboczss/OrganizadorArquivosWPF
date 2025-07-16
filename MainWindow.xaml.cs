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
using OrganizadorArquivosWPF.Helpers;

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
        private sealed class BarProgress : IProgress<double>
        {
            private readonly MainWindow _wnd;
            public BarProgress(MainWindow wnd) => _wnd = wnd;
            public void Report(double value)
                => _wnd.Dispatcher.Invoke(() =>
                {
                    _wnd.Progress.Value = value;
                    _wnd.ProgressPercentText.Text = $"{value:0.#}%";
                });
        }

        private sealed class DownloadProgress : IProgress<double>
        {
            private readonly MainWindow _wnd;
            public DownloadProgress(MainWindow wnd) => _wnd = wnd;
            public void Report(double value)
            {
                _wnd.Dispatcher.Invoke(() =>
                {
                    if (value >= 100)
                    {
                        _wnd.DownloadBar.Value = 100;
                        _wnd.DownloadBar.Visibility = Visibility.Collapsed;
                        _wnd.DownloadBar.IsIndeterminate = false;
                        _wnd.TxtSyncStatus.Text = "Download concluído";
                    }
                    else if (value < 0)
                    {
                        if (_wnd.DownloadBar.Visibility != Visibility.Visible)
                            _wnd.DownloadBar.Visibility = Visibility.Visible;
                        _wnd.DownloadBar.IsIndeterminate = true;
                        _wnd.TxtSyncStatus.Text = "Iniciando download...";
                    }
                    else
                    {
                        if (_wnd.DownloadBar.Visibility != Visibility.Visible)
                            _wnd.DownloadBar.Visibility = Visibility.Visible;
                        _wnd.DownloadBar.IsIndeterminate = false;
                        _wnd.DownloadBar.Value = value;
                        _wnd.TxtSyncStatus.Text = $"Baixando dados ({value:0.#}%)";
                    }
                });
            }
        }

        private sealed class UploadProgress : IProgress<double>
        {
            private readonly MainWindow _wnd;
            public UploadProgress(MainWindow wnd) => _wnd = wnd;
            public void Report(double value)
            {
                _wnd.Dispatcher.Invoke(() =>
                {
                    if (value >= 100)
                    {
                        _wnd.UploadBar.Value = 100;
                        _wnd.UploadBar.Visibility = Visibility.Collapsed;
                        _wnd.UploadBar.IsIndeterminate = false;
                        _wnd.TxtSyncStatus.Text = "Backup concluído";
                    }
                    else if (value < 0)
                    {
                        if (_wnd.UploadBar.Visibility != Visibility.Visible)
                            _wnd.UploadBar.Visibility = Visibility.Visible;
                        _wnd.UploadBar.IsIndeterminate = true;
                        _wnd.TxtSyncStatus.Text = "Preparando upload...";
                    }
                    else
                    {
                        if (_wnd.UploadBar.Visibility != Visibility.Visible)
                            _wnd.UploadBar.Visibility = Visibility.Visible;
                        _wnd.UploadBar.IsIndeterminate = false;
                        _wnd.UploadBar.Value = value;
                        _wnd.TxtSyncStatus.Text = $"Enviando backup ({value:0.#}%)";
                    }
                });
            }
        }
        #endregion

        #region Campos e serviços (mais “leves”)

        private LoggerService _log;
        private RenamerService _renamer;
        private AtualizadorService _update;
        private ManutencoesService _manutencoes;
        private BackupService _backup;
        private List<ClientRecord> _cachedRecords;
        private Dictionary<string, ClientRecord> _recordsByOs;
        private readonly ObservableCollection<LogEntry> _logs;
        private readonly DownloadProgress _downloadReporter;
        private readonly UploadProgress _uploadReporter;
        private LogEntry _lastUpdateEntry;
        private LogEntry _lastBackupEntry;
        private bool _manualSyncRunning;
        private string _pastaOrigem;
        public bool AllowClose { get; set; }
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
            _uploadReporter = new UploadProgress(this);

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
            ConfigurarUploadBar();

            // Exibe texto-padrão nas labels que serão preenchidas
            TxtSyncStatus.Text = "Verificando dados de manutenção...";
            TxtSyncStatus.Foreground = Brushes.Gray;

            TxtLastUpdate.Text = "Carregando dados de manutenção...";
            TxtLastUpdate.Foreground = Brushes.Gray;

            // Define pasta padrão (pode ser rápido o suficiente para rodar no construtor)
            DefinirPastaPadrao();

            // Assina evento Loaded para disparar o que é pesado em background
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
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
                var logFileService = new LogFileService();
                _renamer = new RenamerService(_log, logFileService);
                _update = new AtualizadorService();
                _manutencoes = new ManutencoesService();
                _backup = new BackupService();
            });

            // Carrega rapidamente registros do cache local (sem novo download)
            _cachedRecords = await Task.Run(() => _manutencoes.LoadCachedRecords());
            BuildRecordIndex();

            // 2) Atualiza status de sincronização
            await AtualizarStatusSincronizacaoAsync();

            // 3) Atualiza data da base de dados
            await AtualizarDataPlanilhaAsync();

            // 4) Baixa dados de manutenção se possível
            try
            {
                _manutencoes.UpdateCompleted += Manutencoes_UpdateCompleted;

                // Se o download inicial já foi realizado pela SplashScreen
                // (através do TrayService), evitamos repetir a operação aqui.
                DateTime? cacheTime = ManutencoesService.GetCacheTimestamp();
                bool cacheRecente =
                    cacheTime.HasValue &&
                    (DateTime.Now - cacheTime.Value).TotalMinutes < 10;

                if (!cacheRecente)
                    await _manutencoes.ObterDadosAsync(_downloadReporter);

                // Inicia o auto-update para rodar a cada 10 minutos
                _manutencoes.StartAutoUpdate(TimeSpan.FromMinutes(10), _downloadReporter);
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
                System.Windows.MessageBox.Show("Informe pasta e Nº OS.",
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

            bool manualMode = osNum.Length > 0 && osNum.All(c => c == '0');

            if (manualMode)
                _log.Warning("Modo manual ativado");

            ClientRecord record = null;
            if (!manualMode)
            {
                try
                {
                    _recordsByOs?.TryGetValue(fullOS, out record);
                    reporter.Report(100);
                }
                catch (Exception ex)
                {
                    _log.Error($"Erro ao ler base de dados: {ex.Message}");
                    System.Windows.MessageBox.Show($"Erro ao ler base de dados: {ex.Message}",
                                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    ToggleUIBusy(false);
                    return;
                }
            }
            else
            {
                record = null;
            }

            if (record == null)
            {
                _log.Warning("OS não encontrada – solicitando dados manuais…");
                record = await MostrarFallbackAsync(fullOS, uf, manualMode);
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

                // Descomente essa linha abaixo para liberar o botão abrir
                // BtnAbrir.Visibility = Visibility.Visible;

                // Limpa o log visual, texto da O.S e abre a pasta do cliente após concluir a operação com sucesso
                _log.Clear();
                TxtOS.Clear();
                if (_renamer != null && Directory.Exists(_renamer.LastDestination))
                    Process.Start("explorer.exe", _renamer.LastDestination);
            }
            catch (Exception ex)
            {
                _log.Error($"Erro no renomeio: {ex.Message}");
                System.Windows.MessageBox.Show($"Erro no renomeio: {ex.Message}",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ToggleUIBusy(false);
            }
        }

        private Task<ClientRecord> MostrarFallbackAsync(string fullOS, string uf, bool allowAnyId = false)
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
                    var fb = new FallbackWindow(fullOS, rotaList, uf, _cachedRecords, allowAnyId) { Owner = this };
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
            var result = System.Windows.MessageBox.Show(
                $"Cliente:\n\n{nomeCliente}\n\nContinuar?",
                "Confirmação",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }
        #endregion

        #region Helpers UI
        private void ToggleUIBusy(bool ativo)
        {
            BtnProcessar.IsEnabled = !ativo;
            Progress.Visibility = ativo ? Visibility.Visible : Visibility.Collapsed;
            ProgressPercentText.Visibility = ativo ? Visibility.Visible : Visibility.Collapsed;
            if (!ativo)
            {
                Progress.Value = 0;
                ProgressPercentText.Text = "0%";
            }
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
        }

        private void ConfigurarProgressBar()
        {
            Progress.Minimum = 0;
            Progress.Maximum = 100;
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Collapsed;
            ProgressPercentText.Visibility = Visibility.Collapsed;
            Progress.Value = 0;
            ProgressPercentText.Text = "0%";
        }

        private void ConfigurarDownloadBar()
        {
            DownloadBar.Minimum = 0;
            DownloadBar.Maximum = 100;
            DownloadBar.IsIndeterminate = false;
            DownloadBar.Visibility = Visibility.Collapsed;
        }

        private void ConfigurarUploadBar()
        {
            UploadBar.Minimum = 0;
            UploadBar.Maximum = 100;
            UploadBar.IsIndeterminate = false;
            UploadBar.Visibility = Visibility.Collapsed;
        }

        private void BuildRecordIndex()
        {
            _recordsByOs = _cachedRecords?
                .GroupBy(r => r.NumOS, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, ClientRecord>(StringComparer.OrdinalIgnoreCase);
        }

        // ===================== System Tray (Bandeja) ======================


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
        /// Atualiza a data da base de dados consultando via link (pesado).
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
                }
                else
                {
                    TxtLastUpdate.Text = "Última atualização: --";
                    TxtLastUpdate.Foreground = Brushes.Red;
                    _log.Critical("Cache de manutenção não encontrado");
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
            var service = new AtualizadorService();
            var file = await service.DownloadLatestReleaseAsync();
            if (file == null)
            {
                System.Windows.MessageBox.Show("Arquivo de atualização não encontrado.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var batch = service.CreateUpdateBatch(file);
            Process.Start(new ProcessStartInfo(batch) { UseShellExecute = true });

            await Task.Delay(500);

            AllowClose = true;
            Application.Current?.Shutdown();
            Environment.Exit(0);
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
                Properties.Settings.Default.LastFolder = _pastaOrigem;
                Properties.Settings.Default.Save();
            }
        }

        private void BtnAbrir_Click(object sender, RoutedEventArgs e)
        {
            if (_renamer != null && Directory.Exists(_renamer.LastDestination))
                Process.Start("explorer.exe", _renamer.LastDestination);
        }

        private async void BtnSyncAll_Click(object sender, RoutedEventArgs e)
        {
            if (_manualSyncRunning)
                return;

            await BaixarDadosAgoraAsync();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                Hide();

            base.OnStateChanged(e);
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_manutencoes != null)
            {
                _manutencoes.UpdateCompleted -= Manutencoes_UpdateCompleted;
                _manutencoes.StopAutoUpdate();
            }
        }

        // -----------------------------------------------------------------------------
        // 🔄 Evento disparado pelo ManutencoesService sempre que ele termina uma tentativa
        // -----------------------------------------------------------------------------
        private void Manutencoes_UpdateCompleted(DateTime time, bool fromInternet)
        {
            Dispatcher.Invoke(() =>
            {
                if (_lastUpdateEntry != null)
                    _logs.Remove(_lastUpdateEntry);

                string mensagem;
                if (fromInternet)
                {
                    _cachedRecords = new List<ClientRecord>(_manutencoes.Records);
                    BuildRecordIndex();
                    mensagem = $"Dados de manutenção atualizados em {time:HH:mm:ss} — {_cachedRecords.Count} registros carregados.";
                    _manutencoes.ClearData();
                    _log.Info(mensagem);
                }
                else
                {
                    DateTime? cacheTime = ManutencoesService.GetCacheTimestamp();
                    bool cacheVelho =
                        !cacheTime.HasValue ||
                        (DateTime.Now - cacheTime.Value).TotalDays >= 1;

                    _cachedRecords = new List<ClientRecord>(_manutencoes.Records);
                    BuildRecordIndex();

                    if (cacheVelho)
                    {
                        mensagem = $"Falha ao atualizar dados de manutenção – dados do cache têm mais de 1 dias — {_cachedRecords.Count} registros.";
                        _manutencoes.ClearData();
                        _log.Critical(mensagem);
                    }
                    else
                    {
                        mensagem = $"Sem conexão, usando dados baixados dia ({cacheTime.Value:dd/MM HH:mm}) — {_cachedRecords.Count} registros.";
                        _manutencoes.ClearData();
                        _log.Info(mensagem);
                    }
                }

                _lastUpdateEntry = _logs.LastOrDefault();
            });

            if (!_manualSyncRunning && fromInternet && _backup != null && _renamer != null &&
                !string.IsNullOrWhiteSpace(_renamer.LastDestination) &&
                Directory.Exists(_renamer.LastDestination))
            {
                _uploadReporter.Report(0);
                _ = _backup.EnviarBackupAsync(_renamer.LastDestination, null, _uploadReporter)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            _log.Error($"Backup falhou: {t.Exception?.GetBaseException().Message}");
                            return;
                        }

                        var enviados = t.Result.Count(r => r.Verificado);
                        Dispatcher.Invoke(() =>
                        {
                            if (_lastBackupEntry != null)
                                _logs.Remove(_lastBackupEntry);
                            _log.Info($"Backup concluído ({enviados} arquivos) às {DateTime.Now:HH:mm:ss}");
                            _lastBackupEntry = _logs.LastOrDefault();
                        });
                    });
            }

            try
            {
                // Usa os dados já baixados pelo serviço
            }
            catch (Exception ex)
            {
                _log.Error($"Erro ao atualizar dados de manutenção: {ex.Message}");
            }

            // Atualiza a label "Última atualização" sem aguardar
            _ = AtualizarDataPlanilhaAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.Error($"AtualizarDataPlanilhaAsync falhou: {t.Exception?.GetBaseException().Message}");
            });
            
        }

        /// <summary>
        /// Dispara manualmente o download dos dados de manutenção utilizando
        /// o mesmo serviço e reporter da janela principal.
        /// </summary>
        public async Task BaixarDadosAgoraAsync()
        {
            if (_manutencoes == null)
                return;

            _manualSyncRunning = true;
            Task<IReadOnlyList<FileUploadResult>> backupTask = Task.FromResult<IReadOnlyList<FileUploadResult>>(new List<FileUploadResult>());

            if (_backup != null && _renamer != null &&
                !string.IsNullOrWhiteSpace(_renamer.LastDestination) &&
                Directory.Exists(_renamer.LastDestination))
            {
                _uploadReporter.Report(0);
                backupTask = _backup.EnviarBackupAsync(_renamer.LastDestination, null, _uploadReporter);
            }

            try
            {
                var tracker = new Utils.ProgressTracker(_downloadReporter, 3);
                var downloadTask = _manutencoes.ObterDadosAsync(tracker.NextSegment());
                var instService = new InstalacaoService();
                var instalacaoTask = instService.AtualizarArquivoAsync(tracker.NextSegment());
                var uploadSeg = tracker.NextSegment();
                await Task.WhenAll(downloadTask, instalacaoTask, backupTask);
                uploadSeg.Report(100);
                _manutencoes.ClearData();

                if (_backup != null)
                {
                    try { await _backup.SincronizarPastasRenomeacaoAsync(); }
                    catch (Exception ex) { _log.Error($"Sincronizacao: {ex.Message}"); }
                }

                if (backupTask.Status == TaskStatus.RanToCompletion)
                {
                    var enviados = backupTask.Result.Count(r => r.Verificado);
                    Dispatcher.Invoke(() =>
                    {
                        if (_lastBackupEntry != null)
                            _logs.Remove(_lastBackupEntry);
                        _log.Info($"Backup concluído ({enviados} arquivos) às {DateTime.Now:HH:mm:ss}");
                        _lastBackupEntry = _logs.LastOrDefault();
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Erro ao baixar dados manualmente: {ex.Message}");
            }
            finally
            {
                _manualSyncRunning = false;
            }
        }

        #endregion
    }
}
