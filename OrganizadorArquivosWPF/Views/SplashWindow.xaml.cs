using System.Windows;

namespace OrganizadorArquivosWPF.Views
{
    /// <summary>
    /// Janela de splash que aparece durante a sincronização inicial.
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void SetProgress(int value)
        {
            if (value < 0)
            {
                Progress.IsIndeterminate = true;
                StatusText.Text = "Iniciando download...";
                PercentText.Text = string.Empty;
                return;
            }

            Progress.IsIndeterminate = false;
            if (value > 100) value = 100;
            Progress.Value = value;
            PercentText.Text = $"{value}%";

            if (value >= 100)
                StatusText.Text = "Download concluído";
            else
                StatusText.Text = $"Baixando dados ({value}%)";
        }
    }
}
