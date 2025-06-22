using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;

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
        private LoggerService _log => LoggerService.Instance;

        private async Task<string> ObterAccessTokenAsync()
        {
            var tokenService = new GerarTokenService(AppKey, AppSecret, RefreshToken);
            return await tokenService.ObterAccessTokenAsync();
        }

        /// <summary>
        /// Compacta a pasta indicada e envia para o Dropbox.
        /// </summary>
        public async Task EnviarBackupAsync(string pasta)
        {
            if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta))
                return;

            string nomeZip = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
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
    }
}
