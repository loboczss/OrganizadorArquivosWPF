using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace OrganizadorArquivosWPF.Services
{
    public enum UpdateOutcome { Launched, NotFound, Error }

    public class AtualizadorService
    {
        /// <summary>
        /// Em vez de baixar/extrair aqui, dispara o updater externo e devolve o resultado.
        /// Não fecha o app principal—o batch do updater cuidará de matar o processo
        /// apenas quando o usuário confirmar a atualização na janela externa.
        /// </summary>
        public Task<UpdateOutcome> CheckForUpdateAsync(
            bool manual,
            IProgress<string> statusProgress,
            IProgress<double> percentProgress)
        {
            try
            {
                // 1) Localiza o OneEngUpdater.exe dentro de win-x64
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var updaterExe = Path.Combine(baseDir, "win-x64", "OneEngUpdater.exe");

                if (!File.Exists(updaterExe))
                {
                    statusProgress.Report($"Atualizador não encontrado em:\n{updaterExe}");
                    return Task.FromResult(UpdateOutcome.NotFound);
                }

                // 2) Informa e inicia
                statusProgress.Report("Abrindo atualizador...");
                Process.Start(new ProcessStartInfo(updaterExe)
                {
                    UseShellExecute = true
                });

                // 3) Retorna imediatamente, sem fechar o app
                return Task.FromResult(UpdateOutcome.Launched);
            }
            catch (Exception ex)
            {
                statusProgress.Report("Erro ao iniciar o atualizador: " + ex.Message);
                return Task.FromResult(UpdateOutcome.Error);
            }
        }
    }
}