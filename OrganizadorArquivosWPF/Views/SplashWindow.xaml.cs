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
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            Progress.Value = value;
            PercentText.Text = $"{value}%";
        }
    }
}
