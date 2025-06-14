using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using OrganizadorArquivosWPF.Models;
using System.Xml.Linq;


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

        private JArray _dados = new JArray();

        /// <summary>
        /// Último conjunto de dados obtido.
        /// </summary>
        public JArray Dados => _dados;

        /// Caminho para o arquivo em cache contendo o JSON baixado.
        /// </summary>
        public static string CacheFilePath => OfflinePath;

        /// <summary>
        /// Retorna a data de escrita do arquivo de cache ou null se inexistente.
        /// </summary>
        public static DateTime? GetCacheTimestamp()
        {
            try
            {
                return File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retorna os dados de manutenção. Se houver internet, atualiza o arquivo offline.
        /// </summary>
        public async Task<JArray> ObterDadosAsync()
        {
            string xml = null;
            string json = null;

            if (TemInternet())
            {
                try
                {
                    using (var http = new HttpClient())
                    {
                        xml = await http.GetStringAsync(ApiUrl);
                        var array = ConverterXmlParaArray(xml);
                        json = array.ToString();

                        Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                        File.WriteAllText(OfflinePath, json, Encoding.UTF8);

                        _dados = array;
                        return _dados;
                    }
                }
                catch
                {
                    // continua e tenta usar o cache offline
                }
            }

            try
            {
                if (File.Exists(OfflinePath))
                {
                    json = File.ReadAllText(OfflinePath, Encoding.UTF8);
                    _dados = JArray.Parse(json);
                }
                else
                {
                    _dados = new JArray();
                }
            }
            catch
            {
                _dados = new JArray();
            }

            return _dados;
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

        private static JArray ConverterXmlParaArray(string xml)
        {
            var arr = new JArray();
            try
            {
                var doc = XDocument.Parse(xml);
                foreach (var post in doc.Descendants("post"))
                {
                    var obj = new JObject();
                    foreach (var el in post.Elements())
                        obj[el.Name.LocalName] = el.Value;
                    arr.Add(obj);
                }
            }
            catch
            {
                // retorna array vazio em caso de XML malformado
            }
            return arr;
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
