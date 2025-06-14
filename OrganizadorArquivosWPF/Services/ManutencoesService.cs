using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

        /// <summary>
        /// Retorna os dados de manutenção. Se houver internet, atualiza o arquivo offline.
        /// </summary>
        public async Task<JArray> ObterDadosAsync()
        {
            // texto em XML baixado da web
            string xml = null;

            if (TemInternet())
            {
                try
                {
                    using (var http = new HttpClient())
                    {
                        xml = await http.GetStringAsync(ApiUrl);
                        // valida e converte para JSON antes de salvar
                        var array = ConverterXmlParaArray(xml);
                        Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                        File.WriteAllText(OfflinePath, array.ToString(), Encoding.UTF8);
                        return array;
                    }
                }
                catch
                {
                    xml = null; // falha na conexão ou XML inválido
                }
            }

            if (xml == null)
            {
                if (!File.Exists(OfflinePath))
                    return new JArray();

                try
                {
                    var offlineJson = File.ReadAllText(OfflinePath, Encoding.UTF8);
                    return JArray.Parse(offlineJson);
                }
                catch
                {
                    return new JArray();
                }
            }

            // Se chegamos aqui, temos XML baixado mas não salvo (por erro no salvamento)
            try
            {
                return ConverterXmlParaArray(xml);
            }
            catch
            {
                return new JArray();
            }
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
