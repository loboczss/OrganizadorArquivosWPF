// File: Services/RenamerService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualBasic; // para InputBox
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Renomeia, move e registra arquivos
    /// Esta versão mantém 100 % da funcionalidade original, mas tem pequenas otimizações
    /// de desempenho, legibilidade e resiliência.
    /// </summary>
    public class RenamerService
    {
        #region Constantes / campos imutáveis
        private const string RaizOneEng = "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA";

        // HashSet garante lookup O(1) e ignora maiúsc./minúsc.
        private static readonly HashSet<string> ImgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg"
        };

        // "" = sem extensão (alguns controladores não têm extensão)
        private static readonly HashSet<string> ControlInvBatExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csv", ".xls", ".xlsx", string.Empty
        };

        // Cache dos caracteres proibidos em nomes de arquivo/pasta
        private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars()
                                                            .Concat(Path.GetInvalidPathChars())
                                                            .Distinct()
                                                            .ToArray();
        #endregion

        private readonly LoggerService _logger;
        private readonly LogFileService _logFileService;
        private readonly List<(string Src, string Dst)> _lastMapping = new List<(string Src, string Dst)>();

        public string LastDestination { get; private set; }

        public RenamerService(LoggerService logger, LogFileService logFileService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logFileService = logFileService ?? throw new ArgumentNullException(nameof(logFileService));
        }

        #region Helpers
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Usa o cache em vez de alocar novos arrays a cada chamada
            return string.Concat(input.Split(InvalidFileChars, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string CreateSafeDir(string path)
        {
            Directory.CreateDirectory(path); // idempotente
            return path;
        }

        private static bool SameVolume(string a, string b)
        {
            return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
        }

        private static void MoveOverwrite(string src, string dst)
        {
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }
        #endregion

        #region Pasta base OneDrive
        public string ResolveBaseDir(string uf)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var raiz = Path.Combine(home, RaizOneEng);

            bool isMt = string.Equals(uf, "MT", StringComparison.OrdinalIgnoreCase);
            string mask = isMt
                ? "ONE Engenharia - LOGIN_W_{0:D3}_R_MT"
                : "ONE Engenharia - Clientes PC ONE {0:D3}";

            var candidatas = Enumerable.Range(1, 100)
                                       .Select(n => Path.Combine(raiz, string.Format(mask, n)))
                                       .Where(Directory.Exists)
                                       .ToArray();

            // Se existir exatamente uma, retorna direto
            if (candidatas.Length == 1) return candidatas[0];

            // Se nenhuma pasta foi encontrada, usa Documentos como base
            if (candidatas.Length == 0)
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var fallback = Path.Combine(docs, "OrganizadorArquivos");
                Directory.CreateDirectory(fallback);
                return fallback;
            }

            // Caso contrário, pergunta ao usuário (InputBox já provê a UX esperada)
            int escolha = 0;
            while (escolha < 1 || escolha > 100)
            {
                var prompt = isMt
                    ? "Digite 1–100 para LOGIN_W_*_R_MT:"
                    : "Digite 1–100 para Clientes PC ONE *:";

                int.TryParse(
                    Interaction.InputBox(prompt, "Número da pasta", "1"),
                    out escolha);
            }

            var destino = Path.Combine(raiz, string.Format(mask, escolha));
            Directory.CreateDirectory(destino);
            return destino;
        }
        #endregion

        #region Classificação de Arquivos
        private (List<string> Controllers, string Inv, string Bat, List<string> Images) ClassifyFiles(IEnumerable<string> files)
        {
            var controllers = new List<string>();
            string inv = null, bat = null;
            var images = new List<string>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // 1) Imagens – nome não importa
                if (ImgExts.Contains(ext))
                {
                    images.Add(file);
                    continue;
                }

                // 2) Controladores (prefixo con/c0n) – aceita arquivo sem extensão
                if (fileName.StartsWith("con", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c0n", StringComparison.OrdinalIgnoreCase))
                {
                    if (ControlInvBatExts.Contains(ext)) controllers.Add(file);
                    continue;
                }

                // 3) Inversor – primeiro inv*
                if (inv == null && fileName.StartsWith("inv", StringComparison.OrdinalIgnoreCase))
                {
                    if (ControlInvBatExts.Contains(ext)) inv = file;
                    continue;
                }

                // 4) Bateria – primeiro bat*
                if (bat == null && fileName.StartsWith("bat", StringComparison.OrdinalIgnoreCase))
                {
                    if (ControlInvBatExts.Contains(ext)) bat = file;
                }
            }

            return (controllers, inv, bat, images);
        }
        #endregion

        #region RenameAsync (API pública)
        public Task RenameAsync(
            string sourceFolder,
            ClientRecord record,
            string sistema,
            string tipoSistema,
            bool isDevMode,
            string devDestino,
            string nomeFuncionario,
            string matriculaFuncionario,
            IProgress<double> progress = null)
        {
            // Task.Run mantém compatibilidade com .NET 4.x (não há async streams etc.)
            return Task.Run(() =>
            {
                bool isSistema160 = string.Equals(tipoSistema, "SIGFI160", StringComparison.OrdinalIgnoreCase);
                double pct = 0;
                void Report(double v) { pct = v; progress?.Report(v); }

                _lastMapping.Clear();
                Report(0);

                // 1) Validação da pasta de origem
                if (!Directory.EnumerateFiles(sourceFolder).Any())
                {
                    _logger.Error("Pasta de origem vazia!");
                    throw new IOException("A pasta de origem não contém arquivos.");
                }

                // 2) Estrutura de destino
                string root = isDevMode ? devDestino : ResolveBaseDir(record.UF);
                string rotaDir = CreateSafeDir(Path.Combine(root, Sanitize(record.Rota)));
                string clienteDir = CreateSafeDir(Path.Combine(rotaDir, Sanitize($"{record.NumOS}_{record.IdSigfi}_{record.Tipo}")));
                LastDestination = clienteDir;

                // 3) Dados de contexto para log.txt
                var contextData = new Dictionary<string, string>
                {
                    { "Funcionário", nomeFuncionario },
                    { "Matrícula", matriculaFuncionario },
                    { "Data/Hora", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },                 
                    { "Destino", LastDestination },
                    { "OS", record?.NumOS ?? "(N/A)" },
                    { "Cliente", record?.NomeCliente ?? "(N/A)" },
                    { "ID SIGFI", record?.IdSigfi ?? "(N/A)" },
                    { "UC", record?.UC ?? "(N/A)" },
                    { "Tipo de Sistema", sistema },
                    { "Sistema do Cliente", tipoSistema },
                    { "Versão do Programa", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "(N/A)" },
                };


                // 4) Verifica se destino está vazio
                if (Directory.EnumerateFileSystemEntries(clienteDir).Any())
                {
                    MessageBox.Show("A pasta de destino já contém arquivos.",
                                    "Destino Não Vazio",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    try { Process.Start("explorer.exe", clienteDir); }
                    catch (Exception ex) { _logger.Warning($"Falha ao abrir destino: {ex.Message}"); }

                    _logger.Warning("Operação bloqueada: destino não vazio.");
                    throw new OperationCanceledException("Destino não vazio.");
                }

                Report(10);

                // 5) Classificação dos arquivos
                var files = Directory.EnumerateFiles(sourceFolder).ToList();
                var (controllers, inv, bat, images) = ClassifyFiles(files);

                // 6) Valida nº de controladores conforme sistema
                string sistemaUpper = sistema.ToUpperInvariant();
                int reqCtrl;

                if (sistemaUpper == "INTELBRAS")
                {
                    reqCtrl = 1;
                    if (controllers.Count != 1)
                    {
                        _logger.Error($"Sistema Intelbras requer exatamente 1 controlador. Encontrados: {controllers.Count}");
                        throw new FileNotFoundException("[INTELBRAS] Requerido: 1 controlador.");
                    }
                }
                else if (sistemaUpper == "HOPPECKE" && isSistema160)
                {
                    reqCtrl = 2;
                    if (controllers.Count != 2)
                    {
                        _logger.Error($"Sistema Hoppecke 160 requer exatamente 2 controladores. Encontrados: {controllers.Count}");
                        throw new FileNotFoundException("[HOPPECKE 160] Requerido: 2 controladores.");
                    }
                }
                else
                {
                    reqCtrl = 1; // genérico
                    if (controllers.Count < 1)
                    {
                        _logger.Error("Sistema genérico requer pelo menos 1 controlador.");
                        throw new FileNotFoundException("Requerido: pelo menos 1 controlador.");
                    }
                }

                // 7) Nome base dos arquivos
                string nomeBase = Sanitize(string.Join("_", new[]
                {
                    record.UC,
                    record.Tipo != "PREVENTIVA" ? record.NumOcorrencia : record.Obra,
                    record.NomeCliente,
                    record.NumOS,
                    record.IdSigfi
                }));

                // Cálculo de progresso (80 % do total, 10 % já foi)
                int totalSteps = controllers.Count + (inv != null ? 1 : 0) + (bat != null ? 1 : 0) + images.Count + 2;
                int done = 0;
                void Step() => Report(10 + (++done * 80.0 / totalSteps));

                // 8) Função local de mover/renomear
                Action<string, string> MoveRen = (src, suf) =>
                {
                    var ext = Path.GetExtension(src);
                    var dst = Path.Combine(clienteDir, Sanitize($"{nomeBase}{suf}{ext}"));

                    if (SameVolume(src, dst))
                        MoveOverwrite(src, dst);
                    else
                    {
                        File.Copy(src, dst, true);
                        File.Delete(src);
                    }

                    _lastMapping.Add((dst, src));
                    Step();
                };

                // 9) Controladores (_CON ou _CON1/_CON2)
                for (int i = 0; i < controllers.Count; i++)
                {
                    var suf = (sistemaUpper == "HOPPECKE" && isSistema160)
                              ? (i == 0 ? "_CON1" : "_CON2")
                              : "_CON";
                    MoveRen(controllers[i], suf);
                }

                // 10) Inversor e bateria
                if (inv != null) MoveRen(inv, "_INV");
                if (bat != null) MoveRen(bat, "_BAT");

                // 11) Imagens (PRINT001, PRINT002…)
                for (int i = 0; i < images.Count; i++)
                    MoveRen(images[i], $"_PRINT{i + 1:D3}");

                // 12) Cria log.txt
                _logFileService.CreateLogTxt(LastDestination, contextData, _logger);
                Report(100);
            });
        }
        #endregion
    }
}
