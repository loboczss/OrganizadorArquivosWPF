using System;
using System.Windows;
using OrganizadorArquivosWPF.Services;

namespace OrganizadorArquivosWPF.Views
{
    public partial class UpdatePromptWindow : Window
    {
        public bool ShouldUpdate { get; private set; }

        public UpdatePromptWindow()
        {
            InitializeComponent();
            Loaded += UpdatePromptWindow_Loaded;
        }

        private async void UpdatePromptWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var service = new AtualizadorService();
            var versions = await service.GetVersionsAsync();
            Version localVer = versions.LocalVersion;
            Version remoteVer = versions.RemoteVersion;

            // Se não há update, fecha sem exibir nada
            if (remoteVer <= localVer)
            {
                Close();
                return;
            }

            TxtVersionInfo.Text =
                $"Versão instalada: v{localVer}\n" +
                $"Versão disponível: v{remoteVer}\n\n" +
                "Deseja atualizar agora?";
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            ShouldUpdate = true;
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            ShouldUpdate = false;
            DialogResult = false;
            Close();
        }
    }
}