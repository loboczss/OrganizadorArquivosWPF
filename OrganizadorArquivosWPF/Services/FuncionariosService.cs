using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrganizadorArquivosWPF.Services
{
    public class Funcionario
    {
        public string Matricula { get; set; }
        public string Nome { get; set; }
    }

    public class FuncionariosService
    {
        private readonly string _csvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA",
            "ONE Engenharia - Power BI",
            "funcionarios.csv");

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

        /// <summary>
        /// Busca funcionário pela matrícula. Retorna null se não encontrar.
        /// </summary>
        public Funcionario BuscarPorMatricula(string matricula)
        {
            if (string.IsNullOrWhiteSpace(matricula))
                return null;

            if (!File.Exists(_csvPath))
                throw new FileNotFoundException("Arquivo de funcionários não encontrado!", _csvPath);

            ForcarArquivoOnline(_csvPath); // Força sincronização

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
            if (!File.Exists(_csvPath))
                return lista;

            ForcarArquivoOnline(_csvPath); // Força sincronização

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
