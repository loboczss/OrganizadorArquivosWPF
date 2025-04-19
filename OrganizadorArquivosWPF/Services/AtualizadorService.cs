using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using IWshRuntimeLibrary;           // Windows Script Host – para criar .lnk
using Newtonsoft.Json.Linq;

namespace OrganizadorArquivosWPF.Services
{
    public enum UpdateOutcome { Updated, AlreadyLatest, NoInternet, Error }

    public class AtualizadorService
    {
        private const string GitHubLatestReleaseApi =
            "https://api.github.com/repos/loboczss/OrganizadorArquivosWPF/releases/latest";

        // Pasta de instalação sem exigir UAC
        private static readonly string InstallFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer", "OrganizadorArquivosWPF");

        public async Task<UpdateOutcome> CheckForUpdateAsync(bool manual, IProgress<string> progress)
        {
            // 1. Consulta GitHub
            progress.Report("Conectando ao GitHub…");
            JObject rel;
            try
            {
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Add("User-Agent", "OrganizadorArquivosWPF");
                    var r = await http.GetAsync(GitHubLatestReleaseApi).ConfigureAwait(false);
                    if (r.StatusCode == HttpStatusCode.NotFound)
                    {
                        progress.Report("Release não encontrado.");
                        return UpdateOutcome.Error;
                    }
                    r.EnsureSuccessStatusCode();
                    rel = JObject.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch (HttpRequestException)
            {
                progress.Report("Sem internet.");
                return UpdateOutcome.NoInternet;
            }
            catch (Exception ex)
            {
                progress.Report("Erro: " + ex.Message);
                return UpdateOutcome.Error;
            }

            // 2. Verifica versão
            var remoteVer = new Version(((string)rel["tag_name"]).TrimStart('v'));
            var currVer = Assembly.GetExecutingAssembly().GetName().Version;
            if (remoteVer <= currVer)
            {
                progress.Report($"Já está na versão mais recente (v{currVer}).");
                return UpdateOutcome.AlreadyLatest;
            }

            // 3. Localiza ZIP "-full.zip"
            var asset = ((JArray)rel["assets"])
                        .Cast<JObject>()
                        .FirstOrDefault(a => ((string)a["name"])
                        .EndsWith("-full.zip", StringComparison.OrdinalIgnoreCase));

            if (asset == null)
            {
                progress.Report("ZIP '-full.zip' não encontrado.");
                return UpdateOutcome.Error;
            }

            string dlUrl = (string)asset["browser_download_url"];
            string tmpZip = Path.Combine(Path.GetTempPath(), "update.zip");

            // 4. Download
            progress.Report($"Baixando v{remoteVer}…");
            try
            {
                using (var http = new HttpClient())
                using (var src = await http.GetStreamAsync(dlUrl).ConfigureAwait(false))
                using (var dst = new FileStream(tmpZip, FileMode.Create, FileAccess.Write))
                {
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException)
            {
                progress.Report("Falha no download.");
                return UpdateOutcome.NoInternet;
            }
            catch (Exception ex)
            {
                progress.Report("Erro no download: " + ex.Message);
                return UpdateOutcome.Error;
            }

            // 5. Extrai para histórico (Área de Trabalho)
            progress.Report("Extraindo arquivos…");
            string histDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "OneEngRenamerUpdates", $"v{remoteVer}");

            if (Directory.Exists(histDir)) Directory.Delete(histDir, true);
            Directory.CreateDirectory(histDir);

            try
            {
                ZipFile.ExtractToDirectory(tmpZip, histDir);
            }
            catch (Exception ex)
            {
                progress.Report("Erro ao extrair: " + ex.Message);
                return UpdateOutcome.Error;
            }

            // 6. Copia para staging em %TEMP%
            string staging = Path.Combine(Path.GetTempPath(), "OAWUpdate", $"v{remoteVer}");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            foreach (var f in Directory.GetFiles(histDir, "*", SearchOption.AllDirectories))
            {
                var relPath = f.Substring(histDir.Length + 1);
                var dest = Path.Combine(staging, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                System.IO.File.Copy(f, dest, true);
            }

            // 7. Garante atalho no Desktop com nome "CompillerLog"
            CreateDesktopShortcut();

            // 8. Gera script BAT com ROBOCOPY
            progress.Report("Preparando script de instalação…");
            string batPath = Path.Combine(Path.GetTempPath(), "InstallUpdate.bat");
            string exePath = Path.Combine(InstallFolder, "OrganizadorArquivosWPF.exe");

            string bat =
$@"@echo off
timeout /t 2 /nobreak >nul
robocopy ""{staging}"" ""{InstallFolder}"" /MIR /R:3 /W:1 >nul
start """" ""{exePath}""
del /f /q ""%~f0""
";
            System.IO.File.WriteAllText(batPath, bat);

            // 9. Executa script e encerra app
            progress.Report("Aplicando atualização…");
            Process.Start(new ProcessStartInfo(batPath)
            {
                UseShellExecute = true,
                CreateNoWindow = true
            });
            Application.Current.Shutdown();

            return UpdateOutcome.Updated; // não retorna
        }

        /// <summary>
        /// Cria/atualiza atalho na Área de Trabalho chamado "CompillerLog.lnk".
        /// </summary>
        private static void CreateDesktopShortcut()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string link = Path.Combine(desktop, "CompillerLog.lnk");
            string target = Path.Combine(InstallFolder, "OrganizadorArquivosWPF.exe");

            try
            {
                var shell = new WshShell();
                IWshShortcut s = (IWshShortcut)shell.CreateShortcut(link);
                s.TargetPath = target;
                s.WorkingDirectory = InstallFolder;
                s.Description = "CompillerLog";
                s.IconLocation = target + ",0";
                s.Save();
            }
            catch
            {
                // Ignora falhas na criação do atalho
            }
        }
    }
}
