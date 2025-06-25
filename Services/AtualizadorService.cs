using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
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
    }
}


