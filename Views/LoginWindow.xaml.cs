using System;
using System.Windows;
using System.Windows.Input;
using OrganizadorArquivosWPF.Models;
using OrganizadorArquivosWPF.Services;

namespace OrganizadorArquivosWPF.Views
{
    public partial class LoginWindow : Window
    {
        // Substitui as propriedades separadas
        public UsuarioRecord Usuario { get; private set; }

        private readonly FuncionariosService _funcionariosService
            = new FuncionariosService();

        public LoginWindow()
        {
            InitializeComponent();
            TxtMatricula.Focus();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnEntrar_Click(object sender, RoutedEventArgs e)
            => TentarLogin();

        private void TxtMatricula_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                TentarLogin();
        }

        private void TentarLogin()
        {
            LblErro.Visibility = Visibility.Collapsed;
            var raw = TxtMatricula.Text.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                MostrarErro("Digite a matrícula.");
                return;
            }

            try
            {
                var func = _funcionariosService.BuscarPorMatricula(raw);
                if (func == null)
                {
                    MostrarErro("Matrícula não encontrada. Tente novamente.");
                    return;
                }

                // Monta o UsuarioRecord
                Usuario = new UsuarioRecord
                {
                    Matricula = func.Matricula,
                    NomeUsuario = func.Nome
                };

                DialogResult = true;
            }
            catch (System.IO.FileNotFoundException)
            {
                MostrarErro("Arquivo de funcionários não encontrado!");
            }
            catch (Exception ex)
            {
                MostrarErro("Erro ao ler dados: " + ex.Message);
            }
        }

        private void MostrarErro(string msg)
        {
            LblErro.Text = msg;
            LblErro.Visibility = Visibility.Visible;
        }
    }
}
