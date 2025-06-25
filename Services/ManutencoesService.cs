// ManutencoesService.cs — WPF | C# 7.3 | .NET Framework 4.x
// ONE Engenharia • Revisão: 25/06/2025 • VERSÃO COM GRAPH E ERROS CS0119 CORRIGIDOS

using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrganizadorArquivosWPF.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace OrganizadorArquivosWPF.Services
{
    public class ManutencoesService
    {
        private const string TenantId = "3b08e64e-b3be-402b-bb26-1fa4f91cf61f";
        private const string ClientId = "3cffac6a-f9d9-42d1-9065-4054fcd40163";
        private const string ClientSecret = "JFd8Q~hHgTYYo0P0EjAM8mpe3xm3.5vTfCHRFc.T";

        private const string SPDomain = "oneengenharia.sharepoint.com";
        private const string SPSitePath = "OneEngenharia";
        private const string DocumentLibraryName = "ArquivosJSON";

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "manutencoes.json");

        private readonly string[] _padroesArquivo = {
            "Manutencao_AC2023", "Manutencao_AC2024",
            "Manutencao_AC2025", "Manutencao_MT"
        };

        private readonly GraphServiceClient _graph;
        private string _driveId;
        private JArray _dados = new JArray();
        private List<ClientRecord> _records = new List<ClientRecord>();
        private Timer _timer;
        private IProgress<int> _timerProgress;
        private bool _executandoAtualizacao;
        private static LoggerService _log => LoggerService.Instance;

        public event Action<DateTime, bool> UpdateCompleted;
        public JArray Dados => _dados;
        public IReadOnlyList<ClientRecord> Records => _records;
        public static string CacheFilePath => OfflinePath;

        public ManutencoesService()
        {
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
            _graph = new GraphServiceClient(credential, scopes);
        }

        public static DateTime? GetCacheTimestamp()
        {
            try { return File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null; }
            catch { return null; }
        }

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
                        throw new Exception("⚠️ Nenhum dos 4 arquivos foi encontrado no SharePoint.");

                    _dados = CombinarArquivosJson(arquivos);

                    if (_dados.Count == 0)
                        throw new Exception("⚠️ Não foi possível extrair registros dos arquivos baixados.");

                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, _dados.ToString(Formatting.None), Encoding.UTF8);

                    fromInternet = true;
                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Falha ao baixar do SharePoint ({ex.Message}). Tentando cache local…");
                }
            }

            if (!fromInternet)
            {
                try
                {
                    _dados = File.Exists(OfflinePath)
                        ? JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8))
                        : new JArray();

                    _log.Info($"Dados carregados do cache: {_dados.Count} registros.");
                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _log.Error($"Falha ao ler cache: {ex.Message}");
                    _dados = new JArray();
                    progress?.Report(100);
                }
            }

            _records = ParseClientRecords(_dados);
            UpdateCompleted?.Invoke(DateTime.Now, fromInternet);
            return _dados;
        }

        private async Task<Dictionary<string, string>> BaixarUltimosArquivosManutencaoAsync(
            IProgress<int> progress, int maxTentativas = 3)
        {
            for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
            {
                try { return await BaixarArquivosSharePointInternoAsync(progress); }
                catch (Exception ex) when (tentativa < maxTentativas)
                {
                    _log.Warning($"Tentativa {tentativa} falhou ({ex.Message}). Retentando…");
                    await Task.Delay(1000 * tentativa);
                }
            }
            return new Dictionary<string, string>();
        }

        private async Task<string> ObterDriveIdAsync()
        {
            if (!string.IsNullOrEmpty(_driveId)) return _driveId;

            var site = await _graph.Sites[$"{SPDomain}:/sites/{SPSitePath}"].GetAsync();
            var drives = await _graph.Sites[site.Id].Drives.GetAsync();
            var drive = drives.Value.FirstOrDefault(d => d.Name == DocumentLibraryName);

            if (drive == null)
                throw new Exception($"Biblioteca '{DocumentLibraryName}' não encontrada.");

            _driveId = drive.Id;
            return _driveId;
        }

        private async Task<Dictionary<string, string>> BaixarArquivosSharePointInternoAsync(IProgress<int> progress)
        {
            string driveId = await ObterDriveIdAsync();

            Microsoft.Graph.Models.DriveItemCollectionResponse page =
                await _graph.Drives[driveId].Items["root"].Children.GetAsync();
            var jsonFiles = page.Value
                .Where(it => it.File != null && it.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!jsonFiles.Any())
                throw new FileNotFoundException("Nenhum .json encontrado na biblioteca.");

            var resultados = new Dictionary<string, string>();
            int totalEtapas = _padroesArquivo.Length;
            int concluidos = 0;

            foreach (string padrao in _padroesArquivo)
            {
                var arquivosPadrao = jsonFiles
                    .Where(f => f.Name.IndexOf(padrao, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(f => f.LastModifiedDateTime)
                    .ToList();

                if (!arquivosPadrao.Any())
                {
                    _log.Warning($"Não encontrou arquivo para '{padrao}'.");
                    concluidos++;
                    progress?.Report(concluidos * 100 / totalEtapas);
                    continue;
                }

                var meta = arquivosPadrao.First();

                try
                {
                    using var stream = await _graph.Drives[driveId].Items[meta.Id].Content.GetAsync();
                    using var reader = new StreamReader(stream);
                    resultados[padrao] = await reader.ReadToEndAsync();
                }
                catch (Exception ex)
                {
                    _log.Error($"Falha ao baixar '{meta.Name}': {ex.Message}");
                }
                finally
                {
                    concluidos++;
                    progress?.Report(concluidos * 100 / totalEtapas);
                }
            }

            progress?.Report(100);
            return resultados;
        }

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

                    JArray arr;

                    if (token is JArray array)
                    {
                        arr = array;
                    }
                    else if (token is JObject obj)
                    {
                        arr = obj.Properties().FirstOrDefault()?.Value as JArray;
                    }
                    else
                    {
                        arr = null;
                    }

                    if (arr == null || arr.Count == 0)
                    {
                        _log.Warning($"Arquivo '{kv.Key}' não continha array válido.");
                        continue;
                    }

                    foreach (var item in arr)
                        combinado.Add(item);
                }
                catch (JsonException jex)
                {
                    _log.Error($"JSON inválido em '{kv.Key}': {jex.Message}");
                }
                catch (Exception ex)
                {
                    _log.Error($"Falha ao processar '{kv.Key}': {ex.Message}");
                }
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
                    await ObterDadosAsync(_timerProgress);
                }
                catch (Exception ex)
                {
                    _log.Error($"Auto-update: {ex.Message}");
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

        public void ClearData() => _dados = null;
    }
}
