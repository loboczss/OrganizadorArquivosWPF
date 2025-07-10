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

        public void SetStatus(string text)
        {
            TxtStatus.Text = text;
        }

        public void SetProgress(double value)
        {
            if (value < 0)
            {
                Progress.IsIndeterminate = true;
                TxtStatus.Text = "Iniciando download...";
                PercentText.Text = string.Empty;
                return;
            }

            Progress.IsIndeterminate = false;
            if (value > 100) value = 100;
            Progress.Value = value;
            PercentText.Text = $"{value:0.#}%";

            if (value >= 100)
                TxtStatus.Text = "Download concluído";
            else
                TxtStatus.Text = $"Baixando dados ({value}%)";
        }
    }
}
