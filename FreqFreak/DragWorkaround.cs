using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreqFreak
{
    using System;
    using System.Diagnostics;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Threading;

    public sealed class NormalDragHandler
    {
        private readonly Window _window;
        private static bool _isDragging;
        private Point _startScreen;
        private double _startLeft, _startTop;
        public static bool IsDragging 
        {
            get
            {
                return _isDragging;
            }
        }
        private Point _initialMousePos;

        public NormalDragHandler(Window window)
            => _window = window ?? throw new ArgumentNullException(nameof(window));

        public void BeginDrag(MouseButtonEventArgs e)
        {
            if (_isDragging) return;
            _isDragging = true;


            _startScreen = _window.PointToScreen(e.GetPosition(_window));
            _startLeft = _window.Left;
            _startTop = _window.Top;

            _window.CaptureMouse();
            _window.MouseMove += OnMove;
            _window.MouseLeftButtonUp += OnUp;
            _window.LostMouseCapture += OnUp; 
        }

        public void EndDrag() => OnUp(null, null);


        private void OnMove(object? _, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point now = _window.PointToScreen(e.GetPosition(_window));
            _window.Left = _startLeft + (now.X - _startScreen.X);
            _window.Top = _startTop + (now.Y - _startScreen.Y);
        }

        private void OnUp(object? _, MouseEventArgs? __)
        {
            if (!_isDragging) return;
            _isDragging = false;

            _window.ReleaseMouseCapture();
            _window.MouseMove -= OnMove;
            _window.MouseLeftButtonUp -= OnUp;
            _window.LostMouseCapture -= OnUp;
        }

        private void OnMouseLeftButtonUp(object? sender, MouseButtonEventArgs e) => EndDrag();

        private void OnLostCapture(object? sender, MouseEventArgs e) => EndDrag();
    }

    //public class DragWorkaround
    //{
    //    private const int WH_MOUSE_LL = 14;
    //    private const int WM_MOUSEMOVE = 0x0200;
    //    private const int WM_LBUTTONUP = 0x0202;

    //    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    //    private static LowLevelMouseProc _proc = HookCallback;

    //    private static IntPtr _hookID = IntPtr.Zero;

    //    private static Point _dragOffset;
    //    public static bool _isDragging = false;
    //    private static Window _targetWindow;
    //    private static IntPtr _targetHwnd;

    //    [StructLayout(LayoutKind.Sequential)]
    //    private struct MSLLHOOKSTRUCT
    //    {
    //        public POINT pt;
    //        public uint mouseData;
    //        public uint flags;
    //        public uint time;
    //        public IntPtr dwExtraInfo;
    //    }

    //    [StructLayout(LayoutKind.Sequential)]
    //    private struct POINT
    //    {
    //        public int x;
    //        public int y;
    //    }

    //    [DllImport("user32.dll")]
    //    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    //    [DllImport("user32.dll")]
    //    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    //    [DllImport("user32.dll")]
    //    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    //    [DllImport("user32.dll")]
    //    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    //    [DllImport("kernel32.dll")]
    //    private static extern IntPtr GetModuleHandle(string lpModuleName);

    //    private const uint SWP_NOSIZE = 0x0001;
    //    private const uint SWP_NOZORDER = 0x0004;

    //    public static void StartDragging(Window window, Point clickOffset)
    //    {
    //        _targetWindow = window;
    //        _dragOffset = clickOffset;

    //        var hwnd = new WindowInteropHelper(window).Handle;
    //        _targetHwnd = hwnd;

    //        _hookID = SetHook(_proc);
    //    }

    //    private static IntPtr SetHook(LowLevelMouseProc proc)
    //    {
    //        using (Process curProcess = Process.GetCurrentProcess())
    //        using (ProcessModule curModule = curProcess.MainModule)
    //        {
    //            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    //        }
    //    }
    //    private static Point _lastMousePosition;

    //    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    //    {
    //        if (nCode >= 0)
    //        {
    //            int msg = wParam.ToInt32();

    //            if (msg == WM_MOUSEMOVE && _isDragging)
    //            {
    //                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
    //                _lastMousePosition = new Point(hookStruct.pt.x, hookStruct.pt.y);
    //            }
    //            else if (msg == WM_LBUTTONUP && _isDragging)
    //            {
    //                _isDragging = false;
    //                UnhookWindowsHookEx(_hookID);
    //                _dragTimer?.Stop();
    //            }
    //        }

    //        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    //    }



    //    private static System.Timers.Timer _dragTimer;

    //    public static void BeginDrag(Dispatcher dispatcher)
    //    {
    //        _isDragging = true;

    //        _dragTimer = new System.Timers.Timer(2); // ~60 Hz
    //        _dragTimer.AutoReset = true;
    //        _dragTimer.Elapsed += (s, e) =>
    //        {
    //            if (!_isDragging) return;
    //            var m = _lastMousePosition;
    //            if(m.X == -1 && m.Y == -1)
    //            {
    //                return;
    //            }
    //            int x = (int)(m.X - _dragOffset.X), y = (int)(m.Y - _dragOffset.Y);

    //            dispatcher.BeginInvoke(new Action(() =>
    //            {
    //                SetWindowPos(_targetHwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    //            }),priority: DispatcherPriority.Render);
    //        };
    //        _dragTimer.Start();
    //    }

    //    public static void EndDrag()
    //    {
    //        _isDragging = false;
    //        _dragTimer?.Stop();
    //        _dragTimer?.Dispose();
    //        _lastMousePosition = new Point(-1, -1);
    //    }
    //}

}
