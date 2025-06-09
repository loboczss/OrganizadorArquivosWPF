using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Leitura otimizada de XLSB para C# 7.3 (.NET 4.x).
    /// </summary>
    public class ExcelService
    {
        #region Constantes / Init
        private const string SheetMain = "Manutencao AC_MT";
        private const string SheetUpdate = "DtAtual";

        private static readonly string ExcelPath;

        static ExcelService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ExcelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
                "ONE Engenharia - Power BI",
                "Fluxo de Dados - Power BI.xlsb");
        }
        #endregion

        /* =================================================================
           PÚBLICOS
        ==================================================================*/

        /// <summary>
        /// Procura numOS na aba principal. Reporta progresso 0-100 %.
        /// </summary>
        public ClientRecord GetRecord(string numOS, string ufIgnored, IProgress<int> progress = null)
        {
            if (!File.Exists(ExcelPath))
                throw new FileNotFoundException("Planilha não encontrada em: " + ExcelPath);

            /* abre planilha */
            using (var ds = CreateDataSet())
            {
                var table = ds.Tables[SheetMain]
                           ?? throw new Exception("Aba '" + SheetMain + "' não encontrada.");

                int total = table.Rows.Count;
                int pct = 0;

                Action<int> report = i =>
                {
                    int newPct = (int)(i * 100.0 / total);
                    if (newPct != pct)
                    {
                        pct = newPct;
                        progress?.Report(pct);
                    }
                };

                int idx = 0;
                foreach (DataRow row in table.Rows)
                {
                    report(++idx);

                    var osCell = TryGetCell(row, "NUMOS");
                    if (!osCell.Equals(numOS, StringComparison.OrdinalIgnoreCase))
                        continue;

                    progress?.Report(100);

                    string ufFromNumos = osCell.Length >= 2 ? osCell.Substring(0, 2).ToUpperInvariant() : "";

                    return new ClientRecord
                    {
                        Rota = TryGetCell(row, "ROTA"),
                        Tipo = TryGetCell(row, "TIPO").ToUpperInvariant(),
                        NumOS = osCell,
                        NumOcorrencia = TryGetCell(row, "NUMOCORRENCIA"),
                        Obra = TryGetCell(row, "OBRA"),
                        IdSigfi = TryGetCell(row, "IDSIGFI"),
                        UC = TryGetCell(row, "UC"),
                        NomeCliente = TryGetCell(row, "NOMECLIENTE"),
                        Empresa = TryGetCell(row, "EMPRESA").ToUpperInvariant(),
                        TipoDesigfi = TryGetCell(row, "TIPODESIGFI").ToUpperInvariant(),
                        UF = ufFromNumos,
                        NomeArquivoBase = string.Empty
                    };
                }
            }

            /* não encontrou */
            progress?.Report(100);
            return null;
        }

        /// <summary>
        /// Procura por ID SIGFI e retorna o registro correspondente, garantindo uso da coluna correta.
        /// </summary>
        public ClientRecord GetRecordByIdSigfi(string idSigfi, IProgress<int> progress = null)
        {
            if (!File.Exists(ExcelPath))
                throw new FileNotFoundException("Planilha não encontrada em: " + ExcelPath);

            using (var ds = CreateDataSet())
            {
                var table = ds.Tables[SheetMain]
                    ?? throw new Exception("Aba '" + SheetMain + "' não encontrada.");

                // Identifica índices das colunas IDSIGFI e NOMECLIENTE (insensível a maiúsculas)
                int idIndex = -1, nameIndex = -1;
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var colName = table.Columns[i].ColumnName.Trim();
                    if (colName.Equals("IDSIGFI", StringComparison.OrdinalIgnoreCase))
                        idIndex = i;
                    if (colName.Equals("NOMECLIENTE", StringComparison.OrdinalIgnoreCase))
                        nameIndex = i;
                }

                if (idIndex < 0)
                    throw new Exception("Coluna 'IDSIGFI' não encontrada na planilha.");
                if (nameIndex < 0)
                    throw new Exception("Coluna 'NOMECLIENTE' não encontrada na planilha.");

                int total = table.Rows.Count;
                int pct = 0;

                int idx = 0;
                foreach (DataRow row in table.Rows)
                {
                    int newPct = (int)(++idx * 100.0 / total);
                    if (newPct != pct)
                    {
                        pct = newPct;
                        progress?.Report(pct);
                    }

                    var idCell = row[idIndex]?.ToString().Trim() ?? string.Empty;
                    // Normaliza: remove ".0" se presente (dados numéricos vindos do Excel)
                    if (idCell.EndsWith(".0"))
                        idCell = idCell.Substring(0, idCell.Length - 2);

                    if (!idCell.Equals(idSigfi, StringComparison.OrdinalIgnoreCase))
                        continue;

                    progress?.Report(100);

                    var nomeCliente = row[nameIndex]?.ToString().Trim() ?? string.Empty;
                    return new ClientRecord
                    {
                        Rota = TryGetCell(row, "ROTA"),
                        Tipo = TryGetCell(row, "TIPO").ToUpperInvariant(),
                        NumOS = TryGetCell(row, "NUMOS"),
                        NumOcorrencia = TryGetCell(row, "NUMOCORRENCIA"),
                        Obra = TryGetCell(row, "OBRA"),
                        IdSigfi = idCell,
                        UC = TryGetCell(row, "UC"),
                        NomeCliente = nomeCliente,
                        Empresa = TryGetCell(row, "EMPRESA").ToUpperInvariant(),
                        TipoDesigfi = TryGetCell(row, "TIPODESIGFI").ToUpperInvariant(),
                        UF = TryGetCell(row, "NUMOS").Length >= 2 ? TryGetCell(row, "NUMOS").Substring(0, 2).ToUpperInvariant() : string.Empty,
                        NomeArquivoBase = string.Empty
                    };
                }
            }

            progress?.Report(100);
            return null;
        }

        public IList<string> GetRouteList()
        {
            if (!File.Exists(ExcelPath))
                return Array.Empty<string>();

            using (var ds = CreateDataSet())
            {
                var table = ds.Tables[SheetMain];
                if (table == null) return Array.Empty<string>();

                return table.Rows
                            .Cast<DataRow>()
                            .Select(r => TryGetCell(r, "ROTA"))
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                            .OrderBy(s => s)
                            .ToList();
            }
        }

        public string GetLastUpdate()
        {
            if (!File.Exists(ExcelPath))
                return null;

            using (var ds = CreateDataSet(false))               // sem header
            {
                var sheet = ds.Tables.Cast<DataTable>()
                                .FirstOrDefault(t => t.TableName.Equals(SheetUpdate, StringComparison.OrdinalIgnoreCase));

                if (sheet == null || sheet.Rows.Count < 2 || sheet.Columns.Count < 1)
                    return null;

                var raw = sheet.Rows[1][0]?.ToString();
                return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            }
        }

        /* =================================================================
           PRIVADOS
        ==================================================================*/

        private static DataSet CreateDataSet(bool header = true)
        {
            var conf = new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = header
                }
            };

            Stream stream = null;
            IExcelDataReader reader = null;
            try
            {
                stream = File.Open(ExcelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                reader = ExcelReaderFactory.CreateReader(stream);
                return reader.AsDataSet(conf);
            }
            catch
            {
                reader?.Dispose();
                stream?.Dispose();
                throw;
            }
        }

        private static string TryGetCell(DataRow row, string colName)
        {
            object obj;
            try { obj = row[colName]; }
            catch { return string.Empty; }

            return obj == null ? string.Empty : obj.ToString().Trim();
        }
    }
}
