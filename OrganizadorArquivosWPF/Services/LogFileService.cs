// File: Services/LogFileService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Serilog.Formatting.Display;

namespace OrganizadorArquivosWPF.Services
{

    /// <summary>Responsável por criar o arquivo log.txt com o resumo da operação.</summary>
    public class LogFileService
    {
        string logtxt = "Log.txt";
        public void CreateLogTxt(
            string destinationFolder,
            IDictionary<string, string> contextMap,
            LoggerService logger)
        {
            try
            {
                var logFile = Path.Combine(destinationFolder, logtxt);
                var sb = new StringBuilder();

                sb.AppendLine("======== RESUMO DA OPERAÇÃO ========");
                foreach (var kv in contextMap)
                    sb.AppendLine($"{kv.Key}: {kv.Value}");

                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\OneDrive\Accounts\Business1"))
                {
                    if (key != null)
                    {
                        sb.AppendLine("OneDrive User: " + (key.GetValue("UserName") ?? "N/A"));
                        sb.AppendLine("OneDrive Email: " + (key.GetValue("UserEmail") ?? "N/A"));
                    }
                }

                sb.AppendLine("=====================================");
                sb.AppendLine();
                sb.AppendLine(logger.GetFullLog());
                File.WriteAllText(logFile, sb.ToString());
                logger.Info(logtxt + " criado em: " + logFile);
            }
            catch (Exception ex)
            {
                logger.Warning("Não foi possível criar " + logtxt + ex.Message);
            }
        }
    }
}
