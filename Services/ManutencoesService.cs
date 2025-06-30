// ManutencoesService.cs — WPF | C# 7.3 | .NET Framework 4.x
// ONE Engenharia • Revisão: 25/06/2025 • VERSÃO COM GRAPH E ERROS CS0119 CORRIGIDOS

using Azure.Identity;
using Microsoft.Graph;
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
            var credential = new ClientSecretCredential(Config.TenantId, Config.ClientId, Config.ClientSecret);
            _graph = new GraphServiceClient(credential, scopes);
            _log?.Info("ManutencoesService inicializado");
        }

        public static DateTime? GetCacheTimestamp()
        {
            try
            {
                var ts = File.Exists(OfflinePath) ? File.GetLastWriteTime(OfflinePath) : (DateTime?)null;
                _log?.Info($"Timestamp do cache: {ts}");
                return ts;
            }
            catch (Exception ex)
            {
                _log?.Error($"Erro ao obter timestamp do cache: {ex.Message}");
                return null;
            }
        }

        public async Task<JArray> ObterDadosAsync(IProgress<int> progress = null)
        {
            progress?.Report(0);
            _log?.Info("Iniciando obtenção de dados de manutenção");
            bool fromInternet = false;

            if (TemInternet())
            {
                try
                {
                    _log?.Info("Internet detectada, baixando arquivos do SharePoint");
                    var arquivos = await BaixarUltimosArquivosManutencaoAsync(progress);

                    if (arquivos.Count == 0)
                        throw new Exception("⚠️ Nenhum dos 4 arquivos foi encontrado no SharePoint.");

                    _dados = CombinarArquivosJson(arquivos);

                    if (_dados.Count == 0)
                        throw new Exception("⚠️ Não foi possível extrair registros dos arquivos baixados.");

                    Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
                    File.WriteAllText(OfflinePath, _dados.ToString(Formatting.None), Encoding.UTF8);
                    _log?.Info("Cache de dados atualizado");

                    fromInternet = true;
                    progress?.Report(100);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Falha ao baixar do SharePoint ({ex.Message}). Tentando cache local…");
                }
            }
            else
            {
                _log?.Warning("Sem conexão com a internet. Usando cache local se disponível");
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
            _log?.Info($"Total de registros carregados: {_records.Count}");
            UpdateCompleted?.Invoke(DateTime.Now, fromInternet);
            return _dados;
        }

        private async Task<Dictionary<string, string>> BaixarUltimosArquivosManutencaoAsync(
            IProgress<int> progress, int maxTentativas = 3)
        {
            _log?.Info("Iniciando download dos arquivos de manutenção");
            for (int tentativa = 1; tentativa <= maxTentativas; tentativa++)
            {
                try
                {
                    _log?.Info($"Tentativa {tentativa} de baixar arquivos do SharePoint");
                    return await BaixarArquivosSharePointInternoAsync(progress);
                }
                catch (Exception ex) when (tentativa < maxTentativas)
                {
                    _log.Warning($"Tentativa {tentativa} falhou ({ex.Message}). Retentando…");
                    await Task.Delay(1000 * tentativa);
                }
            }
            _log?.Error("Não foi possível baixar os arquivos de manutenção após as tentativas");
            return new Dictionary<string, string>();
        }

        private async Task<string> ObterDriveIdAsync()
        {
            if (!string.IsNullOrEmpty(_driveId)) return _driveId;

            _log?.Info("Obtendo Drive ID do SharePoint");

            var site = await _graph.Sites[$"{SPDomain}:/sites/{SPSitePath}"].GetAsync();
            var drives = await _graph.Sites[site.Id].Drives.GetAsync();
            var drive = drives.Value.FirstOrDefault(d => d.Name == DocumentLibraryName);

            if (drive == null)
                throw new Exception($"Biblioteca '{DocumentLibraryName}' não encontrada.");

            _driveId = drive.Id;
            _log?.Info($"Drive ID obtido: {_driveId}");
            return _driveId;
        }

        private async Task<Dictionary<string, string>> BaixarArquivosSharePointInternoAsync(IProgress<int> progress)
        {
            string driveId = await ObterDriveIdAsync();
            _log?.Info("Listando arquivos .json no SharePoint");

            Microsoft.Graph.Models.DriveItemCollectionResponse page =
                await _graph.Drives[driveId].Items["root"].Children.GetAsync();
            var jsonFiles = page.Value
                .Where(it => it.File != null && it.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _log?.Info($"{jsonFiles.Count} arquivos .json encontrados");

            if (!jsonFiles.Any())
                throw new FileNotFoundException("Nenhum .json encontrado na biblioteca.");

            var resultados = new Dictionary<string, string>();
            int totalEtapas = _padroesArquivo.Length;
            int concluidos = 0;

            foreach (string padrao in _padroesArquivo)
            {
                _log?.Info($"Procurando arquivos para o padrão '{padrao}'");
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
                    _log?.Info($"Arquivo '{meta.Name}' baixado com sucesso");
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
            _log?.Info("Combinando arquivos JSON");
            var combinado = new JArray();

            foreach (var kv in arquivos)
            {
                _log?.Info($"Processando '{kv.Key}'");
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

            _log?.Info($"Total combinado: {combinado.Count} registros");

            return combinado;
        }

        public static List<ClientRecord> ParseClientRecords(JArray array)
        {
            _log?.Info("Convertendo JArray em registros de cliente");
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
            _log?.Info($"Total de registros convertidos: {list.Count}");
            return list;
        }

        public List<ClientRecord> LoadCachedRecords()
        {
            if (!File.Exists(OfflinePath))
            {
                _log?.Warning("Arquivo de cache não encontrado");
                return new List<ClientRecord>();
            }
            try
            {
                var list = ParseClientRecords(JArray.Parse(File.ReadAllText(OfflinePath, Encoding.UTF8)));
                _records = new List<ClientRecord>(list);
                _log?.Info($"{list.Count} registros carregados do cache");
                return list;
            }
            catch
            {
                _log?.Error("Falha ao carregar registros do cache");
                return new List<ClientRecord>();
            }
        }

        public async Task<List<ClientRecord>> ObterClientRecordsAsync(IProgress<int> p = null)
        {
            _log?.Info("Solicitação de registros de cliente");
            await ObterDadosAsync(p);
            _log?.Info($"Retornando {_records.Count} registros");
            return new List<ClientRecord>(_records);
        }

        private static bool TemInternet()
        {
            try
            {
                using (var wc = new WebClient())
                    wc.DownloadString("https://www.google.com/generate_204");
                _log?.Info("Conexão com a internet verificada via Google");
                return true;
            }
            catch
            {
                try
                {
                    using (var wc = new WebClient())
                        wc.DownloadString("https://www.bing.com");
                    _log?.Info("Conexão com a internet verificada via Bing");
                    return true;
                }
                catch
                {
                    _log?.Warning("Sem acesso à internet");
                    return false;
                }
            }
        }

        public void StartAutoUpdate(TimeSpan interval, IProgress<int> p = null)
        {
            if (_timer != null) return;

            _log?.Info($"Iniciando auto atualização a cada {interval.TotalMinutes} min");

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
            _log?.Info("Auto atualização parada");
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            _timerProgress = null;
            _executandoAtualizacao = false;
        }

        public void ClearData()
        {
            _log?.Info("Limpando dados em memória");
            _dados = null;
        }
    }
}
