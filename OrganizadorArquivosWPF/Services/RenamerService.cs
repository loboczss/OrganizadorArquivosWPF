// Services/RenamerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualBasic;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class RenamerService
    {
        private List<(string Origem, string Destino)> _lastMapping;
        private readonly LoggerService _logger;

        public RenamerService(LoggerService logger)
        {
            _logger = logger;
            _lastMapping = new List<(string, string)>();
        }

        /// <summary>
        /// Remove caracteres inválidos de nomes de arquivo e pasta.
        /// </summary>
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars()
                         .Concat(Path.GetInvalidPathChars())
                         .Distinct()
                         .ToArray();
            return string.Concat(input.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Resolve a pasta‐base, substituindo * por número (001–100).
        /// Se encontrar exatamente uma, usa; senão pede ao usuário.
        /// </summary>
        private string ResolveBaseDir(string ufSan)
        {
            var raiz = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA");

            string mascara = ufSan.Equals("MT", StringComparison.OrdinalIgnoreCase)
                ? "ONE Engenharia - LOGIN_W_{0:D3}_R_MT"
                : "ONE Engenharia - Clientes PC ONE {0:D3}";

            var candidatas = Enumerable.Range(1, 100)
                .Select(n => Path.Combine(raiz, string.Format(mascara, n)))
                .Where(Directory.Exists)
                .ToList();

            if (candidatas.Count == 1)
                return candidatas[0];

            int escolha = 0;
            while (escolha < 1 || escolha > 100)
            {
                var prompt = ufSan.Equals("MT", StringComparison.OrdinalIgnoreCase)
                    ? "Informe o número (1–100) para LOGIN_W_*_R_MT:"
                    : "Informe o número (1–100) para Clientes PC ONE *:";
                var input = Interaction.InputBox(prompt, "Número da pasta", "1");
                int.TryParse(input, out escolha);
            }

            var dir = Path.Combine(raiz, string.Format(mascara, escolha));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public async Task RenameAsync(
            string sourceFolder,
            ClientRecord record,
            string sistema,
            bool isSistema160,
            bool isDevMode,
            string devDestino)
        {
            _lastMapping.Clear();

            // Sanitiza dados da planilha
            var osSan = Sanitize(record.NumOS);
            var ufSan = Sanitize(record.UF);
            var baseNameSan = Sanitize(record.NomeArquivoBase);

            // Determina pasta destino
            var targetBase = isDevMode
                ? devDestino
                : ResolveBaseDir(ufSan);

            if (!Directory.Exists(targetBase))
                Directory.CreateDirectory(targetBase);

            var idFolder = $"{osSan}_{ufSan}";
            var destination = Path.Combine(targetBase, idFolder);

            if (Directory.Exists(destination) && !isDevMode)
            {
                _logger.Warning($"Pasta já existe: {destination}");
                var result = MessageBox.Show(
                    $"A pasta {idFolder} já existe em {targetBase}.\nCriar subpasta de backup?",
                    "Backup Existente",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    var backup = "Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
                    destination = Path.Combine(destination, backup);
                    Directory.CreateDirectory(destination);
                }
            }
            else
            {
                Directory.CreateDirectory(destination);
            }

            var files = Directory.GetFiles(sourceFolder);

            // Controladores
            var controllers = files.Where(f =>
                    Path.GetFileName(f).StartsWith("con", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f).StartsWith("c0n", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sistema.Equals("Intelbras", StringComparison.OrdinalIgnoreCase))
            {
                if (controllers.Count < 1)
                    throw new FileNotFoundException("Intelbras exige pelo menos 1 arquivo 'con*'.");
            }
            else // Hoppecker
            {
                if (isSistema160 && controllers.Count != 2)
                    throw new FileNotFoundException("Sistema 160 exige exatamente 2 arquivos 'con*'.");
                if (!isSistema160 && controllers.Count < 1)
                    throw new FileNotFoundException("Hoppecker exige ao menos 1 arquivo 'con*'.");
            }

            // Outros tipos de arquivo
            var inv = files.Where(f => Path.GetFileName(f)
                .StartsWith("inv", StringComparison.OrdinalIgnoreCase)).ToList();
            if (inv.Count > 1)
                throw new FileNotFoundException("Mais de um arquivo 'inv*' encontrado.");

            var bat = files.Where(f => Path.GetFileName(f)
                .StartsWith("bat", StringComparison.OrdinalIgnoreCase)).ToList();
            if (bat.Count > 1)
                throw new FileNotFoundException("Mais de um arquivo 'bat*' encontrado.");

            // Imagens: todos .png, .jpg, .jpeg
            var imageExts = new[] { ".png", ".jpg", ".jpeg" };
            var prints = files.Where(f => imageExts.Contains(Path.GetExtension(f).ToLower()))
                              .ToList();

            // Restantes
            var others = files.Except(controllers)
                              .Except(inv)
                              .Except(bat)
                              .Except(prints)
                              .ToList();

            int printCount = 1;
            void RenameFile(string src, string suffix)
            {
                var ext = Path.GetExtension(src);
                var nome = Sanitize($"{osSan}_{ufSan}_{baseNameSan}{suffix:D3}") + ext;
                var dest = Path.Combine(destination, nome);
                File.Copy(src, dest);
                _lastMapping.Add((src, dest));
                _logger.Info($"Renomeado {Path.GetFileName(src)} → {nome}");
            }

            // Renomeia controladores
            for (int i = 0; i < controllers.Count; i++)
            {
                var suf = sistema.Equals("Hoppecker", StringComparison.OrdinalIgnoreCase) && isSistema160
                    ? (i == 0 ? "_CON1" : "_CON2")
                    : "_CON";
                RenameFile(controllers[i], suf);
            }
            if (inv.Any()) RenameFile(inv.First(), "_INV");
            if (bat.Any()) RenameFile(bat.First(), "_BAT");
            // Renomeia todas as imagens
            foreach (var p in prints)
                RenameFile(p, $"_PRINT{printCount++:D3}");
            // Demais
            foreach (var o in others)
                RenameFile(o, "_OUT");
        }

        public void Undo()
        {
            foreach (var (orig, dest) in _lastMapping)
            {
                try
                {
                    if (File.Exists(dest))
                    {
                        File.Copy(dest, orig, true);
                        File.Delete(dest);
                        _logger.Info($"Desfeito {Path.GetFileName(dest)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Erro ao desfazer: {ex.Message}");
                }
            }
            _lastMapping.Clear();
        }
    }
}
