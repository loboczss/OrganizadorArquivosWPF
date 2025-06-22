using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Views;

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

            _tray = new TrayService();
            await Task.Run(() => _tray.Start(null)); // <-- await real aqui

            // Prompt de atualização
            var updatePrompt = new UpdatePromptWindow();
            bool? wantsUpdate = updatePrompt.ShowDialog();
            if (wantsUpdate == true && updatePrompt.ShouldUpdate)
            {
                RunUpdaterBat();
                Shutdown();
                return;
            }

            // Login
            var login = new LoginWindow();
            bool? loginOk = login.ShowDialog();
            if (loginOk != true)
            {
                Shutdown();
                return;
            }

            UsuarioRecord user = login.Usuario;

            var main = new MainWindow(user);
            Current.MainWindow = main;
            main.Show();
        }


        private void RunUpdaterBat()
        {
            string batPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "win-x64",
                "AtualizadorSilencioso.bat");

            if (!File.Exists(batPath))
            {
                MessageBox.Show(
                    $"Arquivo de atualização não encontrado:\n{batPath}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(batPath)
            });
        }

        private void EnsureRunAtStartup()
        {
            try
            {
                var runKey = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                runKey?.SetValue("OrganizadorArquivosWPF", '"' + exe + '"');
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
