using Azure.Identity;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class InstalacaoService
    {
        private const string SiteId = "03b55b3a-5e43-430f-90db-687ed2c5b32f";
        private const string ListId = "eeee4abc-d931-4f12-b88b-747d024baf7f";

        private static readonly string OfflinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer",
            "instalacao.json");

        private readonly GraphServiceClient _graph;
        private string _driveId;
        private Timer _timer;
        private static LoggerService _log => LoggerService.Instance;

        public InstalacaoService()
        {
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            var credential = new ClientSecretCredential(Config.TenantId, Config.ClientId, Config.ClientSecret);
            _graph = new GraphServiceClient(credential, scopes);
        }

        private async Task<string> ObterDriveIdAsync()
        {
            if (!string.IsNullOrEmpty(_driveId))
                return _driveId;

            try
            {
                var drive = await _graph
                    .Sites[SiteId]
                    .Lists[ListId]
                    .Drive
                    .GetAsync();

                if (drive == null || string.IsNullOrEmpty(drive.Id))
                    throw new Exception("A lista não retornou um Drive válido.");

                _driveId = drive.Id;
                return _driveId;
            }
            catch (Exception ex)
            {
                _log?.Error($"Erro ao obter Drive ID: {ex.Message}");
                throw;
            }
        }

        private async Task BaixarArquivoAsync()
        {
            string driveId = await ObterDriveIdAsync();

            var page = await _graph
                .Drives[driveId]
                .Items["root"]
                .Children
                .GetAsync();

            var meta = page.Value
                .Where(it => it.File != null && it.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Where(it => it.Name.Contains("Instalacao_AC", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(it => it.LastModifiedDateTime)
                .FirstOrDefault();

            if (meta == null)
                throw new FileNotFoundException("Arquivo de instalação não encontrado no SharePoint.");

            using var stream = await _graph.Drives[driveId].Items[meta.Id].Content.GetAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(OfflinePath));
            using var reader = new StreamReader(stream);
            File.WriteAllText(OfflinePath, await reader.ReadToEndAsync());
        }

        /// <summary>
        /// Baixa o arquivo de instalação do SharePoint substituindo
        /// qualquer versão local existente.
        /// </summary>
        public async Task AtualizarArquivoAsync(IProgress<double>? progress = null)
        {
            try
            {
                progress?.Report(0);
                await BaixarArquivoAsync();
                progress?.Report(100);
            }
            catch (Exception ex)
            {
                _log?.Warning($"Erro ao atualizar arquivo de instalação: {ex.Message}");
            }
        }

        public void StartAutoUpdate(TimeSpan interval)
        {
            if (_timer != null) return;
            _timer = new Timer(interval.TotalMilliseconds) { AutoReset = true, Enabled = true };
            _timer.Elapsed += async (s, e) => await AtualizarArquivoAsync();
        }

        public void StopAutoUpdate()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }

        private void GarantirArquivoLocal()
        {
            if (!File.Exists(OfflinePath))
            {
                try
                {
                    BaixarArquivoAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    throw new FileNotFoundException("Arquivo de instalação não encontrado e não foi possível baixar do SharePoint.", ex);
                }
            }
        }

        public (string NomeCliente, string Rota)? BuscarPorIdSigfi(string idSigfi)
        {
            if (string.IsNullOrWhiteSpace(idSigfi))
                return null;

            GarantirArquivoLocal();

            try
            {
                var json = File.ReadAllText(OfflinePath);
                var root = JObject.Parse(json);
                var arr = root["instalacoes"] as JArray;
                if (arr == null) return null;

                foreach (var item in arr.OfType<JObject>())
                {
                    var val = item.Value<string>("IDSERVICOSCONJ");
                    if (string.Equals(val, idSigfi, StringComparison.OrdinalIgnoreCase))
                    {
                        string cliente = item.Value<string>("NOMEDOCLIENTE") ?? string.Empty;
                        string rota = item.Value<string>("ROTA") ?? string.Empty;
                        return (cliente, rota);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"Erro ao ler arquivo de instalação: {ex.Message}");
            }
            return null;
        }
    }
}
