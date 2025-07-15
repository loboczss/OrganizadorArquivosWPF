// File: App.xaml.cs
using Microsoft.Win32;
using OrganizadorArquivosWPF.Helpers;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Utils;
using OrganizadorArquivosWPF.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrganizadorArquivosWPF
{
    public partial class App : Application
    {
        private const string MutexName = "OrganizadorArquivosWPF_SingleInstance";
        private const string ShowEventName = "OrganizadorArquivosWPF_ShowMain";

        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private CancellationTokenSource? _showEventCts;
        private Task? _showEventTask;
        private TrayService _tray;

        private BackupService _backup;
        private FileSyncService _sync;          // vigia “tipo OneDrive”

        protected override async void OnStartup(StartupEventArgs e)
        {
            bool created;
            _mutex = new Mutex(true, MutexName, out created);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            if (!created)
            {
                _showEvent.Set();
                ActivatePreviousInstance();
                Shutdown();
                return;
            }

            StartShowEventListener();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            base.OnStartup(e);
            EnsureRunAtStartup();

            // Splash inicial
            var splash = new SplashWindow();
            splash.Show();

            var track = new Utils.ProgressTracker(
                new Progress<double>(v => splash.SetProgress(v)), 3); // 3 passos

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            // 1) serviços de tray
            _tray = new TrayService();
            try { await _tray.StartAsync(track.NextSegment()).WaitAsync(cts.Token); } catch { }

            // 2) planilha funcionários
            splash.SetStatus("Baixando planilha de funcionários...");
            var funcSrv = new FuncionariosService();
            await Task.Run(funcSrv.ListarTodos, cts.Token)
                      .ContinueWith(_ => track.NextSegment().Report(100));

            // 3) sincronização inicial
            splash.SetStatus("Sincronizando arquivos com SharePoint...");
            _backup = new BackupService();
            try
            {
                await _backup.SincronizarTudoAsync(cts.Token)
                             .ContinueWith(_ => track.NextSegment().Report(100));
            }
            catch { /* ignorar timeout */ }

            splash.SetProgress(100);
            splash.Close();

            // Atualização
            if (await PromptUpdateAsync())
            {
                Shutdown();
                Environment.Exit(0);
                return;
            }

            // Monitor de alterações em tempo real
            _sync = new FileSyncService(_backup);

            // Login
            var login = new LoginWindow();
            if (login.ShowDialog() == true)
            {
                Current.MainWindow = new MainWindow(login.Usuario);
                Current.MainWindow.Show();
            }
        }

        private async Task<bool> PromptUpdateAsync()
        {
            Versao.GravarVersaoEmTxt();

            var prompt = new UpdatePromptWindow();
            if (prompt.ShowDialog() == true && prompt.ShouldUpdate)
            {
                var updater = new AtualizadorService();
                var file = await updater.DownloadLatestReleaseAsync();
                if (file == null)
                {
                    MessageBox.Show("Arquivo de atualização não encontrado.",
                                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                Process.Start(new ProcessStartInfo(updater.CreateUpdateBatch(file))
                {
                    UseShellExecute = true
                });
                return true;
            }
            return false;
        }

        private void EnsureRunAtStartup()
        {
            try
            {
                // Entrada HKCU\Run
                var runKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                var exe = Environment.ProcessPath ??
                          Process.GetCurrentProcess().MainModule?.FileName ??
                          Assembly.GetExecutingAssembly().Location;

                runKey?.SetValue("OrganizadorArquivosWPF", '"' + exe + '"');
                runKey?.Close();

                // Atalho na pasta Startup
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, "OrganizadorArquivosWPF.lnk");

                if (!File.Exists(shortcutPath))
                {
                    // ↓↓↓ ajuste: shell é dynamic
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                    dynamic shCut = shell.CreateShortcut(shortcutPath);
                    shCut.TargetPath = exe;
                    shCut.WorkingDirectory = Path.GetDirectoryName(exe);
                    shCut.Save();
                }
            }
            catch { /* silencioso */ }
        }

        #region single-instance & tray helpers
        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _tray?.DisposeAsync().AsTask().Wait();
            _sync?.DisposeAsync().AsTask().Wait();

            if (_showEventCts != null)
            {
                _showEventCts.Cancel();
                _showEvent.Set();
                try { _showEventTask?.Wait(); } catch { }
                _showEventCts.Dispose();
            }

            _showEvent?.Dispose();
            base.OnExit(e);
            Environment.Exit(0);
        }

        private void StartShowEventListener()
        {
            _showEventCts = new CancellationTokenSource();
            _showEventTask = Task.Run(() =>
            {
                try
                {
                    while (!_showEventCts.IsCancellationRequested)
                    {
                        _showEvent.WaitOne();
                        if (_showEventCts.IsCancellationRequested) break;
                        Dispatcher.Invoke(() =>
                        {
                            var main = Current.MainWindow;
                            if (main != null)
                            {
                                main.Show();
                                var handle = new WindowInteropHelper(main).Handle;
                                if (handle != IntPtr.Zero)
                                {
                                    WindowHelper.ShowWindow(handle, WindowHelper.SW_RESTORE);
                                    WindowHelper.SetForegroundWindow(handle);
                                }
                            }
                            else
                            {
                                var login = new LoginWindow();
                                if (login.ShowDialog() == true)
                                {
                                    Current.MainWindow = new MainWindow(login.Usuario);
                                    Current.MainWindow.Show();
                                }
                            }
                        });
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { LoggerService.Instance.Warning($"ShowEvent loop: {ex.Message}"); }
            }, _showEventCts.Token);
        }

        private static void ActivatePreviousInstance()
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                var h = p.MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    WindowHelper.ShowWindow(h, WindowHelper.SW_RESTORE);
                    WindowHelper.SetForegroundWindow(h);
                }
                break;
            }
        }

        #region Global exception handlers
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LoggerService.Instance?.Critical("UI exception: " + e.Exception.Message);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                LoggerService.Instance?.Critical("Unhandled exception: " + ex.Message);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LoggerService.Instance?.Critical("Task exception: " + e.Exception.Message);
            e.SetObserved();
        }
        #endregion
        #endregion
    }
}
