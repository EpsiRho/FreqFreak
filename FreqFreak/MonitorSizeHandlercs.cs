using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;

namespace FreqFreak
{
    // This is stupid but I cannot get system.windows.forms.screen access so fuck you I'll go get it from the system myself.
    public static class MonitorSizeHandlercs
    {
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public static (int w, int h) GetCurrentMonitorSize(Window window)
        {

            var hwnd = new WindowInteropHelper(window).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                int width = monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left;
                int height = monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top;

                return (width, height);
            }

             return (0, 0);
        }
    }
}
