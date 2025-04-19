using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class LoggerService
    {
        private readonly string _logFilePath;
        private readonly ObservableCollection<LogEntry> _logs;
        private readonly Dispatcher _dispatcher;

        public LoggerService(ObservableCollection<LogEntry> logs, Dispatcher dispatcher)
        {
            _logs = logs;
            _dispatcher = dispatcher;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneEngRenamer");
            Directory.CreateDirectory(dir);

            _logFilePath = Path.Combine(dir, "log.txt");
        }

        private void Add(string tipo, string mensagem)
        {
            var entry = new LogEntry(tipo, mensagem);

            _dispatcher.Invoke(() => _logs.Add(entry));
            try
            {
                File.AppendAllText(_logFilePath,
                    $"{entry.Hora:yyyy-MM-dd HH:mm:ss} [{entry.Tipo}] {entry.Mensagem}{Environment.NewLine}");
            }
            catch { /* ignorar falha de IO */ }
        }

        public void Info(string msg) => Add("INFO", msg);
        public void Warning(string msg) => Add("WARN", msg);
        public void Error(string msg) => Add("ERROR", msg);
    }
}
