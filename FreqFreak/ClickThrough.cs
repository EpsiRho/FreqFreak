using System.Runtime.InteropServices;

namespace FreqFreak
{
    public static class ClickThrough
    {
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        public static bool IsEnabled(IntPtr hwnd)
        {
            ulong style = 0;
            if (IntPtr.Size == 8) // 64-bit
            {
                style = (ulong)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            }
            else // 32-bit
            {
                style = (ulong)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt32();
            }
            return (style & WS_EX_TRANSPARENT) != 0;
        }

        public static void Toggle(IntPtr hwnd, bool enable)
        {
            ulong style = 0;
            if (IntPtr.Size == 8) // 64-bit
            {
                style = (ulong)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            }
            else // 32-bit
            {
                style = (ulong)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt32();
            }

            if (enable)
                style |= WS_EX_TRANSPARENT;
            else
                style &= ~WS_EX_TRANSPARENT;

            if (IntPtr.Size == 8) // 64-bit
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((long)style));
            }
            else // 32-bit
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr((long)style));
            }
        }
    }
}
