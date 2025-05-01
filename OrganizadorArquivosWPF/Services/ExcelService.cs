using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using ExcelDataReader;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class ExcelService
    {
        private readonly string _excelPath;

        public ExcelService()
        {
            // Necessário para ler .xlsb
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _excelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
                "ONE Engenharia - Power BI",
                "Fluxo de Dados - Power BI.xlsb"
            );
        }

        /// <summary>
        /// Lê a aba "Manutenção AC_MT" e retorna o ClientRecord cujo NUMOS casa com numOS completo (ex: "AC202500000265").
        /// </summary>
        public ClientRecord GetRecord(string numOS, string ufIgnored)
        {
            if (!File.Exists(_excelPath))
                throw new FileNotFoundException("Planilha não encontrada em: " + _excelPath);

            using (var stream = File.Open(_excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var conf = new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                };
                var ds = reader.AsDataSet(conf);
                var table = ds.Tables["Manutenção AC_MT"]
                            ?? throw new Exception("Aba 'Manutenção AC_MT' não encontrada.");

                foreach (DataRow row in table.Rows)
                {
                    var osCell = row["NUMOS"]?.ToString().Trim();
                    if (!string.Equals(osCell, numOS, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // extrai os dois primeiros caracteres como UF
                    var ufFromNumos = osCell.Length >= 2
                        ? osCell.Substring(0, 2).ToUpper()
                        : "";

                    return new ClientRecord
                    {
                        Rota = row["ROTA"]?.ToString().Trim(),
                        Tipo = row["TIPO"]?.ToString().Trim().ToUpper(),
                        NumOS = osCell,
                        NumOcorrencia = row["NUMOCORRENCIA"]?.ToString().Trim(),
                        Obra = row["OBRA"]?.ToString().Trim(),
                        IdSigfi = row["IDSIGFI"]?.ToString().Trim(),
                        UC = row["UC"]?.ToString().Trim(),
                        NomeCliente = row["NOMECLIENTE"]?.ToString().Trim(),
                        Empresa = row["EMPRESA"]?.ToString().Trim().ToUpper(),
                        TipoDesigfi = row["TIPODESIGFI"]?.ToString().Trim().ToUpper(),
                        UF = ufFromNumos,
                        NomeArquivoBase = ""  // continua gerado depois
                    };
                }
            }

            return null;
        }


        /// <summary>
        /// Retorna a lista de rotas distintas da planilha, para popular o ComboBox de fallback.
        /// </summary>
        public IList<string> GetRouteList()
        {
            if (!File.Exists(_excelPath))
                return Array.Empty<string>();

            using (var stream = File.Open(_excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var conf = new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration
                    {
                        UseHeaderRow = true
                    }
                };
                var ds = reader.AsDataSet(conf);
                var table = ds.Tables["Manutenção AC_MT"];
                if (table == null) return Array.Empty<string>();

                return table.Rows
                            .Cast<DataRow>()
                            .Select(r => r["ROTA"]?.ToString().Trim())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                            .OrderBy(s => s)
                            .ToList();
            }
        }
    }
}
