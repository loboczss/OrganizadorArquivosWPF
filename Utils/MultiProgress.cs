using System;

namespace OrganizadorArquivosWPF.Utils
{
    /// <summary>
    /// Helper para dividir uma barra de progresso em segmentos.
    /// Cada segmento recebe parte proporcional do progresso total.
    /// </summary>
    public class MultiProgress
    {
        private readonly IProgress<int> _baseProgress;
        private readonly double _totalUnits;
        private double _consumed;

        public MultiProgress(IProgress<int> progress, int totalUnits)
        {
            if (totalUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalUnits));
            _baseProgress = progress ?? throw new ArgumentNullException(nameof(progress));
            _totalUnits = totalUnits;
        }

        /// <summary>
        /// Cria um segmento com peso relativo informado.
        /// </summary>
        public IProgress<int> NextSegment(int units = 1)
        {
            if (units <= 0)
                throw new ArgumentOutOfRangeException(nameof(units));
            double start = (_consumed / _totalUnits) * 100.0;
            double range = (units / _totalUnits) * 100.0;
            _consumed += units;
            return new Progress<int>(value =>
            {
                if (value < 0)
                {
                    _baseProgress.Report(value);
                    return;
                }
                value = Math.Clamp(value, 0, 100);
                double scaled = start + (value / 100.0) * range;
                _baseProgress.Report((int)Math.Round(scaled));
            });
        }
    }
}
