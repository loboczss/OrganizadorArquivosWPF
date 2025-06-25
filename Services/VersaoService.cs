using System;
using System.IO;
using System.Reflection;

namespace OrganizadorArquivosWPF.Helpers
{
    public static class Versao
    {
        /// <summary>
        /// Cria um arquivo "versao.txt" com a versão do app atual.
        /// </summary>
        public static void GravarVersaoEmTxt()
        {
            try
            {
                // Obtem a versão atual do assembly em execução
                Version versao = Assembly.GetExecutingAssembly().GetName().Version;

                // Caminho da pasta onde o executável está rodando
                string pasta = AppDomain.CurrentDomain.BaseDirectory;

                // Caminho completo do arquivo de versão
                string caminho = Path.Combine(pasta, "versao.txt");

                // Salva a versão como texto (ex: 2.7.4.0)
                File.WriteAllText(caminho, versao.ToString());

                Console.WriteLine($"✔ Versão {versao} gravada em {caminho}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao salvar versão: {ex.Message}");
            }
        }

        /// <summary>
        /// Lê o conteúdo de "versao.txt" na pasta do app, se existir.
        /// </summary>
        public static Version? LerVersaoDoTxt()
        {
            string caminho = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "versao.txt");
            if (!File.Exists(caminho))
                return null;

            try
            {
                string conteudo = File.ReadAllText(caminho).Trim();
                return Version.TryParse(conteudo, out var v) ? v : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
