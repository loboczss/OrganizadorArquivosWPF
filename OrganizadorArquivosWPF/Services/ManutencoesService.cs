// ManutencoesService.cs — atualizado com proteção contra loop no Timer
// ONE Engenharia • Revisão 🔥 2025

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
        // === CREDENCIAIS =====================================================
        private const string AppKey = "523wx0kknv1xj4h";
        private const string AppSecret = "mcw1pgyfnx3hqbh";
        private const string RefreshToken = "7-G0mKVNMRQAAAAAAAAAASvMELHHomwEkmVR24HK-XLEFvNMpNUp7Py0hxUnjic_";

        private const string DropboxFolder = ""; // Pasta raiz

        // ======================================================================

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "manutencoes.json");

        private readonly LoggerService _logger;
        private JArray _dados = new JArray();
        private Timer _timer;
        private IProgress<int> _timerProgress;
        private bool _executandoAtualizacao = false;

        public event Action<DateTime, bool> UpdateCompleted;
        public JArray Dados => _dados;

        public ManutencoesService(LoggerService logger = null)
        {
            _logger = logger;
        }

        public static string CacheFilePath => OfflinePath;

        public static DateTime? GetCacheTimestamp()
        {
            try
            {
                return File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null;
            }
            catch { return null; }
        }

        // 🔗 Gera Access Token dinâmico
        private async Task<string> ObterAccessTokenAsync()
        {
            var tokenService = new GerarTokenService(AppKey, AppSecret, RefreshToken);
            return await tokenService.ObterAccessTokenAsync();
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
                    _logger?.Info("🔗 Tentando baixar dados do Dropbox...");

                    string json = await BaixarUltimoJsonDropboxAsync(progress);

                    if (string.IsNullOrEmpty(json) || json.Trim() == "[]")
                        throw new Exception("⚠️ Arquivo baixado está vazio ou inválido.");

                    _logger?.Info("💾 Salvando arquivo local...");
                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, json, Encoding.UTF8);

                    _logger?.Info("📜 Lendo JSON...");
                    _dados = JArray.Parse(json);

                    fromInternet = true;
                    _logger?.Info($"✅ Dados carregados do Dropbox com {_dados.Count} registros.");

                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Durante download ou leitura do Dropbox: {ex.Message}");
                    _logger?.Warning("⚠️ Usando dados do cache...");
                }
            }

            if (!fromInternet)
            {
                try
                {
                    _logger?.Info("📦 Carregando dados do cache local...");
                    _dados = File.Exists(OfflinePath)
                        ? JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8))
                        : new JArray();

                    _logger?.Info($"✅ Cache carregado com {_dados.Count} registros.");
                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Falha ao ler cache local: {ex.Message}");
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
        private async Task<string> BaixarUltimoJsonDropboxAsync(IProgress<int> progress, int maxTentativas = 3)
        {
            for (int tentativa = 1; ; tentativa++)
            {
                try
                {
                    return await BaixarJsonDropboxInternoAsync(progress);
                }
                catch when (tentativa < maxTentativas)
                {
                    _logger?.Warning($"Tentativa {tentativa} falhou, tentando novamente...");
                    await Task.Delay(1000 * tentativa);
                }
            }
        }

        private async Task<string> BaixarJsonDropboxInternoAsync(IProgress<int> progress)
        {
            _logger?.Info($"Conectando ao Dropbox na pasta '{DropboxFolder}'...");

            string accessToken = await ObterAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
                throw new Exception("❌ Falha ao gerar Access Token para Dropbox.");

            using (var dbx = new DropboxClient(accessToken))
            {
                ListFolderResult page;
                try
                {
                    page = await dbx.Files.ListFolderAsync(DropboxFolder);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Falha ao listar a pasta: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                _logger?.Info($"Total de entradas na pasta: {page.Entries.Count}");

                var jsonFiles = page.Entries
                    .Where(e => e.IsFile && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _logger?.Info($"Arquivos .json encontrados: {jsonFiles.Count}");

                if (!jsonFiles.Any())
                    throw new FileNotFoundException("❌ Nenhum arquivo .json encontrado no Dropbox.");

                var escolhido = jsonFiles
                    .OrderByDescending(e => ((FileMetadata)e).ServerModified)
                    .First();

                _logger?.Info($"Download: {escolhido.Name} ({((FileMetadata)escolhido).ServerModified:dd/MM/yyyy HH:mm:ss})");
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
                    _logger?.Error($"Falha ao baixar '{escolhido.Name}': {ex.GetType().Name}: {ex.Message}");
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

        // ------------------ ATUALIZAÇÃO AUTOMÁTICA ----------------------------
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
                    _logger?.Info("🔄 Iniciando atualização automática...");
                    await ObterDadosAsync(_timerProgress);
                    _logger?.Info("✅ Atualização automática concluída.");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Erro no auto update: {ex.Message}");
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
    }
}
