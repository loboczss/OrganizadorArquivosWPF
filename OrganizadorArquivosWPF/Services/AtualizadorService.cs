using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Squirrel;
using System.Windows;
using System.Net.Http;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Resultado da tentativa de atualização via Squirrel/GitHub.
    /// </summary>
    public enum UpdateOutcome
    {
        Updated,       // Atualizou e reiniciou
        AlreadyLatest, // Nenhuma versão nova
        NoInternet,    // Falha de rede
        Error          // Outro erro
    }

    /// <summary>
    /// Serviço de atualização automática usando Squirrel.Windows
    /// apontando para Releases do GitHub.
    /// </summary>
    public class AtualizadorService
    {
        // URL base do seu repositório GitHub (sem "/releases")
        private const string GitHubUrl = "https://github.com/loboczss/OrganizadorArquivosWPF";

        /// <summary>
        /// Checa e aplica atualizações.
        /// Todas as mensagens de status chegam via progress.Report(string).
        /// </summary>
        public async Task<UpdateOutcome> CheckForUpdateAsync(bool manual,
                                                            IProgress<string> progress)
        {
            try
            {
                progress?.Report("Conectando ao GitHub...");
                // Cria UpdateManager apontando para GitHub
                using (var mgr = await UpdateManager.GitHubUpdateManager(
                                    GitHubUrl, prerelease: false))
                {
                    progress?.Report("Buscando novas versões...");
                    var updateInfo = await mgr.CheckForUpdate();

                    // Se não tiver nada novo
                    if (!updateInfo.ReleasesToApply.Any())
                    {
                        if (manual)
                            progress?.Report("Já está na versão mais recente.");
                        return UpdateOutcome.AlreadyLatest;
                    }

                    // Baixa releases pendentes, reportando % concluído
                    progress?.Report("Baixando atualizações...");
                    await mgr.DownloadReleases(
                        updateInfo.ReleasesToApply,
                        percent => progress?.Report($"Baixando: {percent}%"));

                    // Aplica delta ou full, reportando %
                    progress?.Report("Instalando atualizações...");
                    await mgr.ApplyReleases(
                        updateInfo,
                        percent => progress?.Report($"Instalando: {percent}%"));

                    // Atualiza o atalho do desktop/start menu
                    mgr.CreateShortcutForThisExe();

                    progress?.Report("Atualização concluída. Reiniciando...");
                    await Task.Delay(1000);

                    // Reinicia o app
                    UpdateManager.RestartApp();

                    return UpdateOutcome.Updated;
                }
            }
            catch (HttpRequestException)
            {
                // Falha de rede
                progress?.Report("Sem conexão com a internet.");
                return UpdateOutcome.NoInternet;
            }
            catch (Exception ex)
            {
                // Outros erros
                if (manual)
                    progress?.Report($"Erro na atualização: {ex.Message}");
                return UpdateOutcome.Error;
            }
        }
    }
}
