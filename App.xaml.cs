using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using IWshRuntimeLibrary;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Views;
using OrganizadorArquivosWPF.Helpers;

namespace OrganizadorArquivosWPF
{
    public partial class App : Application
    {
        private const string MutexName = "OrganizadorArquivosWPF_SingleInstance";
        private const string ShowEventName = "OrganizadorArquivosWPF_ShowMain";
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private TrayService _tray;

        protected async override void OnStartup(StartupEventArgs e)
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
            base.OnStartup(e);
            EnsureRunAtStartup();

            var splash = new SplashWindow();
            splash.Show();

            _tray = new TrayService();
            var progress = new Progress<int>(v => splash.SetProgress(v));

            // Executa downloads em paralelo e limita o tempo de espera
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var trayTask = _tray.StartAsync(progress).WaitAsync(cts.Token);

            splash.SetStatus("Baixando planilha de funcionários...");
            splash.SetProgress(-1);
            var funcService = new FuncionariosService();
            var funcTask = Task.Run(() => funcService.ListarTodos(), cts.Token);

            try
            {
                await Task.WhenAll(trayTask, funcTask);
            }
            catch (OperationCanceledException)
            {
                // Continua mesmo que alguma tarefa exceda o tempo limite
            }

            // Salva versão antes de seguir para o prompt de atualização
            Versao.GravarVersaoEmTxt();

            splash.Close();

            // Prompt de atualização
            var updatePrompt = new UpdatePromptWindow();
            bool? wantsUpdate = updatePrompt.ShowDialog();
            if (wantsUpdate == true && updatePrompt.ShouldUpdate)
            {
                RunUpdaterExe();
                Shutdown();
                return;
            }

            // Login
            var login = new LoginWindow();
            bool? loginOk = login.ShowDialog();
            if (loginOk == true)
            {
                UsuarioRecord user = login.Usuario;
                var main = new MainWindow(user);
                Current.MainWindow = main;
                main.Show();
            }
        }


        private void RunUpdaterExe()
        {
            string exePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "atualiza",
                "AtualizadorApp.Wpf.exe");

            if (!System.IO.File.Exists(exePath))
            {
                MessageBox.Show(
                    $"Arquivo de atualização não encontrado:\n{exePath}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });
        }

        private void EnsureRunAtStartup()
        {
            try
            {
                var runKey = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                var exe = Environment.ProcessPath ??
                          System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
                          System.Reflection.Assembly.GetExecutingAssembly().Location;
                runKey?.SetValue("OrganizadorArquivosWPF", '"' + exe + '"');
                runKey?.Close();

                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "OrganizadorArquivosWPF.lnk");
                if (!System.IO.File.Exists(shortcutPath))
                {
                    var shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = exe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                    shortcut.Save();
                }
            }
            catch { /* ignore registry errors */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _tray?.Dispose();
            _showEvent?.Dispose();
            base.OnExit(e);
        }

        private void StartShowEventListener()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.Invoke(() =>
                    {
                        var main = Current.MainWindow;
                        if (main != null)
                        {
                            main.Show();
                            var handle = new WindowInteropHelper(main).Handle;
                            if (handle != IntPtr.Zero)
                            {
                                Utils.WindowHelper.ShowWindow(handle, Utils.WindowHelper.SW_RESTORE);
                                Utils.WindowHelper.SetForegroundWindow(handle);
                            }
                        }
                    });
                }
            });
        }

        private static void ActivatePreviousInstance()
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id == current.Id)
                    continue;

                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    Utils.WindowHelper.ShowWindow(handle, Utils.WindowHelper.SW_RESTORE);
                    Utils.WindowHelper.SetForegroundWindow(handle);
                }
                break;
            }
        }
    }
}
