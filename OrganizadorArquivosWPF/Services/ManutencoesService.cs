using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using Dropbox.Api;
using Dropbox.Api.Files;
using Newtonsoft.Json.Linq;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class ManutencoesService
    {
        // === CREDENCIAIS =====================================================
        private const string AppKey = "523wx0kknv1xj4h";
        private const string AppSecret = "mcw1pgyfnx3hqbh";
        private const string RefreshToken = "7-G0mKVNMRQAAAAAAAAAASvMELHHomwEkmVR24HK-XLEFvNMpNUp7Py0hxUnjic_";
        private const string DropboxFolder = ""; // raiz do Dropbox
        // ======================================================================

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "manutencoes.json");

        private readonly string[] _padroesArquivo =
        {
            "Manutencao_AC2023",
            "Manutencao_AC2024",
            "Manutencao_AC2025",
            "Manutencao_MT"
        };

        private JArray _dados = new JArray();
        private Timer _timer;
        private IProgress<int> _timerProgress;
        private bool _executandoAtualizacao;

        public event Action<DateTime, bool> UpdateCompleted;
        public JArray Dados => _dados;
        public static string CacheFilePath => OfflinePath;

        public static DateTime? GetCacheTimestamp()
        {
            try { return File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null; }
            catch { return null; }
        }

        // === TOKEN DINÂMICO ==================================================
        private async Task<string> ObterAccessTokenAsync()
        {
            var tokenService = new GerarTokenService(AppKey, AppSecret, RefreshToken);
            return await tokenService.ObterAccessTokenAsync().ConfigureAwait(false);
        }

        // ----------------------------------------------------------------------
        // log visual (thread-safe)
        private readonly Dictionary<string, int> _linhasDinamicas = new Dictionary<string, int>();
        private readonly List<string> _linhasLog = new List<string>();

        private void CriarOuAtualizarLinha(string chave, string texto)
        {
            Application.Current.Dispatcher.InvokeAsync(delegate
            {
                int idx;
                if (_linhasDinamicas.TryGetValue(chave, out idx) && idx < _linhasLog.Count)
                {
                    _linhasLog[idx] = texto;
                }
                else
                {
                    _linhasLog.Add(texto);
                    _linhasDinamicas[chave] = _linhasLog.Count - 1;
                }
                // dispare aqui event/binding para UI observar _linhasLog
            });
        }

        // ----------------------------------------------------------------------
        // Baixa em streaming diretamente via Dropbox SDK, reportando progresso
        private async Task<string> BaixarDoDropboxComProgressoAsync(
            DropboxClient dbx,
            string pathLower,
            string identificador,
            string grupo)
        {
            // 1) Conectar e iniciar
            CriarOuAtualizarLinha(identificador, $"[{identificador}] Conectando ao Dropbox...");
            var download = await dbx.Files.DownloadAsync(pathLower).ConfigureAwait(false);
            // Ajuste: Size retorna ulong, não long?
            ulong totalBytes = download.Response.Size;
            bool canReportPercent = totalBytes > 0;

            CriarOuAtualizarLinha(identificador, $"[{identificador}] Iniciando download...");
            using (var stream = await download.GetContentAsStreamAsync().ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[1024 * 1024];
                long totalRead = 0;
                var sw = Stopwatch.StartNew();

                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (bytesRead == 0) break;

                    ms.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    double speedKBs = (totalRead / sw.Elapsed.TotalSeconds) / 1024;
                    string texto;
                    if (canReportPercent)
                    {
                        double pct = (totalRead / (double)totalBytes) * 100;
                        texto = $"[{identificador}] {pct:0.0}% • {speedKBs:0.0} KB/s";
                    }
                    else
                    {
                        texto = $"[{identificador}] {totalRead / 1024} KB • {speedKBs:0.0} KB/s";
                    }
                    CriarOuAtualizarLinha(identificador, texto);
                }

                sw.Stop();
                string finalMsg = $"Download concluído • {totalRead / 1024} KB em {sw.Elapsed.TotalSeconds:0.0}s";
                CriarOuAtualizarLinha(identificador, $"[{identificador}] {finalMsg}");
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // ====================== MÉTODO PRINCIPAL ============================
        public async Task<JArray> ObterDadosAsync(IProgress<int> progress = null)
        {
            progress?.Report(0);
            bool fromInternet = false;

            if (TemInternet())
            {
                try
                {
                    var arquivos = await BaixarUltimosArquivosManutencaoAsync(progress).ConfigureAwait(false);
                    if (arquivos.Count == 0)
                        throw new Exception("⚠️ Nenhum dos 4 arquivos foi encontrado.");

                    _dados = CombinarArquivosJson(arquivos);
                    if (_dados.Count == 0)
                        throw new Exception("⚠️ Não foi possível extrair registros.");

                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, _dados.ToString(), Encoding.UTF8);

                    fromInternet = true;
                    progress?.Report(100);
                }
                catch
                {
                    // falha momentânea, vai usar cache
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

        private async Task<Dictionary<string, string>> BaixarUltimosArquivosManutencaoAsync(
            IProgress<int> progress, int maxTentativas = 3)
        {
            for (int i = 1; i <= maxTentativas; i++)
            {
                try { return await BaixarArquivosDropboxInternoAsync(progress).ConfigureAwait(false); }
                catch when (i < maxTentativas)
                {
                    await Task.Delay(1000 * i).ConfigureAwait(false);
                }
            }
            return new Dictionary<string, string>();
        }

        private async Task<Dictionary<string, string>> BaixarArquivosDropboxInternoAsync(IProgress<int> progress)
        {
            string token = await ObterAccessTokenAsync().ConfigureAwait(false);
            using (var dbx = new DropboxClient(token))
            {
                var page = await dbx.Files.ListFolderAsync(DropboxFolder).ConfigureAwait(false);
                var files = page.Entries
                    .Where(e => e.IsFile && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Cast<FileMetadata>()
                    .ToList();

                var resultados = new Dictionary<string, string>();
                int total = _padroesArquivo.Length, done = 0;

                foreach (var padrao in _padroesArquivo)
                {
                    var meta = files
                        .Where(f => f.Name.IndexOf(padrao, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(f => f.ServerModified)
                        .FirstOrDefault();
                    if (meta == null)
                    {
                        progress?.Report(++done * 100 / total);
                        continue;
                    }

                    string conteudo = await BaixarDoDropboxComProgressoAsync(
                        dbx,
                        meta.PathLower,
                        padrao,
                        "Dropbox").ConfigureAwait(false);

                    if (conteudo != null)
                        resultados[padrao] = conteudo;

                    progress?.Report(++done * 100 / total);
                }

                progress?.Report(100);
                return resultados;
            }
        }

        private static JArray CombinarArquivosJson(Dictionary<string, string> arquivos)
        {
            var combinado = new JArray();
            foreach (var kv in arquivos)
            {
                try
                {
                    string c = kv.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(c)) continue;

                    var token = JToken.Parse(c);
                    JArray arr = token.Type == JTokenType.Array
                        ? (JArray)token
                        : token.Children<JProperty>().FirstOrDefault()?.Value as JArray;
                    if (arr == null) continue;

                    foreach (var item in arr) combinado.Add(item);
                }
                catch { }
            }
            return combinado;
        }

        public static List<ClientRecord> ParseClientRecords(JArray array)
        {
            var list = new List<ClientRecord>();
            if (array == null) return list;

            foreach (JObject obj in array.OfType<JObject>())
            {
                string numos = obj.Value<string>("NUMOS") ?? string.Empty;
                string uf = obj.Value<string>("UF")
                    ?? (numos.Length >= 2 ? numos.Substring(0, 2).ToUpperInvariant() : string.Empty);

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

        public async Task<List<ClientRecord>> ObterClientRecordsAsync(IProgress<int> p = null)
        {
            var arr = await ObterDadosAsync(p).ConfigureAwait(false);
            return ParseClientRecords(arr);
        }

        private static bool TemInternet()
        {
            try { using (var wc = new WebClient()) wc.DownloadString("https://www.google.com/generate_204"); return true; }
            catch { return false; }
        }

        public void StartAutoUpdate(TimeSpan interval, IProgress<int> p = null)
        {
            if (_timer != null) return;
            _timerProgress = p;
            _timer = new Timer(interval.TotalMilliseconds) { AutoReset = true, Enabled = true };
            _timer.Elapsed += async (s, e) =>
            {
                if (_executandoAtualizacao) return;
                _executandoAtualizacao = true;
                try { await ObterDadosAsync(_timerProgress).ConfigureAwait(false); }
                catch { }
                finally { _executandoAtualizacao = false; }
            };
        }

        public void StopAutoUpdate()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            _timerProgress = null;
            _executandoAtualizacao = false;
        }
    }
}
