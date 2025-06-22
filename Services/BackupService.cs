using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Serviço simples para enviar backups para o Dropbox.
    /// </summary>
    public class BackupService
    {
        private const string AppKey = "523wx0kknv1xj4h";
        private const string AppSecret = "mcw1pgyfnx3hqbh";
        private const string RefreshToken = "7-G0mKVNMRQAAAAAAAAAASvMELHHomwEkmVR24HK-XLEFvNMpNUp7Py0hxUnjic_";
        private const string DropboxFolder = "/backups";
        private static readonly string BackupHistoryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "uploaded_backups.txt");
        private LoggerService _log => LoggerService.Instance;

        private static HashSet<string> CarregarHistorico(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new HashSet<string>(File.ReadAllLines(path), StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SalvarHistorico(string path, IEnumerable<string> itens)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, itens);
            }
            catch { }
        }

        private static string ExtrairOs(string pasta)
        {
            try
            {
                var dir = Path.GetFileName(pasta);
                return dir?.Split('_')[0];
            }
            catch { return null; }
        }

        private async Task<string> ObterAccessTokenAsync()
        {
            var tokenService = new GerarTokenService(AppKey, AppSecret, RefreshToken);
            return await tokenService.ObterAccessTokenAsync();
        }

        /// <summary>
        /// Compacta a pasta indicada e envia para o Dropbox. O backup de uma
        /// determinada O.S. é enviado apenas uma vez para evitar duplicados.
        /// </summary>
        public async Task EnviarBackupAsync(string pasta, string numOs = null)
        {
            if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                return;

            numOs ??= ExtrairOs(pasta);
            if (string.IsNullOrWhiteSpace(numOs))
                return;

            string histFile = BackupHistoryFile;
            var enviados = CarregarHistorico(histFile);
            if (enviados.Contains(numOs))
            {
                _log.Info($"Backup já enviado para a O.S {numOs}. Pulando envio.");
                return;
            }

            string nomeZip = Path.GetFileName(
                Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar,
                                              Path.AltDirectorySeparatorChar)) + ".zip";
            string zipLocal = Path.Combine(Path.GetTempPath(), nomeZip);

            try
            {
                if (File.Exists(zipLocal))
                    File.Delete(zipLocal);

                ZipFile.CreateFromDirectory(pasta, zipLocal, CompressionLevel.Optimal, false);

                string token = await ObterAccessTokenAsync();
                using (var dbx = new DropboxClient(token))
                using (var fs = File.OpenRead(zipLocal))
                {
                    string dropboxPath = DropboxFolder + "/" + nomeZip;
                    await dbx.Files.UploadAsync(dropboxPath, WriteMode.Overwrite.Instance, body: fs);
                    enviados.Add(numOs);
                    SalvarHistorico(histFile, enviados);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Falha ao enviar backup: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(zipLocal)) File.Delete(zipLocal); } catch { }
            }
        }

        /// <summary>
        /// Executa o mesmo processo do <see cref="RenamerService"/>, salvando o
        /// resultado localmente e opcionalmente enviando para o Dropbox.
        /// </summary>
        public async Task ProcessarBackupAsync(
            string pastaOrigem,
            ClientRecord registro,
            string sistema,
            string tipoSistema,
            string destinoLocal,
            bool enviarParaNuvem,
            string nomeFuncionario,
            string matriculaFuncionario,
            IProgress<int> progress = null)
        {
            if (string.IsNullOrWhiteSpace(pastaOrigem) || !Directory.Exists(pastaOrigem))
                throw new DirectoryNotFoundException("Pasta de origem inválida para backup.");

            var logFileService = new LogFileService();
            var renamer = new RenamerService(_log, logFileService);
            await renamer.RenameAsync(
                pastaOrigem,
                registro,
                sistema,
                tipoSistema,
                true,
                destinoLocal,
                nomeFuncionario,
                matriculaFuncionario,
                progress);

            if (enviarParaNuvem)
                await EnviarBackupAsync(renamer.LastDestination, registro?.NumOS);
        }
    }
}
