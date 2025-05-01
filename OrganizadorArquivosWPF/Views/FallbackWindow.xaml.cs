using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OrganizadorArquivosWPF.Views
{
    public partial class FallbackWindow : Window
    {
        // Captura dos dados
        public string OSFull { get; }
        public string Rota { get; private set; }
        public string IdSigfi { get; private set; }
        public bool Is160 => Chk160.IsChecked == true;

        public FallbackWindow(string osFull, IEnumerable<string> rotas, string uf)
        {
            InitializeComponent();

            OSFull = osFull;
            TxtOSFull.Text = osFull;

            // Preenche ComboBox de rotas
            foreach (var r in rotas.Distinct().OrderBy(x => x))
                CmbRota.Items.Add(new ComboBoxItem { Content = r });
        }

        // Habilita OK apenas quando rota e ID SIGFI preenchidos
        private void Validate()
        {
            BtnOk.IsEnabled =
                CmbRota.SelectedIndex > 0 &&
                !string.IsNullOrWhiteSpace(TxtIdSigfi.Text);
        }

        private void CmbRota_Changed(object sender, SelectionChangedEventArgs e)
            => Validate();

        private void TxtIdSigfi_TextChanged(object sender, TextChangedEventArgs e)
            => Validate();

        private void Chk160_Checked(object sender, RoutedEventArgs e)
        {
            // não altera a lógica de OK, só revalida
            Validate();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Lê valores finais
            Rota = (CmbRota.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            IdSigfi = TxtIdSigfi.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
