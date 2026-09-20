using System;

namespace PointCursor
{
    public enum GestureKind { None, Drag, DoubleClick }

    // Pure input state: use the event timestamp, not the time the UI processes it.
    public sealed class SelectionGesture
    {
        private readonly int dragX, dragY, doubleX, doubleY;
        private readonly uint doubleTime;
        private bool held, dragged, secondClick, previousClick;
        private uint downTime, previousTime;
        private int previousX, previousY;
        private IntPtr previousWindow;
        public int StartX { get; private set; }
        public int StartY { get; private set; }
        public IntPtr Window { get; private set; }

        public SelectionGesture(int dragX, int dragY, int doubleX, int doubleY, uint doubleTime)
        {
            this.dragX = Math.Max(1, dragX); this.dragY = Math.Max(1, dragY);
            this.doubleX = doubleX; this.doubleY = doubleY; this.doubleTime = doubleTime;
        }
        public void Down(IntPtr window, int x, int y, uint time)
        {
            secondClick = previousClick && window == previousWindow
                && unchecked(time - previousTime) <= doubleTime
                && Math.Abs(x - previousX) <= doubleX && Math.Abs(y - previousY) <= doubleY;
            held = true; dragged = false; StartX = x; StartY = y; Window = window; downTime = time;
        }
        public void Move(int x, int y)
        {
            if (held && (Math.Abs(x - StartX) >= dragX || Math.Abs(y - StartY) >= dragY)) dragged = true;
        }
        public GestureKind Up(int x, int y)
        {
            if (!held) return GestureKind.None;
            Move(x, y); held = false;
            GestureKind kind = dragged ? GestureKind.Drag : secondClick ? GestureKind.DoubleClick : GestureKind.None;
            previousClick = kind == GestureKind.None;
            previousTime = downTime; previousX = StartX; previousY = StartY; previousWindow = Window;
            return kind;
        }
        public void Reset() { held = previousClick = secondClick = dragged = false; }
    }
}
