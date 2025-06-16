using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OrganizadorArquivosWPF.Services;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Views;

namespace OrganizadorArquivosWPF
{
    public partial class App : Application
    {
        private TrayService _tray;
        protected async override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            EnsureRunAtStartup();

            // ------------------------------------------------------
            // (Opcional) 0) Abre splash e executa sincronização
            // ------------------------------------------------------
            var splash = new SplashWindow();
            splash.Owner = null;       // para não ficar “presa” a outra janela
            splash.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            splash.Show();
            var progress = new Progress<int>(p => splash.SetProgress(p));

            // Dispara sincronização e download de dados em background
            _ = Task.Run(() => SyncVerifierService.VerificarOuSincronizarArquivo());
            _ = Task.Run(async () =>
            {
                var manutencao = new ManutencoesService();
                try { await manutencao.ObterDadosAsync(progress); } catch { }
            });


            // Aguarda breve momento apenas para mostrar o splash
            await Task.Delay(500);
            // Dispara sincronização e download de dados durante o splash
            var syncTask = Task.Run(() => SyncVerifierService.VerificarOuSincronizarArquivo());
            try
            {
                var manutencao = new ManutencoesService();
                await manutencao.ObterDadosAsync(progress);
            }
            catch
            {
                // Ignora falhas de download no splash
            }

            // Fecha splash após completar o download
            splash.Close();

            _tray = new TrayService();
            _tray.Start(null);
            // ------------------------------------------------------

            // ------------------------------------------------------
            // 1) Exibe Login
            // ------------------------------------------------------
            var login = new LoginWindow();
            bool? loginOk = login.ShowDialog();
            if (loginOk != true)
            {
                // Usuário cancelou login
                Shutdown();
                return;
            }

            UsuarioRecord user = login.Usuario;

            // ------------------------------------------------------
            // 2) Prompt de atualização antes de abrir o main
            // ------------------------------------------------------
            var updatePrompt = new UpdatePromptWindow();
            bool? wantsUpdate = updatePrompt.ShowDialog();
            if (wantsUpdate == true && updatePrompt.ShouldUpdate)
            {
                RunUpdaterBat();
                Shutdown();
                return;
            }

            // ------------------------------------------------------
            // 3) Se não atualizou, continua para a MainWindow
            // ------------------------------------------------------
            var main = new MainWindow(user);
            Current.MainWindow = main;
            main.Show();

            // ------------------------------------------------------
            // 4) (Opcional) Se você não esperou a syncTask, pode aguardar aqui
            // ------------------------------------------------------
            // await syncTask;
            // Se a MainWindow usa dados que dependem do Excel, você pode
            // atualizar a interface (ou mostrar um aviso) quando a Task terminar.
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
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
