using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OrganizadorArquivosWPF.Services
{
    public class AtualizadorService
    {
        private const string ApiUrl =
            "https://api.github.com/repos/loboczss/OrganizadorArquivosWPF/releases/latest";

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer", "OrganizadorArquivosWPF");

        public async Task<(Version LocalVersion, Version RemoteVersion)> GetVersionsAsync()
        {
            // 1) Versão local
            Version localVer = new Version(0, 0, 0, 0);
            // A partir da versão com .NET 8 self-contained o executável é
            // apenas um host nativo e a DLL contém os metadados reais.
            var dllPath = Path.Combine(InstallDir, "OrganizadorArquivosWPF.dll");
            var exePath = Path.Combine(InstallDir, "OrganizadorArquivosWPF.exe");

            string asmPath = File.Exists(dllPath) ? dllPath : exePath;

            if (File.Exists(asmPath))
            {
                try
                {
                    localVer = AssemblyName.GetAssemblyName(asmPath).Version;
                }
                catch (BadImageFormatException)
                {
                    // Executável não possui metadados (ex: host nativo). Mantém versão padrão.
                }
            }

            // 2) Versão remota
            Version remoteVer = localVer;
            try
            {
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Add("User-Agent", "OrganizadorArquivosWPF");
                    var json = await http.GetStringAsync(ApiUrl);
                    var obj = JObject.Parse(json);
                    remoteVer = new Version(((string)obj["tag_name"]).TrimStart('v'));
                }
            }
            catch
            {
                // sem internet ou erro → remote = local
            }

            return (localVer, remoteVer);
        }

        public async Task<string?> DownloadLatestReleaseAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "OrganizadorArquivosWPF");
            var json = await http.GetStringAsync(ApiUrl);
            var obj = JObject.Parse(json);
            var asset = (obj["assets"] as JArray)?.FirstOrDefault();
            var url = (string?)asset?["browser_download_url"];
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var fileName = (string?)asset?["name"] ?? Path.GetFileName(url);
            var dest = Path.Combine(Path.GetTempPath(), fileName);
            var data = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dest, data);
            return dest;
        }

        public string CreateUpdateBatch(string zipPath)
        {
            string batchPath = Path.Combine(Path.GetTempPath(), "OrganizadorUpdate.bat");
            string installDir = InstallDir.TrimEnd('\n', '\r', '\\');

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("set ZIP=\"" + zipPath + "\"");
            sb.AppendLine("set INSTALL=\"" + installDir + "\"");
            sb.AppendLine("set TEMP_DIR=%TEMP%\\OrganizadorUpdate");
            sb.AppendLine("if exist \"%TEMP_DIR%\" rmdir /s /q \"%TEMP_DIR%\"");
            sb.AppendLine("mkdir \"%TEMP_DIR%\"");
            sb.AppendLine("powershell -NoLogo -NoProfile -Command \"Expand-Archive -Path '%ZIP%' -DestinationPath '%TEMP_DIR%' -Force\"");
            sb.AppendLine("xcopy \"%TEMP_DIR%\\*\" \"%INSTALL%\\\" /E /Y");
            sb.AppendLine("set DESKTOP=%USERPROFILE%\\Desktop");
            sb.AppendLine("powershell -NoLogo -NoProfile -Command \"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%DESKTOP%\\OrganizadorArquivosWPF.lnk');$s.TargetPath='%INSTALL%\\OrganizadorArquivosWPF.exe';$s.Save()\"");
            sb.AppendLine("rmdir /s /q \"%TEMP_DIR%\"");
            sb.AppendLine("start \"\" \"%INSTALL%\\OrganizadorArquivosWPF.exe\"");

            File.WriteAllText(batchPath, sb.ToString(), Encoding.UTF8);
            return batchPath;
        }
    }
}


