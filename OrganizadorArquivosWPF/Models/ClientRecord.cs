namespace OrganizadorArquivosWPF.Models
{
    /// <summary>
    /// Representa uma linha da aba “Manutenção AC_MT” da planilha.
    /// </summary>
    public class ClientRecord
    {
        public string Rota { get; set; }
        public string Tipo { get; set; }
        public string NumOS { get; set; }
        public string NumOcorrencia { get; set; }
        public string Obra { get; set; }
        public string IdSigfi { get; set; }
        public string UC { get; set; }
        public string NomeCliente { get; set; }
        public string Empresa { get; set; }       // “INTELBRAS” ou “HOPPECKE”
        public string TipoDesigfi { get; set; }   // ex: “SIGFI160”

        // Mantidos para compatibilidade:
        public string UF { get; set; }            // “AC” ou “MT”
        public string NomeArquivoBase { get; set; } // Construído dinamicamente
    }
}
