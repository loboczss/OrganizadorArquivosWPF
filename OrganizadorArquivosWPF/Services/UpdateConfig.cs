using System;
using System.IO;
using System.Text.RegularExpressions;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Carrega VersaoUrl e ZipUrl de update.json (opcional),
    /// ou usa os links raw do Dropbox por padrão.
    /// </summary>
    public sealed class UpdateConfig
    {
        private static readonly string _cfgPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.json");

        public string VersaoUrl { get; }
        public string ZipUrl { get; }

        private UpdateConfig(string versao, string zip)
        {
            VersaoUrl = versao;
            ZipUrl = zip;
        }

        public static UpdateConfig Load()
        {
            // raw do seu versao.txt no Dropbox
            var versaoUrl = "https://dl.dropboxusercontent.com/s/kxa0qmj65891joipxl9yp/versao.txt";
            // raw do seu Debug.zip no Dropbox
            var zipUrl = "https://dl.dropboxusercontent.com/s/zp30fygytil2opnv5q4ah/Debug.zip?dl=1";

            if (!File.Exists(_cfgPath))
                return new UpdateConfig(versaoUrl, zipUrl);

            try
            {
                var json = File.ReadAllText(_cfgPath);
                var rxV = new Regex("\"VersaoUrl\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                var rxZ = new Regex("\"ZipUrl\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);

                var mV = rxV.Match(json);
                var mZ = rxZ.Match(json);
                if (mV.Success && !string.IsNullOrWhiteSpace(mV.Groups[1].Value))
                    versaoUrl = mV.Groups[1].Value.Trim();
                if (mZ.Success && !string.IsNullOrWhiteSpace(mZ.Groups[1].Value))
                    zipUrl = mZ.Groups[1].Value.Trim();
            }
            catch
            {
                // mantém os padrões em caso de erro
            }

            return new UpdateConfig(versaoUrl, zipUrl);
        }
    }
}
