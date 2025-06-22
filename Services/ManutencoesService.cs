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
using Newtonsoft.Json;
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

        private const string DropboxFolder = ""; // Pasta raiz
        // =====================================================================

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
        private List<ClientRecord> _records = new List<ClientRecord>();
        private Timer _timer;
        private IProgress<int> _timerProgress;
        private bool _executandoAtualizacao;
        private LoggerService _log => LoggerService.Instance;

        public event Action<DateTime, bool> UpdateCompleted;
        public JArray Dados => _dados;
        public IReadOnlyList<ClientRecord> Records => _records;
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
            return await tokenService.ObterAccessTokenAsync();
        }

        // ======================  MÉTODO PRINCIPAL  ===========================
        public async Task<JArray> ObterDadosAsync(IProgress<int> progress = null)
        {
            progress?.Report(0);
            bool fromInternet = false;

            if (TemInternet())
            {
                try
                {
                    var arquivos = await BaixarUltimosArquivosManutencaoAsync(progress);

                    if (arquivos.Count == 0)
                        throw new Exception("⚠️ Nenhum dos 4 arquivos foi encontrado.");

                    _dados = CombinarArquivosJson(arquivos);

                    if (_dados.Count == 0)
                        throw new Exception("⚠️ Não foi possível extrair registros dos arquivos baixados.");

                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, _dados.ToString(Formatting.None), Encoding.UTF8);

                    fromInternet = true;
                    progress?.Report(100);

                    _log.Info($"✅ Dados carregados do Dropbox ({_dados.Count} registros).");
                }
                catch (Exception ex)
                {
                    _log.Info($"[ERRO] Download/merge: {ex.Message}");
                    _log.Info("⚠️ Tentando usar o cache local…");
                }
            }

            if (!fromInternet)
            {
                try
                {
                    _dados = File.Exists(OfflinePath)
                        ? JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8))
                        : new JArray();

                    _log.Info($"✅ Cache carregado: {_dados.Count} registros.");
                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _log.Info($"[ERRO] Falha ao ler cache: {ex.Message}");
                    _dados = new JArray();
                    progress?.Report(100);
                }
            }

            _records = ParseClientRecords(_dados);
            UpdateCompleted?.Invoke(DateTime.Now, fromInternet);
            return _dados;
        }

        // =====================  DOWNLOAD DROPBOX  ============================
        private async Task<Dictionary<string, string>> BaixarUltimosArquivosManutencaoAsync(
            IProgress<int> progress, int maxTentativas = 3)
        {
            for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
            {
                try { return await BaixarArquivosDropboxInternoAsync(progress); }
                catch (Exception ex) when (tentativa < maxTentativas)
                {
                    _log.Info($"[ERRO] Tentativa {tentativa} falhou ({ex.Message}). Retentando…");
                    await Task.Delay(1000 * tentativa);
                }
            }
            return new Dictionary<string, string>();
        }

        private async Task<Dictionary<string, string>> BaixarArquivosDropboxInternoAsync(IProgress<int> progress)
        {
            _log.Info($">>> Conectando ao Dropbox…");
            string accessToken = await ObterAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
                throw new Exception("❌ Falha ao gerar Access Token.");

            using (var dbx = new DropboxClient(accessToken))
            {
                ListFolderResult page;
                try { page = await dbx.Files.ListFolderAsync(DropboxFolder); }
                catch (Exception ex) { throw new Exception($"Falha ao listar pasta: {ex.Message}", ex); }

                var jsonFiles = page.Entries
                    .Where(e => e.IsFile && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Cast<FileMetadata>()
                    .ToList();

                if (!jsonFiles.Any())
                    throw new FileNotFoundException("Nenhum .json encontrado no Dropbox.");

                var resultados = new Dictionary<string, string>();
                int totalEtapas = _padroesArquivo.Length;
                int concluidos = 0;

                foreach (string padrao in _padroesArquivo)
                {
                    var arquivosPadrao = jsonFiles
                        .Where(f => f.Name.IndexOf(padrao, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(f => f.ServerModified)
                        .ToList();

                    if (!arquivosPadrao.Any())
                    {
                        _log.Info($"⚠️ Não encontrou arquivo para '{padrao}'.");
                        concluidos++;
                        progress?.Report(concluidos * 100 / totalEtapas);
                        continue;
                    }

                    var meta = arquivosPadrao.First();
                    _log.Info($">>> Baixando {meta.Name} ({meta.ServerModified:dd/MM/yyyy HH:mm:ss})");

                    try
                    {
                        using (var resp = await dbx.Files.DownloadAsync(meta.PathLower))
                        {
                            resultados[padrao] = await resp.GetContentAsStringAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Info($"[ERRO] Falha ao baixar '{meta.Name}': {ex.Message}");
                    }
                    finally
                    {
                        concluidos++;
                        progress?.Report(concluidos * 100 / totalEtapas);
                    }
                }

                // Garante 100 % mesmo se faltou arquivo
                progress?.Report(100);
                return resultados;
            }
        }

        // ======================  MERGE DE ARQUIVOS  ===========================
        private static JArray CombinarArquivosJson(Dictionary<string, string> arquivos)
        {
            var combinado = new JArray();

            foreach (var kv in arquivos)
            {
                try
                {
                    string conteudo = kv.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(conteudo)) continue;

                    var token = JToken.Parse(conteudo);

                    JArray arr =
                        token.Type == JTokenType.Array
                            ? (JArray)token
                            : token.Children<JProperty>().FirstOrDefault()?.Value as JArray;

                    if (arr == null || arr.Count == 0)
                    {
                        _log.Info($"⚠️ Arquivo '{kv.Key}' não continha array válido.");
                        continue;
                    }

                    foreach (var item in arr)
                        combinado.Add(item);
                }
                catch (JsonException jex)
                {
                    _log.Info($"[ERRO] JSON inválido em '{kv.Key}': {jex.Message}");
                }
                catch (Exception ex)
                {
                    _log.Info($"[ERRO] Falha ao processar '{kv.Key}': {ex.Message}");
                }
            }

            _log.Info($"🔗 Merge concluído: {combinado.Count} itens combinados.");
            return combinado;
        }

        // ======================  PARSE P/ OBJETO  =============================
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
            try
            {
                var list = ParseClientRecords(JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8)));
                _records = new List<ClientRecord>(list);
                return list;
            }
            catch
            {
                return new List<ClientRecord>();
            }
        }

        public async Task<List<ClientRecord>> ObterClientRecordsAsync(IProgress<int> p = null)
        {
            await ObterDadosAsync(p);
            return new List<ClientRecord>(_records);
        }

        // ======================  UTILIDADES  ==================================
        private static bool TemInternet()
        {
            try
            {
                using (var wc = new WebClient())
                    wc.DownloadString("https://www.google.com/generate_204");
                return true;
            }
            catch
            {
                try
                {
                    using (var wc = new WebClient())
                        wc.DownloadString("https://www.bing.com");
                    return true;
                }
                catch { return false; }
            }
        }

        // ===================  ATUALIZAÇÃO AUTOMÁTICA  =========================
        public void StartAutoUpdate(TimeSpan interval, IProgress<int> p = null)
        {
            if (_timer != null) return;

            _timerProgress = p;
            _timer = new Timer(interval.TotalMilliseconds) { AutoReset = true, Enabled = true };
            _timer.Elapsed += async (s, e) =>
            {
                if (_executandoAtualizacao) return;
                _executandoAtualizacao = true;

                try
                {
                    _log.Info("🔄 Auto-update iniciado…");
                    await ObterDadosAsync(_timerProgress);
                    _log.Info("✅ Auto-update concluído.");
                }
                catch (Exception ex)
                {
                    _log.Info($"[ERRO] Auto-update: {ex.Message}");
                }
                finally
                {
                    _executandoAtualizacao = false;
                }
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

        /// <summary>
        /// Libera a memória utilizada pelos dados baixados.
        /// </summary>
        public void ClearData()
        {
            _dados = null;
        }

    }
}

