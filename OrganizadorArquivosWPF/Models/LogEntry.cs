using System;

namespace OrganizadorArquivosWPF.Models
{
    public class LogEntry
    {
        public DateTime Hora { get; set; }
        public string Tipo { get; set; }
        public string Mensagem { get; set; }

        public LogEntry(string tipo, string mensagem)
        {
            Hora = DateTime.Now;
            Tipo = tipo;
            Mensagem = mensagem;
        }
    }
}
