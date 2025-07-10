using System;

namespace OrganizadorArquivosWPF.Utils
{
    /// <summary>
    /// Utility classes to compose progress of multiple sequential tasks.
    /// </summary>
    public class ProgressTracker
    {
        private readonly IProgress<double> _outer;
        private readonly int _totalSegments;
        private int _current;

        public ProgressTracker(IProgress<double> outer, int totalSegments)
        {
            _outer = outer;
            _totalSegments = Math.Max(1, totalSegments);
            _current = 0;
        }

        /// <summary>
        /// Gets a progress instance that maps its 0-100 range to the next segment
        /// of the outer progress.
        /// </summary>
        public IProgress<double> NextSegment()
        {
            int index = _current++;
            double start = index * 100.0 / _totalSegments;
            double end = (index + 1) * 100.0 / _totalSegments;
            return new Progress<double>(v =>
            {
                v = Math.Clamp(v, 0.0, 100.0);
                double scaled = start + (v * (end - start) / 100.0);
                _outer.Report(scaled);
            });
        }
    }
}
