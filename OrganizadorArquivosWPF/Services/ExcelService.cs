using System;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Serviço para leitura da planilha de clientes.
    /// Configura o contexto de licença do EPPlus para uso não-comercial
    /// e abre o arquivo em modo compartilhado para evitar bloqueios.
    /// </summary>
    public class ExcelService
    {
        private readonly string _excelPath;

        public ExcelService()
        {
            // Define o contexto de licença do EPPlus antes de qualquer uso
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Monta o caminho da planilha de clientes
            _excelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
                "ONE Engenharia - Renomeação",
                "Clientes.xlsx"
            );
        }

        /// <summary>
        /// Retorna o registro correspondente ao número da OS e UF,
        /// ou null se não encontrado.
        /// Abre o arquivo com FileShare.ReadWrite para evitar erro de acesso quando estiver aberto no Excel.
        /// </summary>
        public ClientRecord GetRecord(string numOS, string uf)
        {
            var fileInfo = new FileInfo(_excelPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Planilha não encontrada em: " + _excelPath);

            // Abre em modo compartilhado para leitura
            using (var stream = new FileStream(
                fileInfo.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var package = new ExcelPackage(stream))
            {
                var sheet = package.Workbook.Worksheets.FirstOrDefault();
                if (sheet == null || sheet.Dimension == null)
                    return null;

                int lastRow = sheet.Dimension.End.Row;
                for (int row = 2; row <= lastRow; row++)
                {
                    var osCell = sheet.Cells[row, 2].Text.Trim();
                    var ufCell = sheet.Cells[row, 5].Text.Trim();
                    if (string.Equals(osCell, numOS, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(ufCell, uf, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ClientRecord
                        {
                            NumOS = osCell,
                            NomeCliente = sheet.Cells[row, 4].Text.Trim(),
                            UF = ufCell,
                            NomeArquivoBase = sheet.Cells[row, 6].Text.Trim()
                        };
                    }
                }
            }

            return null;
        }
    }
}
