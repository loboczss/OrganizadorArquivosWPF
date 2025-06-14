using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrganizadorArquivosWPF.Models;

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

        /// <summary>
        /// Converte uma <see cref="JArray"/> em uma lista de <see cref="ClientRecord"/>.
        /// </summary>
        public static List<ClientRecord> ParseClientRecords(JArray array)
        {
            var list = new List<ClientRecord>();
            if (array == null) return list;

            foreach (JObject obj in array.OfType<JObject>())
            {
                string numos = obj.Value<string>("NUMOS") ?? string.Empty;
                string uf = obj.Value<string>("UF") ?? (numos.Length >= 2 ? numos.Substring(0, 2).ToUpperInvariant() : string.Empty);

                list.Add(new ClientRecord
                {
                    Rota = obj.Value<string>("ROTA") ?? string.Empty,
                    Tipo = (obj.Value<string>("TIPO") ?? string.Empty).ToUpperInvariant(),
                    NumOS = numos,
                    NumOcorrencia = obj.Value<string>("NUMOCORRENCIA") ?? string.Empty,
                    Obra = obj.Value<string>("OBRA") ?? string.Empty,
                    IdSigfi = obj.Value<string>("IDSIGFI") ?? string.Empty,
                    UC = obj.Value<string>("UC") ?? string.Empty,
                    NomeCliente = obj.Value<string>("NOMECLIENTE") ?? string.Empty,
                    Empresa = (obj.Value<string>("EMPRESA") ?? string.Empty).ToUpperInvariant(),
                    TipoDesigfi = (obj.Value<string>("TIPODESIGFI") ?? string.Empty).ToUpperInvariant(),
                    UF = uf,
                    NomeArquivoBase = string.Empty
                });
            }

            return list;
        }

        /// <summary>
        /// Lê o arquivo JSON em cache e converte em registros de cliente.
        /// </summary>
        public List<ClientRecord> LoadCachedRecords()
        {
            if (!File.Exists(OfflinePath))
                return new List<ClientRecord>();

            try
            {
                var json = File.ReadAllText(OfflinePath, Encoding.UTF8);
                var array = JArray.Parse(json);
                return ParseClientRecords(array);
            }
            catch
            {
                return new List<ClientRecord>();
            }
        }

        /// <summary>
        /// Obtém os dados de manutenção e converte para registros de cliente.
        /// </summary>
        public async Task<List<ClientRecord>> ObterClientRecordsAsync()
        {
            var arr = await ObterDadosAsync();
            return ParseClientRecords(arr);
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
