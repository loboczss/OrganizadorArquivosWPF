// ManutencoesService.cs  —  compatível com C# 7.3 e .NET Framework 4.x
// -------------------------------------------------------------------
// NuGet necessários:
//   Install-Package Dropbox.Api
//   Install-Package Newtonsoft.Json

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Dropbox.Api;
using Dropbox.Api.Files;
using Newtonsoft.Json.Linq;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class ManutencoesService
    {
        // === CREDENCIAIS DO DROPBOX ===========================================
        private const string DropboxToken =
@"sl.u.AFxIVnN-nbzNi02QTU_5bC7igCwpHNfuWz5kr6Jg7LaivptkwnVh5-hhSUshCFzARHNp6oE4xPSTvSMn_jhMJI5N4GDcNo-I3gp1b9UykecYwiYbBOWhzqjPb7boLt2SrUHql_jHYmnO2xz4Ofe4L2se-y5ySVHfkspq1v7nyJ2th-98tdROXGu4A0BV1MFxgbdv1rSaQO8_brB0Ti4HJuyWyhhrup0fqe0kDAhGmzH3WVlvuPsxejfudjkqV6KkEBlx3A6Ptt19TIOqVwu5SQgHK6g8AOtONwmxS4gNSaewhbgqzfXPaTfeXKzwYlS0abg76L53q8tD-gC0BdvMAeKsOBRlO5x_WC0NF3Z95shCqWNBTbuiQSqfKCKv9k120dfAyDmBTCSfAPwAIeGmc1j_Lenqmw6WY1a5P4o0Zjp7AEubj3UCJ45dcollwNwJ08HnQ2wIyu5Trq70ZCshYKMV5UF79Sxu8vx6GmYxWFTIl5vfOkJf5kfx_k8vID0K1M3enXNVLnTA2MF8Vq2mxmCTA4taSqWgwi50wO3xsM2lXRN9dIKJcFbZWz8m_msmmYdK9p6ZdWkDifVlyF4cHW5-SeVgdk5h1W_xxOkFMpIZDkC_ZI7UVdCieozRLZzWXnJToEKm4V-abgIY5SC-G-VeWAxJBM4ITgBCZP_f6698Xz1BEvoK33lmbG2KRI_lYEUJVa1iPZVGMPjsZMRNOMH9UN6LqldkTvfq4xZeSHF19eZ-puOgwgTcIIT8SaUgEHFFpL6ZaGe23mDMi0Gnn1U-fHG8836gqUr7gWyk22rVDCGTOS9QjqvVrQjk2kAs7FDWW_C9j17D6QEcNBDBnPwDpyhIHqprsn2pnH2xZlyC50UP42orUx-lVs9yPoYtjoSnVKPnVaOP_vnuJbV_AH8jGPKm2cxXhKCj3YIx1ypmRs9avmf4mHhdX2ImY5D-uZTUYB4PCzgqz9zFzBPxhfu5BBkASSrC616UC-0PUsP_a9lpmashuIXE6PuxcOupSbTlxIfmFCwJ5jCMvPLlVKpm4sLVB89FmQiPyQcWSSjSQ-uegJe-kRJjXCYfpte3yyVWegGn3keCz9aVCXqZc5LSK73FSHOiP3bzNGyxUFKi5y_ZKjd1Cu9s5Tzt4d8QieWfjj2Ha3Ixf4Ku2XNB6o32f-RfU2v-9C7YJUM4y2OVaKRjTHHrpdjIVE6GXdsvDqyzfjoV0Rl4_ayBzlCCwZVubc0Qw4hHvYO8a1x5Myo8yC99fC5rntSd0TkWFFRSUk8oHs533QJKXint3yBaNxTNR9WTFyoDIYayeJ5mE9_b8g";
        // “App folder” ⇒ raiz = "", se quisesse subpasta: "/MinhaSubpasta"
        private const string DropboxFolder = "";

        // ======================================================================

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "manutencoes.json");

        private JArray _dados = new JArray();
        private Timer _timer;
        private IProgress<int> _timerProgress;

        public event Action<DateTime, bool> UpdateCompleted;
        public JArray Dados => _dados;

        public static string CacheFilePath => OfflinePath;

        public static DateTime? GetCacheTimestamp()
        {
            try
            {
                return File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null;
            }
            catch { return null; }
        }

        // --------------------- DOWNLOAD / CACHE -------------------------------
        public async Task<JArray> ObterDadosAsync(IProgress<int> progress = null)
        {
            progress?.Report(0);
            bool fromInternet = false;

            if (TemInternet())
            {
                try
                {
                    string json = await BaixarUltimoJsonDropboxAsync(progress);
                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, json, Encoding.UTF8);

                    _dados = JArray.Parse(json);
                    fromInternet = true;
                    progress?.Report(100);
                }
                catch
                {
                    // se falhar, usa cache
                }
            }

            if (!fromInternet)
            {
                try
                {
                    _dados = File.Exists(OfflinePath)
                        ? JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8))
                        : new JArray();
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

        // -------------------------- PARSE -------------------------------------
        public static List<ClientRecord> ParseClientRecords(JArray array)
        {
            var list = new List<ClientRecord>();
            if (array == null) return list;

            foreach (JObject obj in array.OfType<JObject>())
            {
                string numos = obj.Value<string>("NUMOS") ?? string.Empty;
                string uf = obj.Value<string>("UF") ??
                           (numos.Length >= 2 ? numos.Substring(0, 2).ToUpperInvariant() : string.Empty);

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

        public List<ClientRecord> LoadCachedRecords()
        {
            if (!File.Exists(OfflinePath)) return new List<ClientRecord>();
            try { return ParseClientRecords(JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8))); }
            catch { return new List<ClientRecord>(); }
        }

        public async Task<List<ClientRecord>> ObterClientRecordsAsync(IProgress<int> p = null)
        {
            var arr = await ObterDadosAsync(p);
            return ParseClientRecords(arr);
        }

        // ---------------------  DROPBOX HELPERS --------------------------------
        private async Task<string> BaixarUltimoJsonDropboxAsync(IProgress<int> progress)
        {
            if (string.IsNullOrWhiteSpace(DropboxToken))
                throw new InvalidOperationException("DropboxToken não definido.");

            Console.WriteLine($">>> Conectando ao Dropbox na pasta '{DropboxFolder}'...");
            using (var dbx = new DropboxClient(DropboxToken))
            {
                ListFolderResult page;
                try
                {
                    page = await dbx.Files.ListFolderAsync(DropboxFolder);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha ao listar a pasta: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                Console.WriteLine($">>> Total de entradas retornadas: {page.Entries.Count}");
                foreach (var entry in page.Entries)
                {
                    bool isFile = entry.IsFile;
                    string name = entry.Name;
                    string mod = isFile
                        ? ((FileMetadata)entry).ServerModified.ToString("s")
                        : "-";
                    Console.WriteLine($"    • {name} (IsFile: {isFile}, ServerModified: {mod})");
                }

                var jsonFiles = page.Entries
                    .Where(e => e.IsFile && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($">>> Arquivos .json encontrados: {jsonFiles.Count}");
                if (!jsonFiles.Any())
                    throw new FileNotFoundException("Nenhum arquivo .json encontrado no Dropbox.");

                var escolhido = jsonFiles
                    .OrderByDescending(e => ((FileMetadata)e).ServerModified)
                    .First();

                Console.WriteLine($">>> Escolhido para download: {escolhido.Name} ({((FileMetadata)escolhido).ServerModified:dd/MM/yyyy HH:mm:ss})");
                progress?.Report(-1);

                try
                {
                    using (var resp = await dbx.Files.DownloadAsync(escolhido.PathLower))
                    {
                        return await resp.GetContentAsStringAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha ao baixar '{escolhido.Name}': {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
        }



        private static bool TemInternet()
        {
            try
            {
                using (var wc = new WebClient())
                    wc.DownloadString("https://www.google.com/generate_204");
                return true;
            }
            catch { return false; }
        }

        // ------------------ ATUALIZAÇÃO AUTOMÁTICA ----------------------------
        public void StartAutoUpdate(TimeSpan interval, IProgress<int> p = null)
        {
            if (_timer != null) return;

            _timerProgress = p;
            _timer = new Timer(interval.TotalMilliseconds) { AutoReset = true, Enabled = true };
            _timer.Elapsed += async (s, e) =>
            {
                _timer.Enabled = false;
                try { await ObterDadosAsync(_timerProgress); }
                catch { }
                finally { _timer.Enabled = true; }
            };
        }

        public void StopAutoUpdate()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            _timerProgress = null;
        }
    }
}
