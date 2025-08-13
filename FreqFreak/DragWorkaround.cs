using System.Windows;
using System.Windows.Input;

namespace FreqFreak
{
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
}


