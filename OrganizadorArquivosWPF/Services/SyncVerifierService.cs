// Services/SyncVerifierService.cs
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Net;

namespace OrganizadorArquivosWPF.Services
{
    public static class SyncVerifierService
    {
        public static string ExcelPath
        {
            get
            {
                // 1) tenta OneDrive sync
                var oneDriveRoot = Environment.GetEnvironmentVariable("OneDriveCommercial")
                                 ?? Environment.GetEnvironmentVariable("OneDrive")
                                 ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var pathOneDrive = Path.Combine(
                    oneDriveRoot,
                    "OneEngenharia",
                    "Power BI",
                    "Fluxo de Dados - Power BI.xlsb"
                );
                if (File.Exists(pathOneDrive))
                    return pathOneDrive;

                // 2) fallback diretório padrão
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
                    "ONE Engenharia - Power BI",
                    "Fluxo de Dados - Power BI.xlsb"
                );
            }
        }

        /// <summary>
        /// Se o arquivo não existir e houver internet+email, dispara odopen://sync.
        /// Nunca abre diretório nem faz Process.Start com pasta local.
        /// </summary>
        public static void VerificarOuSincronizarArquivo()
        {
            var path = ExcelPath;
            if (File.Exists(path))
            {
                // Apenas fixa offline; não abre nada
                TentarFixarOffline(path);
                return;
            }

            // Só tenta sincronizar se tiver internet
            if (!TemInternet())
                return;

            var reg = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\OneDrive\Accounts\Business1",
                "UserEmail", null);
            var userEmail = reg as string;
            if (string.IsNullOrWhiteSpace(userEmail))
                return;

            // monta URI para sincronizar via OneDrive
            var siteId = "03b55b3a-5e43-430f-90db-687ed2c5b32f";
            var webId = "f404ba0e-0042-4854-807c-067f8b083162";
            var listId = "a121e09f-af17-4537-8069-dc247ac802ad";
            var siteUrl = "https://oneengenharia.sharepoint.com/sites/OneEngenharia";
            var webTitle = "ONE Engenharia";
            var listTitle = "Power BI";
            var uri = string.Format(
                "odopen://sync/?siteId={0}&webId={1}&webUrl={2}&listId={3}&userEmail={4}&webTitle={5}&listTitle={6}",
                siteId, webId, siteUrl, listId, userEmail, webTitle, listTitle
            );

            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch
            {
                // silencioso em caso de falha
            }
        }

        static bool TemInternet()
        {
            try
            {
                using (var wc = new WebClient())
                    wc.DownloadString("https://www.bing.com");
                return true;
            }
            catch { return false; }
        }

        static void TentarFixarOffline(string filePath)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "attrib",
                        Arguments = "-U \"" + filePath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.WaitForExit();
            }
            catch { /* ignorar */ }
        }
    }
}
