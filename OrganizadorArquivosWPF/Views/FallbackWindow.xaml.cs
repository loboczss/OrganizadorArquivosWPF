using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OrganizadorArquivosWPF.Services;

namespace OrganizadorArquivosWPF.Views
{
    public partial class FallbackWindow : Window
    {
        // Controle para evitar buscas durante a inicialização
        private bool _carregando = true;

        // Captura dos dados
        public string ClienteEncontrado { get; private set; }
        public string OSFull { get; }
        public string Rota { get; private set; }
        public string IdSigfi { get; private set; }
        public bool Is160 => Chk160.IsChecked == true;
        public string TipoSistema => Is160 ? "SIGFI160" : "OUTRO";

        // Guarda a UF selecionada no MainWindow
        private readonly string _ufPrefixo;

        public FallbackWindow(string osFull, IEnumerable<string> rotas, string uf)
        {
            InitializeComponent();

            OSFull = osFull;
            _ufPrefixo = uf.ToUpperInvariant();

            TxtOSFull.Text = osFull;

            // Preenche ComboBox de rotas (desabilitada para seleção manual)
            foreach (var r in rotas.Distinct().OrderBy(x => x))
                CmbRota.Items.Add(new ComboBoxItem { Content = r });

            CmbRota.IsEnabled = false; // rota será preenchida automaticamente

            // Prefixa o campo IdSIGFI com a UF
            TxtIdSigfi.Text = _ufPrefixo;
            TxtIdSigfi.SelectionStart = TxtIdSigfi.Text.Length;

            // Inicialmente nenhum cliente, valida botão OK
            ClienteEncontrado = null;
            Validate();

            // Concluiu a inicialização
            _carregando = false;
        }

        // Habilita OK apenas quando ID SIGFI estiver completo e cliente existir
        private void Validate()
        {
            BtnOk.IsEnabled =
                !string.IsNullOrWhiteSpace(TxtIdSigfi.Text) &&
                TxtIdSigfi.Text.Length > _ufPrefixo.Length &&
                !string.IsNullOrEmpty(ClienteEncontrado);
        }

        // Busca assincrona sem travar UI
        private async Task BuscarClientePorIdSigfiAsync(string idSigfi)
        {
            try
            {
                var service = new ExcelService();
                var progress = new Progress<int>(_ => { }); // ignoramos progresso aqui

                // Executa em background
                var record = await Task.Run(() => service.GetRecordByIdSigfi(idSigfi, progress));

                // Atualiza UI no thread principal
                Dispatcher.Invoke(() =>
                {
                    if (record != null)
                    {
                        ClienteEncontrado = record.NomeCliente;
                        LblCliente.Content = $"Cliente: {ClienteEncontrado}";

                        // Preenche e seleciona a rota automaticamente
                        var rotaEncontrada = record.Rota;
                        Rota = rotaEncontrada;

                        bool achou = false;
                        foreach (ComboBoxItem item in CmbRota.Items)
                        {
                            if (string.Equals(item.Content.ToString(), rotaEncontrada, StringComparison.OrdinalIgnoreCase))
                            {
                                CmbRota.SelectedItem = item;
                                achou = true;
                                break;
                            }
                        }
                        if (!achou)
                        {
                            var novo = new ComboBoxItem { Content = rotaEncontrada };
                            CmbRota.Items.Add(novo);
                            CmbRota.SelectedItem = novo;
                        }
                    }
                    else
                    {
                        ClienteEncontrado = null;
                        LblCliente.Content = "Cliente não encontrado.";
                        Rota = string.Empty;
                        CmbRota.SelectedIndex = -1;
                    }

                    // Toda vez que o cliente for buscado, revalida o botão OK
                    Validate();
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    ClienteEncontrado = null;
                    LblCliente.Content = "Erro ao buscar cliente.";
                    Rota = string.Empty;
                    CmbRota.SelectedIndex = -1;
                    Validate();
                });
            }
        }

        private void CmbRota_Changed(object sender, SelectionChangedEventArgs e)
            => Validate();

        private async void TxtIdSigfi_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_carregando) return;

            string texto = TxtIdSigfi.Text.Trim();

            // Se o usuário apagar o prefixo, restaura
            if (!texto.StartsWith(_ufPrefixo, StringComparison.OrdinalIgnoreCase))
            {
                texto = _ufPrefixo;
                TxtIdSigfi.Text = texto;
                TxtIdSigfi.SelectionStart = texto.Length;
                ClienteEncontrado = null;
                LblCliente.Content = "Digite o restante do ID SIGFI.";
                Rota = string.Empty;
                CmbRota.SelectedIndex = -1;
                Validate();
                return;
            }

            // Só faz busca se tiver pelo menos 5 caracteres além do prefixo
            if (texto.Length < _ufPrefixo.Length + 5)
            {
                ClienteEncontrado = null;
                LblCliente.Content = "ID SIGFI incompleto (mínimo 5 dígitos).";
                Rota = string.Empty;
                CmbRota.SelectedIndex = -1;
                Validate();
                return;
            }

            // Quando cumprido o mínimo, inicia busca assíncrona
            Validate(); // enquanto busca, OK permanece desabilitado
            await BuscarClientePorIdSigfiAsync(texto);
        }

        private void Chk160_Checked(object sender, RoutedEventArgs e)
        {
            // não altera diretamente o OK, mas revalida para caso dependa de cliente
            Validate();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Lê valores finais
            IdSigfi = TxtIdSigfi.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
