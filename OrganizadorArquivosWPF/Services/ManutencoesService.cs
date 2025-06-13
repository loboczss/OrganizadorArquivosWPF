using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Obtém os dados de manutenção a partir da internet e mantém uma cópia offline em JSON.
    /// </summary>
    public class ManutencoesService
    {
        private const string ApiUrl = "http://wseletrotransol.service4.sinapi.com.br/dadosbanco.php?action=manutencoes";

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "manutencoes.json");

        /// <summary>
        /// Retorna os dados de manutenção. Se houver internet, atualiza o arquivo offline.
        /// </summary>
        public async Task<JArray> ObterDadosAsync()
        {
            string json = null;

            if (TemInternet())
            {
                try
                {
                    using (var http = new HttpClient())
                    {
                        json = await http.GetStringAsync(ApiUrl);
                        Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                        File.WriteAllText(OfflinePath, json, Encoding.UTF8);
                    }
                }
                catch
                {
                    json = null; // falha na conexão, tenta local
                }
            }

            if (json == null)
            {
                if (!File.Exists(OfflinePath))
                    return new JArray();

                json = File.ReadAllText(OfflinePath, Encoding.UTF8);
            }

            try
            {
                return JArray.Parse(json);
            }
            catch
            {
                return new JArray();
            }
        }

        private static bool TemInternet()
        {
            try
            {
                using (var client = new WebClient())
                    client.DownloadString("https://www.bing.com");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
