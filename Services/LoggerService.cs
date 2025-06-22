using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Threading;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class LoggerService
    {
        private readonly string _logFilePath;
        private readonly ObservableCollection<LogEntry> _logs;
        private readonly Dispatcher _dispatcher;
        private readonly object _fileLock = new object();
        private bool _logIoErrorNotified = false;

        public LoggerService(ObservableCollection<LogEntry> logs, Dispatcher dispatcher)
        {
            _logs = logs ?? throw new ArgumentNullException(nameof(logs));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneEngRenamer");
            Directory.CreateDirectory(dir);

            _logFilePath = Path.Combine(dir, "log.txt");
        }

        private void Add(string tipo, string emoji, string mensagem)
        {
            var entry = new LogEntry(tipo, emoji, mensagem);

            // Atualiza a UI de forma thread-safe
            _dispatcher.Invoke(() => _logs.Add(entry));

            // Grava em disco de forma thread-safe
            lock (_fileLock)
            {
                try
                {
                    var texto = string.IsNullOrEmpty(emoji)
                        ? entry.Mensagem
                        : emoji + " " + entry.Mensagem;
                    File.AppendAllText(_logFilePath,
                        $"{entry.Hora:yyyy-MM-dd HH:mm:ss} [{entry.Tipo}] {texto}{Environment.NewLine}");
                    _logIoErrorNotified = false;
                }
                catch (IOException ex)
                {
                    if (!_logIoErrorNotified)
                    {
                        Critical("Falha ao gravar log em disco: " + ex.Message);
                        _logIoErrorNotified = true;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (!_logIoErrorNotified)
                    {
                        Critical("Sem permissão para gravar log em disco: " + ex.Message);
                        _logIoErrorNotified = true;
                    }
                }
            }
        }


        public void Info(string msg) => Add("INFO", "✅", msg);
        public void Warning(string msg) => Add("WARN", "⚠️", msg);
        public void Error(string msg) => Add("ERROR", "❌", msg);
        public void Critical(string msg) => Add("CRITICAL", "🛑", msg);


        /// <summary>
        /// Loga informações de contexto (empresa, sistema, usuário, etc) em bloco.
        /// </summary>
        public void ContextInfo(string titulo, IDictionary<string, string> dados)
        {
            var sb = new StringBuilder();
            sb.AppendLine("======= " + titulo + " =======");
            foreach (var kv in dados)
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            sb.AppendLine("=============================");
            Add("INFO", string.Empty, sb.ToString());
        }

        /// <summary>
        /// Retorna todo o log em memória (sem tocar no arquivo).
        /// </summary>
        public string GetFullLog()
        {
            var sb = new StringBuilder();
            foreach (var entry in _logs)
            {
                var texto = string.IsNullOrEmpty(entry.Emoji)
                    ? entry.Mensagem
                    : entry.Emoji + " " + entry.Mensagem;
                sb.AppendLine($"{entry.Hora:yyyy-MM-dd HH:mm:ss} [{entry.Tipo}] {texto}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sobrescreve o arquivo de log.txt com todo o log em memória.
        /// </summary>
        public void ExportFullLog()
        {
            var fullLog = GetFullLog();
            lock (_fileLock)
            {
                try
                {
                    File.WriteAllText(_logFilePath, fullLog);
                    _logIoErrorNotified = false;
                }
                catch (IOException ex)
                {
                    if (!_logIoErrorNotified)
                    {
                        Critical("Falha ao exportar log: " + ex.Message);
                        _logIoErrorNotified = true;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (!_logIoErrorNotified)
                    {
                        Critical("Sem permissão para exportar log: " + ex.Message);
                        _logIoErrorNotified = true;
                    }
                }
            }
        }

        /// <summary>
        /// Limpa todas as entradas de log mantidas em memória e exibidas na UI.
        /// </summary>
        public void Clear()
        {
            _dispatcher.Invoke(() => _logs.Clear());
        }
    }
}
