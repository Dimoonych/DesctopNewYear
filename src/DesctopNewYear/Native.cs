using System.Runtime.InteropServices;
using System.Text;

namespace DesctopNewYear;

internal static class Native
{
    internal const int Child = 0x40000000;
    internal const int Popup = unchecked((int)0x80000000);
    internal const int Layered = 0x80000;
    internal const int Transparent = 0x20;
    internal const int ToolWindow = 0x80;
    internal const int NoActivate = 0x08000000;
    internal const int WmPaint = 0x0f;
    internal const uint NoMove = 2;
    internal const uint NoSize = 1;
    internal const uint Inactive = 0x10;
    internal const uint Show = 0x40;
    internal static readonly nint Topmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) => (X, Y) = (x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;
        public NativeSize(int width, int height) => (Width, Height) = (width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        public NativeRect(Rectangle r) => (Left, Top, Right, Bottom) = (r.Left, r.Top, r.Right, r.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Paint
    {
        public nint Dc;
        public int Erase;
        public NativeRect Region;
        private int restore, update;
        private long reserved1, reserved2, reserved3, reserved4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Blend
    {
        public byte Operation, Flags, Alpha, Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint Colors, ImportantColors;
    }

    internal delegate bool WindowVisitor(nint window, nint argument);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(WindowVisitor visitor, nint argument);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam,
        uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")]
    internal static extern nint GetParent(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetParent(nint window, nint parent);
    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")]
    internal static extern bool InvalidateRect(nint window, ref NativeRect rectangle, bool erase);
    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint window, out Paint paint);
    [DllImport("user32.dll")]
    internal static extern bool EndPaint(nint window, ref Paint paint);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint destination,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int max);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect rectangle, int size);

    internal static nint ForegroundApp()
    {
        var window = GetForegroundWindow();
        if (window == 0)
            return 0;

        GetWindowThreadProcessId(window, out var process);
        if (process == Environment.ProcessId)
            return 0;

        var name = new StringBuilder(96);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return 0;

        return window;
    }

    internal static Rectangle FrameBounds(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window))
            return Rectangle.Empty;
        if (DwmGetWindowAttribute(window, 9, out var bounds, Marshal.SizeOf<NativeRect>()) == 0 ||
            GetWindowRect(window, out bounds))
            return bounds.Rectangle;

        return Rectangle.Empty;
    }

    internal static WindowPerch? FindPerch(nint window, Rectangle bounds, YardLayout layout)
    {
        if (window == 0 || bounds.IsEmpty || IsZoomed(window) || IsFullscreen(bounds, layout.View))
            return null;

        var usable = Rectangle.Intersect(bounds, layout.Work);
        var scale = layout.Scale;
        if (usable.Width < 155 * scale || bounds.Height < 115 * scale ||
            bounds.Top < layout.Work.Top + 81 * scale || bounds.Top > layout.Work.Bottom - 95 * scale)
            return null;

        return new(window, bounds, new(usable.Left + usable.Width * .55f, bounds.Top + 2 * scale));
    }

    internal static bool IsFullscreen(Rectangle window, Rectangle monitor) =>
        !window.IsEmpty && window.Left <= monitor.Left + 2 && window.Top <= monitor.Top + 2 &&
        window.Right >= monitor.Right - 2 && window.Bottom >= monitor.Bottom - 2;

    internal static void AttachChild(nint window, nint parent)
    {
        const int style = -16;
        SetWindowLong(window, style, (GetWindowLong(window, style) & ~Popup) | Child);
        SetParent(window, parent);
        var error = Marshal.GetLastWin32Error();
        if (GetParent(window) != parent)
            throw new System.ComponentModel.Win32Exception(error, "Не удалось присоединить окно к рабочему столу.");
    }
}

internal readonly record struct DesktopHost(nint Parent, nint IconHost, nint Icons, bool Raised)
{
    internal bool Available => Parent != 0 && Native.IsWindow(Parent) &&
        Native.IsWindow(IconHost) && Native.IsWindow(Icons);

    internal static DesktopHost TryFind()
    {
        const int exStyle = -20;
        const int noRedirection = 0x00200000;
        var progman = Native.FindWindow("Progman", null);
        if (progman == 0)
            return default;

        Native.SendMessageTimeout(progman, 0x052c, 0x0d, 1, 2, 400, out _);
        var icons = Native.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        var childWorker = Native.FindWindowEx(progman, 0, "WorkerW", null);
        if ((Native.GetWindowLong(progman, exStyle) & noRedirection) != 0 && icons != 0 && childWorker != 0)
            return new(progman, progman, icons, true);

        DesktopHost classic = default;
        Native.EnumWindows((window, _) =>
        {
            var view = Native.FindWindowEx(window, 0, "SHELLDLL_DefView", null);
            if (view == 0)
                return true;

            var worker = Native.FindWindowEx(0, window, "WorkerW", null);
            if (worker == 0)
                return true;

            classic = new(worker, window, view, false);
            return false;
        }, 0);

        return classic;
    }
}
