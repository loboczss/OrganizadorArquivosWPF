// Services/RenamerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;                    // para ler o registro do OneDrive
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class RenamerService
    {
        private readonly LoggerService _logger;
        private readonly List<Tuple<string, string>> _lastMapping = new List<Tuple<string, string>>();

        /// <summary>Última pasta onde os arquivos foram salvos.</summary>
        public string LastDestination { get; private set; }

        public RenamerService(LoggerService logger)
        {
            _logger = logger;
        }

        // Remove caracteres inválidos de nome/path
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars()
                          .Concat(Path.GetInvalidPathChars())
                          .Distinct()
                          .ToArray();
            return string.Concat(input.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        // Escolhe raiz baseada em UF
        public string ResolveBaseDir(string uf)
        {
            var raiz = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA");
            string mascara = uf.Equals("MT", StringComparison.OrdinalIgnoreCase)
                ? "ONE Engenharia - LOGIN_W_{0:D3}_R_MT"
                : "ONE Engenharia - Clientes PC ONE {0:D3}";

            var candidatas = new List<string>();
            for (int n = 1; n <= 100; n++)
            {
                var d = Path.Combine(raiz, string.Format(mascara, n));
                if (Directory.Exists(d)) candidatas.Add(d);
            }
            if (candidatas.Count == 1) return candidatas[0];

            int escolha = 0;
            while (escolha < 1 || escolha > 100)
            {
                string prompt = uf.Equals("MT", StringComparison.OrdinalIgnoreCase)
                    ? "Digite 1–100 para LOGIN_W_*_R_MT:"
                    : "Digite 1–100 para Clientes PC ONE *:";
                int.TryParse(
                    Microsoft.VisualBasic.Interaction.InputBox(prompt, "Número da pasta", "1"),
                    out escolha);
            }
            var destino = Path.Combine(raiz, string.Format(mascara, escolha));
            Directory.CreateDirectory(destino);
            return destino;
        }

        /// <summary>
        /// Renomeia e organiza:
        /// 1) cria pasta da rota,
        /// 2) cria pasta do cliente,
        /// 3) monta nomeBase conforme Power Query,
        /// 4) pergunta sobre backup e faz backup se necessário,
        /// 5) copia/renomeia e apaga originais,
        /// 6) gera log.txt com data/hora, usuário do Windows e OneDrive corporativo.
        /// </summary>
        public async Task RenameAsync(
            string sourceFolder,
            ClientRecord record,
            string sistema,
            bool isSistema160,
            bool isDevMode,
            string devDestino)
        {
            _lastMapping.Clear();

            // 1) pasta raiz
            string root = isDevMode
                ? devDestino
                : ResolveBaseDir(record.UF);
            Directory.CreateDirectory(root);

            // 2) pasta da rota
            string rotaDir = Path.Combine(root, Sanitize(record.Rota));
            Directory.CreateDirectory(rotaDir);

            // 3) pasta do cliente
            string clienteFolder = $"{record.NumOS}_{record.IdSigfi}_{record.Tipo}";
            string clienteDir = Path.Combine(rotaDir, Sanitize(clienteFolder));
            Directory.CreateDirectory(clienteDir);
            LastDestination = clienteDir;

            // 4) se já houver arquivos, perguntar e fazer backup
            var existing = Directory.EnumerateFileSystemEntries(clienteDir).ToArray();
            if (existing.Any())
            {
                var fileNames = existing.Select(Path.GetFileName);
                string msg = "Esta pasta já contém:\n" +
                             string.Join("\n", fileNames) +
                             "\n\nDeseja continuar e criar um backup?";
                if (MessageBox.Show(msg, "Backup Existente", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes)
                {
                    throw new OperationCanceledException("Operação cancelada pelo usuário.");
                }
                string backupDir = Path.Combine(clienteDir, "Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
                Directory.CreateDirectory(backupDir);
                foreach (var entry in existing)
                {
                    var dest = Path.Combine(backupDir, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                        Directory.Move(entry, dest);
                    else
                        File.Move(entry, dest);
                }
                _logger.Info("Backup criado em: " + backupDir);
            }

            // 5) monta nomeBase conforme Power Query
            string nomeBase = string.Join("_", new[]
            {
                record.UC,
                record.Tipo != "CORRETIVA" ? record.Obra : record.NumOcorrencia,
                record.NomeCliente,
                record.NumOS,
                record.IdSigfi
            });
            nomeBase = Sanitize(nomeBase);

            // 6) coleta e valida arquivos
            var files = Directory.GetFiles(sourceFolder).ToList();
            var controllers = files.Where(f =>
                Path.GetFileName(f).StartsWith("con", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith("c0n", StringComparison.OrdinalIgnoreCase))
                .ToList();
            int reqCtrl = sistema.Equals("INTELBRAS", StringComparison.OrdinalIgnoreCase)
                          ? 1
                          : (isSistema160 ? 2 : 1);
            if (controllers.Count < reqCtrl)
                throw new FileNotFoundException($"Encontrados {controllers.Count} controladores; requeridos {reqCtrl}.");

            var invs = files.Where(f =>
                Path.GetFileName(f).StartsWith("inv", StringComparison.OrdinalIgnoreCase)).ToList();
            var bats = files.Where(f =>
                Path.GetFileName(f).StartsWith("bat", StringComparison.OrdinalIgnoreCase)).ToList();
            var imgs = files.Where(f =>
                new[] { ".png", ".jpg", ".jpeg" }
                .Contains(Path.GetExtension(f).ToLower())).ToList();
            var rest = files.Except(controllers).Except(invs).Except(bats).Except(imgs).ToList();

            // 7) copia/renomeia com sufixos
            int printCount = 1;
            Action<string, string> CopyRename = (src, suf) =>
            {
                string ext = Path.GetExtension(src);
                string newName = $"{nomeBase}{suf}{ext}";
                string dst = Path.Combine(clienteDir, Sanitize(newName));
                File.Copy(src, dst, true);
                _lastMapping.Add(Tuple.Create(src, dst));
                _logger.Info($"{Path.GetFileName(src)} → {newName}");
            };

            for (int i = 0; i < controllers.Count; i++)
            {
                string suf = (sistema.Equals("HOPPECKE", StringComparison.OrdinalIgnoreCase) && isSistema160)
                    ? (i == 0 ? "_CON1" : "_CON2")
                    : "_CON";
                CopyRename(controllers[i], suf);
            }
            if (invs.Any()) CopyRename(invs.First(), "_INV");
            if (bats.Any()) CopyRename(bats.First(), "_BAT");
            foreach (var img in imgs)
                CopyRename(img, $"_PRINT{printCount++:D3}");
            foreach (var o in rest)
                CopyRename(o, "_OUT");

            // 8) apaga originais
            foreach (var f in files)
            {
                try { File.Delete(f); }
                catch (Exception ex)
                {
                    _logger.Warning($"Não foi possível apagar {Path.GetFileName(f)}: {ex.Message}");
                }
            }

            // 9) gera o log.txt em LastDestination
            try
            {
                var logFile = Path.Combine(LastDestination, "log.txt");
                var winUser = Environment.UserName;

                // lê nome/email do OneDrive corporativo
                string odName = "N/A", odEmail = "N/A";
                using (var key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\OneDrive\Accounts\Business1"))
                {
                    if (key != null)
                    {
                        odEmail = key.GetValue("UserEmail") as string ?? odEmail;
                        odName = key.GetValue("UserName") as string ?? odEmail;
                    }
                }

                var line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss} | WindowsUser: {1} | OneDriveUser: {2} | OneDriveEmail: {3}",
                    DateTime.Now, winUser, odName, odEmail);
                File.AppendAllText(logFile, line + Environment.NewLine);
                _logger.Info("log.txt criado em: " + logFile);
            }
            catch (Exception ex)
            {
                _logger.Warning("Não foi possível criar log.txt: " + ex.Message);
            }
        }

        /// <summary>Reverte a última operação.</summary>
        public void Undo()
        {
            foreach (var pair in _lastMapping)
            {
                try
                {
                    if (File.Exists(pair.Item2))
                    {
                        File.Copy(pair.Item2, pair.Item1, true);
                        File.Delete(pair.Item2);
                        _logger.Info("Desfeito " + Path.GetFileName(pair.Item2));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Erro ao desfazer: " + ex.Message);
                }
            }
            _lastMapping.Clear();
        }
    }
}
