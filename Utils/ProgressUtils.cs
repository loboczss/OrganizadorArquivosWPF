using System;

namespace OrganizadorArquivosWPF.Utils
{
    /// <summary>
    /// Utility classes to compose progress of multiple sequential tasks.
    /// </summary>
    public class ProgressTracker
    {
        private readonly IProgress<int> _outer;
        private readonly int _totalSegments;
        private int _current;

        public ProgressTracker(IProgress<int> outer, int totalSegments)
        {
            _outer = outer;
            _totalSegments = Math.Max(1, totalSegments);
            _current = 0;
        }

        /// <summary>
        /// Gets a progress instance that maps its 0-100 range to the next segment
        /// of the outer progress.
        /// </summary>
        public IProgress<int> NextSegment()
        {
            int index = _current++;
            int start = index * 100 / _totalSegments;
            int end = (index + 1) * 100 / _totalSegments;
            return new Progress<int>(v =>
            {
                v = Math.Clamp(v, 0, 100);
                int scaled = start + (v * (end - start) / 100);
                _outer.Report(scaled);
            });
        }
    }
}
