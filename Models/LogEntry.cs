using System;
using System.ComponentModel;

namespace OrganizadorArquivosWPF.Models
{
    public class LogEntry : INotifyPropertyChanged
    {
        private DateTime _hora;
        private string _tipo;
        private string _emoji;
        private string _mensagem;

        public DateTime Hora
        {
            get => _hora;
            set
            {
                if (_hora != value)
                {
                    _hora = value;
                    OnPropertyChanged(nameof(Hora));
                }
            }
        }

        public string Tipo
        {
            get => _tipo;
            set
            {
                if (_tipo != value)
                {
                    _tipo = value;
                    OnPropertyChanged(nameof(Tipo));
                }
            }
        }

        public string Emoji
        {
            get => _emoji;
            set
            {
                if (_emoji != value)
                {
                    _emoji = value;
                    OnPropertyChanged(nameof(Emoji));
                }
            }
        }

        public string Mensagem
        {
            get => _mensagem;
            set
            {
                if (_mensagem != value)
                {
                    _mensagem = value;
                    OnPropertyChanged(nameof(Mensagem));
                }
            }
        }

        public LogEntry(string tipo, string emoji, string mensagem)
        {
            Hora = DateTime.Now;
            Tipo = tipo;
            Emoji = emoji;
            Mensagem = mensagem;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
