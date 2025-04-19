using System;
using System.IO;
using OfficeOpenXml;
using OrganizadorArquivosWPF.Models;

namespace OrganizadorArquivosWPF.Services
{
    public class ExcelService
    {
        private readonly string _excelPath;

        public ExcelService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _excelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
                "ONE Engenharia - Renomeação",
                "Clientes.xlsx"
            );
        }

        /// <summary>
        /// Retorna o registro correspondente à NumOS e UF.
        /// </summary>
        public ClientRecord GetRecord(string numOS, string uf)
        {
            var fileInfo = new FileInfo(_excelPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Planilha não encontrada em: " + _excelPath);

            using (var package = new ExcelPackage(fileInfo))
            {
                var sheet = package.Workbook.Worksheets[0];
                var rowCount = sheet.Dimension.End.Row;
                for (int row = 2; row <= rowCount; row++)
                {
                    var osCell = sheet.Cells[row, 2].Text?.Trim();
                    var ufCell = sheet.Cells[row, 5].Text?.Trim();
                    if (string.Equals(osCell, numOS, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(ufCell, uf, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ClientRecord
                        {
                            NumOS = osCell,
                            NomeCliente = sheet.Cells[row, 4].Text?.Trim(),
                            UF = ufCell,
                            NomeArquivoBase = sheet.Cells[row, 6].Text?.Trim()
                        };
                    }
                }
            }

            return null;
        }
    }
}
