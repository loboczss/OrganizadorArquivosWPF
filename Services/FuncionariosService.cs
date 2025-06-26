using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;

namespace OrganizadorArquivosWPF.Services
{
    public class Funcionario
    {
        public string Matricula { get; set; }
        public string Nome { get; set; }
    }

    public class FuncionariosService
    {
        private const string TenantId = "3b08e64e-b3be-402b-bb26-1fa4f91cf61f";
        private const string ClientId = "3cffac6a-f9d9-42d1-9065-4054fcd40163";
        private const string ClientSecret = "JFd8Q~hHgTYYo0P0EjAM8mpe3xm3.5vTfCHRFc.T";

        private const string SPDomain = "oneengenharia.sharepoint.com";
        private const string SPSitePath = "OneEngenharia";
        private const string DocumentLibraryName = "ArquivosJSON";
        private const string CsvFileName = "funcionarios.csv";

        private readonly GraphServiceClient _graph;
        private string _driveId;

        private readonly string _csvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneEngRenamer","OrganizadorArquivosWPF","funcionarios.csv");

        public FuncionariosService()
        {
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
            _graph = new GraphServiceClient(credential, scopes);
        }


        /// <summary>
        /// Força o arquivo a ser disponibilizado localmente (online) caso esteja somente na nuvem.
        /// </summary>
        private void ForcarArquivoOnline(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    byte[] buffer = new byte[1];
                    stream.Read(buffer, 0, 1); // Lê 1 byte para forçar a sincronização
                }
            }
            catch (Exception ex)
            {
                throw new IOException("Falha ao forçar o arquivo a ficar online. Verifique se o SharePoint está sincronizado corretamente.", ex);
            }
        }

        private async Task<string> ObterDriveIdAsync()
        {
            if (!string.IsNullOrEmpty(_driveId)) return _driveId;

            var site = await _graph.Sites[$"{SPDomain}:/sites/{SPSitePath}"].GetAsync();
            var drives = await _graph.Sites[site.Id].Drives.GetAsync();
            var drive = drives.Value.FirstOrDefault(d => d.Name == DocumentLibraryName)
                ?? throw new Exception($"Biblioteca '{DocumentLibraryName}' não encontrada.");

            _driveId = drive.Id;
            return _driveId;
        }

        private async Task BaixarCsvSharePointAsync()
        {
            string driveId = await ObterDriveIdAsync();
            var page = await _graph.Drives[driveId].Items["root"].Children.GetAsync();
            var meta = page.Value.FirstOrDefault(it => string.Equals(it.Name, CsvFileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Arquivo '{CsvFileName}' não encontrado no SharePoint.");

            using var stream = await _graph.Drives[driveId].Items[meta.Id].Content.GetAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(_csvPath));
            using var fs = File.Create(_csvPath);
            await stream.CopyToAsync(fs);
        }

        private void GarantirArquivoLocal()
        {
            if (!File.Exists(_csvPath))
            {
                try
                {
                    BaixarCsvSharePointAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    throw new FileNotFoundException("Arquivo de funcionários não encontrado e não foi possível baixar do SharePoint.", ex);
                }
            }

            ForcarArquivoOnline(_csvPath);
        }

        /// <summary>
        /// Busca funcionário pela matrícula. Retorna null se não encontrar.
        /// </summary>
        public Funcionario BuscarPorMatricula(string matricula)
        {
            if (string.IsNullOrWhiteSpace(matricula))
                return null;

            GarantirArquivoLocal();

            try
            {
                string matSemZero = matricula.TrimStart('0');

                using (var reader = new StreamReader(_csvPath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var cols = line.Split(',');
                        if (cols.Length >= 2)
                        {
                            var matArquivo = cols[0].Trim();
                            if (matArquivo.TrimStart('0') == matSemZero)
                            {
                                return new Funcionario
                                {
                                    Matricula = matArquivo,
                                    Nome = cols[1].Trim()
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao ler arquivo de funcionários: " + ex.Message, ex);
            }

            return null;
        }

        /// <summary>
        /// Lista todos os funcionários do CSV.
        /// </summary>
        public List<Funcionario> ListarTodos()
        {
            var lista = new List<Funcionario>();

            try
            {
                GarantirArquivoLocal();
            }
            catch
            {
                return lista;
            }

            try
            {
                using (var reader = new StreamReader(_csvPath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var cols = line.Split(',');
                        if (cols.Length >= 2)
                        {
                            lista.Add(new Funcionario
                            {
                                Matricula = cols[0].Trim(),
                                Nome = cols[1].Trim()
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignora erro na listagem geral
            }

            return lista;
        }
    }
}
