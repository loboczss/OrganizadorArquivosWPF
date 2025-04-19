using System.Windows;

namespace OrganizadorArquivosWPF
{
    public partial class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Atualiza a mensagem exibida no splash.
        /// </summary>
        /// <param name="message">Texto de status</param>
        public void SetStatus(string message)
        {
            Dispatcher.Invoke(() => TxtStatus.Text = message);
        }
    }
}
