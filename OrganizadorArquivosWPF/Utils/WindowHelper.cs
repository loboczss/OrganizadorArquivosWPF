using System;
using System.Runtime.InteropServices;

namespace OrganizadorArquivosWPF.Utils
{
    internal static class WindowHelper
    {
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        internal const int SW_RESTORE = 9;
    }
}
