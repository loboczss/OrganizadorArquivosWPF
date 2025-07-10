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
            string backupDir = Path.Combine(Path.GetTempPath(), "OrgBackup");

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal enabledelayedexpansion");
            sb.AppendLine();
            sb.AppendLine(":: caminhos");
            sb.AppendLine("set \"ZIP=" + zipPath + "\"");
            sb.AppendLine("set \"INSTALL=" + installDir + "\"");
            sb.AppendLine("set \"BACKUP=" + backupDir + "\"");
            sb.AppendLine("set \"TEMP_DIR=%TEMP%\\OrganizadorUpdate\"");
            sb.AppendLine("set \"LOG=%TEMP%\\OrganizadorUpdate.log\"");
            sb.AppendLine();
            sb.AppendLine(":: limpa log anterior");
            sb.AppendLine("if exist \"%LOG%\" del /f /q \"%LOG%\"");
            sb.AppendLine();
            sb.AppendLine(":: checa ZIP");
            sb.AppendLine("if not exist \"%ZIP%\" (");
            sb.AppendLine("    echo ERRO: arquivo %ZIP% nao encontrado.>>\"%LOG%\"");
            sb.AppendLine("    goto erro");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine(":: backup da instalação atual");
            sb.AppendLine("if exist \"%BACKUP%\" rmdir /s /q \"%BACKUP%\"");
            sb.AppendLine("mkdir \"%BACKUP%\" || (echo Falha ao criar pasta de backup>>\"%LOG%\" & goto erro)");
            sb.AppendLine("xcopy \"%INSTALL%\\*\" \"%BACKUP%\\\" /E /Y >nul 2>&1 || (echo Falha no backup>>\"%LOG%\" & goto erro)");
            sb.AppendLine();
            sb.AppendLine(":: prepara pasta temporária");
            sb.AppendLine("if exist \"%TEMP_DIR%\" rmdir /s /q \"%TEMP_DIR%\"");
            sb.AppendLine("mkdir \"%TEMP_DIR%\" || (echo Falha ao criar temp>>\"%LOG%\" & goto erro)");
            sb.AppendLine();
            sb.AppendLine(":: descompacta");
            sb.AppendLine("powershell -NoLogo -NoProfile -Command \"Expand-Archive -Path '%ZIP%' -DestinationPath '%TEMP_DIR%' -Force\" || (echo Erro ao descompactar>>\"%LOG%\" & goto erro)");
            sb.AppendLine();
            sb.AppendLine(":: copia novos arquivos");
            sb.AppendLine("xcopy \"%TEMP_DIR%\\*\" \"%INSTALL%\\\" /E /Y >nul 2>&1 || (echo Falha ao copiar novos arquivos>>\"%LOG%\" & goto erro)");
            sb.AppendLine();
            sb.AppendLine(":: cria atalho usando VBScript (sempre acerta o Desktop)");
            sb.AppendLine("set \"VBS=%TEMP%\\CreateShortcut.vbs\"");
            sb.AppendLine(@"echo Set oWS = WScript.CreateObject(""WScript.Shell"") > ""%VBS%""");
            sb.AppendLine(@"echo sLinkFile = oWS.SpecialFolders(""Desktop"") ^& ""\CompillerLog.lnk"" >> ""%VBS%""");
            sb.AppendLine(@"echo Set oLink = oWS.CreateShortcut(sLinkFile) >> ""%VBS%""");
            sb.AppendLine(@"echo oLink.TargetPath = ""%INSTALL%\OrganizadorArquivosWPF.exe"" >> ""%VBS%""");
            sb.AppendLine(@"echo oLink.Save >> ""%VBS%""");
            sb.AppendLine("cscript //nologo \"%VBS%\" >nul 2>&1 || (echo Erro ao criar atalho>>\"%LOG%\" & goto erro)");
            sb.AppendLine("del \"%VBS%\"");
            sb.AppendLine();
            sb.AppendLine(":: cleanup");
            sb.AppendLine("rmdir /s /q \"%TEMP_DIR%\"");
            sb.AppendLine();
            sb.AppendLine(":: sucesso");
            sb.AppendLine("echo Atualizacao concluida com sucesso!>>\"%LOG%\"");
            sb.AppendLine("type \"%LOG%\"");
            sb.AppendLine();
            sb.AppendLine(":: inicia o programa principal");
            sb.AppendLine("start \"\" \"%INSTALL%\\OrganizadorArquivosWPF.exe\"");
            sb.AppendLine();
            sb.AppendLine("endlocal");
            sb.AppendLine("exit /b 0");
            sb.AppendLine();
            sb.AppendLine(":erro");
            sb.AppendLine("echo --- OCORREU UM ERRO --->>\"%LOG%\"");
            sb.AppendLine("echo Veja o log em %LOG%");
            sb.AppendLine("type \"%LOG%\"");
            sb.AppendLine("echo Restaurando backup...");
            sb.AppendLine("if exist \"%BACKUP%\" xcopy \"%BACKUP%\\*\" \"%INSTALL%\\\" /E /Y >nul 2>&1");
            sb.AppendLine("endlocal");
            sb.AppendLine("exit /b 1");

            File.WriteAllText(batchPath, sb.ToString(), Encoding.UTF8);
            return batchPath;
        }
    }
}


