using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using IWshRuntimeLibrary;

namespace OrganizadorArquivosWPF.Services
{
    public enum UpdateOutcome { Updated, AlreadyLatest, NoInternet, Error }

    public class AtualizadorService
    {
        private readonly string _versaoUrl;
        private readonly string _zipUrl;
        private const string InstallFolder = @"C:\One\OrganizadorArquivosWPF";

        public AtualizadorService()
        {
            var cfg = UpdateConfig.Load();
            _versaoUrl = cfg.VersaoUrl;
            _zipUrl = cfg.ZipUrl;
        }

        /// <summary>
        /// Checa e instala atualização.
        /// Todas as mensagens vão para progress.Report(...).
        /// </summary>
        public async Task<UpdateOutcome> CheckForUpdateAsync(bool manual, IProgress<string> progress)
        {
            Version remoteVersion;
            progress?.Report("Conectando…");

            // 1) Lê versão remota
            try
            {
                using (var http = new HttpClient())
                {
                    var txt = await http.GetStringAsync(_versaoUrl).ConfigureAwait(false);
                    if (!Version.TryParse(txt.Trim(), out remoteVersion))
                    {
                        progress?.Report("Versão remota inválida.");
                        return UpdateOutcome.Error;
                    }
                }
            }
            catch (HttpRequestException)
            {
                progress?.Report("Sem conexão com a internet.");
                return UpdateOutcome.NoInternet;
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao ler versão: {ex.Message}");
                return UpdateOutcome.Error;
            }

            // 2) Compara
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (remoteVersion <= current)
            {
                progress?.Report($"Já está na última versão (v{current}).");
                return UpdateOutcome.AlreadyLatest;
            }

            // 3) Baixa ZIP
            progress?.Report("Baixando pacote…");
            var tmpZip = Path.Combine(Path.GetTempPath(), "debug_update.zip");
            try
            {
                using (var http2 = new HttpClient())
                using (var zipStream = await http2.GetStreamAsync(_zipUrl).ConfigureAwait(false))
                using (var fs = new FileStream(tmpZip, FileMode.Create, FileAccess.Write))
                    zipStream.CopyTo(fs);
            }
            catch (HttpRequestException)
            {
                progress?.Report("Sem conexão com a internet.");
                return UpdateOutcome.NoInternet;
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao baixar pacote: {ex.Message}");
                return UpdateOutcome.Error;
            }

            // 4) Extrai histórico
            progress?.Report("Instalando arquivos…");
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var histDir = Path.Combine(desktop, "OneEngRenamerUpdates", remoteVersion.ToString());
            try
            {
                if (Directory.Exists(histDir)) Directory.Delete(histDir, true);
                Directory.CreateDirectory(histDir);

                using (var archive = ZipFile.OpenRead(tmpZip))
                    foreach (var entry in archive.Entries)
                    {
                        var parts = entry.FullName
                            .Replace('/', Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar)
                            .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(p => p != "..");
                        var safeName = Path.Combine(parts.ToArray());
                        var dest = Path.Combine(histDir, safeName);

                        if (string.IsNullOrEmpty(entry.Name))
                            Directory.CreateDirectory(dest);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            entry.ExtractToFile(dest, overwrite: true);
                        }
                    }
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao extrair arquivos: {ex.Message}");
                return UpdateOutcome.Error;
            }

            // 5) Copia para C:\One\OrganizadorArquivosWPF
            try
            {
                var parent = Path.GetDirectoryName(InstallFolder);
                if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                if (Directory.Exists(InstallFolder)) Directory.Delete(InstallFolder, true);
                Directory.CreateDirectory(InstallFolder);

                foreach (var f in Directory.GetFiles(histDir, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(histDir.Length + 1);
                    var dest = Path.Combine(InstallFolder, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    System.IO.File.Copy(f, dest, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao instalar arquivos: {ex.Message}");
                return UpdateOutcome.Error;
            }

            // 6) Gera instalador offline (ZIP + BAT)
            try
            {
                var instRoot = Path.Combine(desktop, "OneEngRenamerInstallers", remoteVersion.ToString());
                if (Directory.Exists(instRoot)) Directory.Delete(instRoot, true);
                Directory.CreateDirectory(instRoot);

                var installerZip = Path.Combine(instRoot, $"OrganizadorArquivosWPF_{remoteVersion}.zip");
                System.IO.File.Copy(tmpZip, installerZip, overwrite: true);

                var bat = Path.Combine(instRoot, "Instalar.bat");
                System.IO.File.WriteAllLines(bat, new[]
                {
                    "@echo off",
                    "title Instalador OrganizadorArquivosWPF",
                    "echo Instalando em C:\\One\\OrganizadorArquivosWPF ...",
                    "set DEST=C:\\One\\OrganizadorArquivosWPF",
                    "if exist \"%DEST%\" rmdir /S /Q \"%DEST%\"",
                    "mkdir \"%DEST%\"",
                    "powershell -Command \"Expand-Archive -LiteralPath '%~dp0" +
                        Path.GetFileName(installerZip) +
                        "' -DestinationPath '%DEST%' -Force\"",
                    "echo.",
                    "echo Concluído!",
                    "pause"
                });
            }
            catch (Exception ex)
            {
                progress?.Report($"Erro ao gerar instalador: {ex.Message}");
                return UpdateOutcome.Error;
            }

            // 7) Cria atalho
            var lnk = Path.Combine(desktop, "OrganizadorArquivosWPF.lnk");
            var shell = new WshShell();
            var link = (IWshShortcut)shell.CreateShortcut(lnk);
            link.TargetPath = Path.Combine(InstallFolder, "OrganizadorArquivosWPF.exe");
            link.WorkingDirectory = InstallFolder;
            link.IconLocation = link.TargetPath;
            link.Save();

            // 8) Finaliza e reinicia
            progress?.Report($"Atualizado para v{remoteVersion}. Reiniciando…");
            await Task.Delay(1200);
            Process.Start(link.TargetPath);
            Application.Current.Shutdown();

            return UpdateOutcome.Updated;
        }
    }
}
