using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DesctopNewYear;

internal sealed class SpriteWindow : NativeWindow, IDisposable
{
    private readonly bool owl;
    private readonly nint desktop;
    private Bitmap? surface;
    private nint dc, dib, previous;
    private ActorFrame? drawn;
    private float drawnScale;
    private Rectangle drawnClip;
    private bool visible;
    private bool dragging;

    public event Action<Point>? Clicked;
    public event Action<Point>? Dragged;
    public event Action<Point>? Dropped;
    public event Action? CaptureLost;

    public bool Available => Handle != 0 && Native.IsWindow(Handle);

    public SpriteWindow(bool owl, nint desktop)
    {
        this.owl = owl;
        this.desktop = desktop;
        CreateHandle(new CreateParams
        {
            Caption = desktop == 0 ? "DesctopNewYear — сова на окне" :
                owl ? "DesctopNewYear — сова" : "DesctopNewYear — снеговик",
            Style = Native.Popup,
            ExStyle = Native.Layered | Native.NoActivate | Native.ToolWindow |
                (desktop == 0 ? Native.Transparent : 0)
        });
        try
        {
            if (desktop != 0)
                Native.AttachChild(Handle, desktop);
        }
        catch
        {
            DestroyHandle();
            throw;
        }
    }

    public void Draw(ActorFrame? frame, float scale, Rectangle monitor)
    {
        if (frame is not { } actor)
        {
            Hide();
            return;
        }

        var width = (int)Math.Ceiling(actor.ArtWidth * scale);
        var height = (int)Math.Ceiling(actor.ArtHeight * scale);
        var left = (int)Math.Round(actor.Feet.X - width / 2f);
        var top = (int)Math.Round(actor.Feet.Y - height + 4 * scale);
        var clip = Rectangle.Intersect(new Rectangle(left, top, width, height), monitor);
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            Hide();
            return;
        }
        clip.Offset(-left, -top);
        if (visible && drawn == actor && drawnScale == scale && drawnClip == clip)
            return;

        if (surface?.Width != width || surface.Height != height)
            Allocate(width, height);

        var sameArtwork = visible && drawn is { } last && last with { Feet = actor.Feet } == actor &&
            drawnScale == scale && drawnClip == clip;
        if (!sameArtwork)
        {
            using var graphics = Graphics.FromImage(surface!);
            graphics.Clear(Color.Transparent);
            graphics.SetClip(clip);
            YardArt.Prepare(graphics);
            graphics.TranslateTransform(width / 2f, height - 4 * scale);
            graphics.ScaleTransform(scale, scale);
            actor.Paint(graphics);
        }

        var destination = new Native.NativePoint(left, top);
        if (desktop != 0 && !Native.ScreenToClient(desktop, ref destination))
            throw new Win32Exception("Не удалось определить положение персонажа на рабочем столе.");
        var size = new Native.NativeSize(width, height);
        var source = new Native.NativePoint();
        // The wallpaper paints desktop actors. These children only provide silhouette-shaped input.
        var blend = new Native.Blend { Alpha = desktop == 0 ? (byte)255 : (byte)1, Format = 1 };
        if (!Native.UpdateLayeredWindow(Handle, 0, ref destination, ref size, dc, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось обновить окно персонажа.");

        if (!visible && !Native.SetWindowPos(Handle, desktop == 0 ? Native.Topmost : 0, 0, 0, 0, 0,
                Native.NoMove | Native.NoSize | Native.Inactive | Native.Show))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось показать персонажа на рабочем столе.");

        visible = true;
        drawn = actor;
        drawnScale = scale;
        drawnClip = clip;
    }

    public void UpdateDrag()
    {
        if (!dragging)
            return;

        // An inactive layered window may stop receiving mouse messages outside its silhouette.
        // Read only the primary mouse button during a gesture that began on the owl.
        var primaryButton = SystemInformation.MouseButtonsSwapped ? 2 : 1;
        if (Native.GetAsyncKeyState(primaryButton) < 0)
            Dragged?.Invoke(Cursor.Position);
        else
            EndDrag();
    }

    private void EndDrag()
    {
        dragging = false;
        Dropped?.Invoke(Cursor.Position);
    }

    private void Hide()
    {
        if (!visible)
            return;

        if (dragging)
        {
            dragging = false;
            CaptureLost?.Invoke();
        }
        Native.ShowWindow(Handle, 0);
        visible = false;
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case 0x21: // WM_MOUSEACTIVATE
                message.Result = 3; // MA_NOACTIVATE
                return;
            case 0x201: // WM_LBUTTONDOWN
                dragging = owl;
                Clicked?.Invoke(Cursor.Position);
                return;
            case 0x202 when dragging: // WM_LBUTTONUP
                EndDrag();
                return;
            default:
                base.WndProc(ref message);
                return;
        }
    }

    private void Allocate(int width, int height)
    {
        ReleaseSurface();
        dc = Native.CreateCompatibleDC(0);
        if (dc == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var info = new Native.BitmapInfo
        {
            Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        dib = Native.CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
        if (dib == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        previous = Native.SelectObject(dc, dib);
        surface = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, pixels);
    }

    private void ReleaseSurface()
    {
        surface?.Dispose();
        surface = null;
        if (previous != 0)
            Native.SelectObject(dc, previous);
        if (dib != 0)
            Native.DeleteObject(dib);
        if (dc != 0)
            Native.DeleteDC(dc);
        previous = dib = dc = 0;
    }

    public void Dispose()
    {
        Hide();
        ReleaseSurface();
        DestroyHandle();
    }
}
