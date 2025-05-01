// App.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace OrganizadorArquivosWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = Environment.GetCommandLineArgs();
            if (args.Contains("-startup"))
            {
                // 1) Fecha qualquer janela que possa estar aberta
                // (garante que não volte ao MainWindow)
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // 2) Localiza o updater externo em win-x64
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var updaterPath = Path.Combine(baseDir, "win-x64", "OneEngUpdater.exe");

                // 3) Se existir, dispara; senão, mostra erro
                if (File.Exists(updaterPath))
                {
                    Process.Start(new ProcessStartInfo(updaterPath)
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        $"Não foi possível encontrar o atualizador:\n{updaterPath}",
                        "Erro ao Atualizar",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }

                // 4) Fecha o app principal imediatamente
                Current.Shutdown();
            }
        }
    }
}
