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
using System.Timers;


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
        private Timer _timer;
        private IProgress<int> _timerProgress;

        /// <summary>
        /// Disparado ao concluir a obtenção de dados. O bool indica se
        /// os dados vieram da internet (true) ou do cache offline (false).
        /// </summary>
        public event Action<DateTime, bool> UpdateCompleted;

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
        public async Task<JArray> ObterDadosAsync(IProgress<int> progress = null)
        {
            progress?.Report(0);
            string xml = null;
            string json = null;
            bool fromInternet = false;

            if (TemInternet())
            {
                try
                {
                    using (var http = new HttpClient())
                    using (var response = await http.GetAsync(ApiUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var total = response.Content.Headers.ContentLength ?? -1L;
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var ms = new MemoryStream())
                        {
                            var buffer = new byte[8192];
                            long readTotal = 0;
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                ms.Write(buffer, 0, read);
                                readTotal += read;
                                if (total > 0)
                                    progress?.Report((int)(readTotal * 100 / total));
                            }

                            xml = Encoding.UTF8.GetString(ms.ToArray());
                        }

                        var array = ConverterXmlParaArray(xml);
                        json = array.ToString();

                        Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                        File.WriteAllText(OfflinePath, json, Encoding.UTF8);

                        _dados = array;
                        fromInternet = true;
                        progress?.Report(100);
                    }
                }
                catch
                {
                    // continua e tenta usar o cache offline
                }
            }

            if (!fromInternet)
            {
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
                    progress?.Report(100);
                }
                catch
                {
                    _dados = new JArray();
                    progress?.Report(100);
                }
            }

            UpdateCompleted?.Invoke(DateTime.Now, fromInternet);
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
        public async Task<List<ClientRecord>> ObterClientRecordsAsync(IProgress<int> progress = null)
        {
            var arr = await ObterDadosAsync(progress);
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

        /// <summary>
        /// Inicia a atualização automática dos dados em intervalo fixo.
        /// </summary>
        public void StartAutoUpdate(TimeSpan interval, IProgress<int> progress = null)
        {
            if (_timer != null)
                return;

            _timerProgress = progress;

            _timer = new Timer(interval.TotalMilliseconds)
            {
                AutoReset = true,
                Enabled = true
            };
            _timer.Elapsed += async (s, e) =>
            {
                _timer.Enabled = false;
                try { await ObterDadosAsync(_timerProgress); }
                catch { /* ignorar erros de download */ }
                finally { _timer.Enabled = true; }
            };
        }

        /// <summary>
        /// Para a atualização automática.
        /// </summary>
        public void StopAutoUpdate()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
                _timerProgress = null;
            }
        }
    }
}
