using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesctopNewYear;

internal sealed class WallpaperWindow : NativeWindow, IDisposable
{
    private const int WmEraseBackground = 0x14;
    private readonly DesktopHost host;
    private readonly YardLayout layout;
    private readonly YardScene scene;
    private readonly Snowflake[] flakes;
    private readonly BufferedGraphicsContext frameBuffer;
    private Bitmap? landscape;
    private ActorFrame? snowman, owl, ball;
    private PointF? miniatureTarget;
    private Rectangle weather;
    private int snowFrame = -1;
    private bool weatherEnabled;
    private PrankFrame prank;
    private (Daylight Light, int Ornaments, int Snow, int Missing, int MiniSize, bool MiniHat)? painted;

    public bool Available => Handle != 0 && Native.IsWindow(Handle) && host.Available;
    public nint IconHost => host.IconHost;

    public WallpaperWindow(DesktopHost host, YardScene scene)
    {
        this.host = host;
        this.scene = scene;
        layout = scene.Layout;
        var random = new Random(31417);
        const int columns = 8, rows = 9;
        flakes = Enumerable.Range(0, columns * rows).Select(i => new Snowflake(
            (i % columns + random.NextSingle()) / columns,
            (i / columns + random.NextSingle()) / rows, 21 + random.NextSingle() * 60,
            1.6f + random.NextSingle() * 2.8f, .36f + random.NextSingle() * .64f)).ToArray();

        var position = new Native.NativePoint(layout.View.X, layout.View.Y);
        if (!Native.ScreenToClient(host.Parent, ref position))
            throw new Win32Exception("Не удалось определить положение рабочего стола.");

        frameBuffer = new BufferedGraphicsContext
        {
            MaximumBuffer = new Size(layout.View.Width, layout.View.Height)
        };
        try
        {
            CreateHandle(new CreateParams
            {
                Caption = "DesctopNewYear — двор",
                Style = Native.Popup,
                ExStyle = Native.NoActivate | Native.ToolWindow | Native.Transparent |
                    (host.Raised ? Native.Layered : 0)
            });
            Native.AttachChild(Handle, host.Parent);

            if (host.Raised && !Native.SetLayeredWindowAttributes(Handle, 0, 255, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!Native.SetWindowPos(Handle, host.Raised ? host.Icons : 0, position.X, position.Y,
                layout.View.Width, layout.View.Height, Native.Inactive | Native.Show))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch
        {
            DestroyHandle();
            frameBuffer.Dispose();
            throw;
        }
    }

    public void Draw(Daylight daylight, YardSettings settings, ActorFrame? backSnowman, ActorFrame? backOwl,
        bool showWeather)
    {
        var key = (daylight, settings.Ornaments, settings.Snow, scene.StolenOrnament,
            settings.MiniSnowmanSize, settings.MiniSnowmanHat);
        var dirty = Rectangle.Empty;
        if (painted != key)
        {
            var next = YardArt.Landscape(layout, daylight, settings.Ornaments, settings.Snow, scene.StolenOrnament,
                settings.MiniSnowmanSize, settings.MiniSnowmanHat);
            landscape?.Dispose();
            landscape = next;
            painted = key;
            dirty = new Rectangle(Point.Empty, layout.View.Size);
        }

        MarkActor(snowman, backSnowman);
        MarkActor(owl, backOwl);
        var nextBall = scene.LooseSnowball;
        MarkActor(ball, nextBall);
        ball = nextBall;
        var nextTarget = scene.MiniatureTarget;
        if (nextTarget != miniatureTarget)
        {
            Invalidate(Union(PrankRegion(miniatureTarget, 42), PrankRegion(nextTarget, 42)));
            miniatureTarget = nextTarget;
        }
        snowman = backSnowman;
        owl = backOwl;

        var nextWeather = showWeather ? scene.AmbientFootprint() : Rectangle.Empty;
        dirty = Union(Union(dirty, weather), nextWeather);
        weather = nextWeather;
        weatherEnabled = showWeather;
        var nextSnowFrame = showWeather ? scene.SnowFrame : -1;
        if (nextSnowFrame != snowFrame)
        {
            dirty = layout.SnowArea;
            snowFrame = nextSnowFrame;
        }

        var nextPrank = scene.Prank;
        if (nextPrank != prank)
        {
            Invalidate(Union(PrankRegion(prank.Hat, 32), PrankRegion(nextPrank.Hat, 32)));
            Invalidate(Union(PrankRegion(prank.Snowball, 8), PrankRegion(nextPrank.Snowball, 8)));
            Invalidate(Union(PrankRegion(prank.Toy?.Position, 15), PrankRegion(nextPrank.Toy?.Position, 15)));
            prank = nextPrank;
        }
        Invalidate(dirty);
    }

    private Rectangle PrankRegion(PointF? point, float radius)
    {
        if (point is not { } active)
            return Rectangle.Empty;
        radius *= layout.Scale;
        var x = active.X - layout.View.X;
        var y = active.Y - layout.View.Y;
        return Rectangle.FromLTRB((int)Math.Floor(x - radius), (int)Math.Floor(y - radius),
            (int)Math.Ceiling(x + radius), (int)Math.Ceiling(y + radius));
    }

    private void Invalidate(Rectangle dirty)
    {
        dirty.Intersect(new Rectangle(Point.Empty, layout.View.Size));
        if (dirty.Width <= 0 || dirty.Height <= 0)
            return;
        var rectangle = new Native.NativeRect(dirty);
        Native.InvalidateRect(Handle, ref rectangle, false);
    }

    private void MarkActor(ActorFrame? previous, ActorFrame? next)
    {
        if (previous == next)
            return;
        var dirty = Rectangle.Empty;
        if (previous is { } old)
            dirty = Union(dirty, old.Footprint(layout.Scale, layout.View.Location));
        if (next is { } active)
            dirty = Union(dirty, active.Footprint(layout.Scale, layout.View.Location));
        Invalidate(dirty);
    }

    private static Rectangle Union(Rectangle a, Rectangle b) => a.IsEmpty ? b :
        b.IsEmpty ? a : Rectangle.Union(a, b);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmEraseBackground)
        {
            message.Result = 1;
            return;
        }
        if (message.Msg != Native.WmPaint)
        {
            base.WndProc(ref message);
            return;
        }

        var dc = Native.BeginPaint(message.HWnd, out var paint);
        try
        {
            if (dc == 0 || landscape is null)
                return;
            var region = paint.Region.Rectangle;
            if (region.Width <= 0 || region.Height <= 0)
                return;
            PresentFrame(dc, region);
        }
        finally
        {
            Native.EndPaint(message.HWnd, ref paint);
        }
    }

    internal void PresentFrame(nint destination, Rectangle region)
    {
        // Present the finished scene in one copy so the desktop never shows the erased actors.
        using var frame = frameBuffer.Allocate(destination, region);
        PaintFrame(frame.Graphics, region);
        frame.Render(destination);
    }

    internal void PaintFrame(Graphics graphics, Rectangle region)
    {
        if (landscape is null)
            return;
        graphics.DrawImage(landscape, region, region, GraphicsUnit.Pixel);
        YardArt.Prepare(graphics);
        if (weatherEnabled)
            scene.DrawWeather(graphics, flakes);
        DrawPrank(graphics);
        if (miniatureTarget is { } marker)
            YardArt.DrawSnowballTarget(graphics, new(marker.X - layout.View.X, marker.Y - layout.View.Y), layout.Scale);
        DrawActor(graphics, ball, region);
        DrawActor(graphics, snowman, region);
        DrawActor(graphics, owl, region);
    }

    private void DrawPrank(Graphics graphics)
    {
        if (prank.Hat is { } hat)
            YardArt.Hat(graphics, new(hat.X - layout.View.X, hat.Y - layout.View.Y), layout.Scale * .88f, prank.Angle);
        if (prank.Toy is { } toy)
            YardArt.Ornament(graphics, new(toy.Position.X - layout.View.X, toy.Position.Y - layout.View.Y),
                toy.Index, layout.Scale * 1.1f);
        if (prank.Snowball is not { } ball)
            return;
        var scale = layout.Scale;
        var x = ball.X - layout.View.X;
        var y = ball.Y - layout.View.Y;
        graphics.FillEllipse(Brushes.LightSteelBlue, x - 5 * scale, y - 5 * scale, 10 * scale, 10 * scale);
        graphics.FillEllipse(Brushes.White, x - 4 * scale, y - 5 * scale, 7.2f * scale, 7.2f * scale);
    }

    private void DrawActor(Graphics graphics, ActorFrame? actor, Rectangle dirty)
    {
        if (actor is not { } drawn || !dirty.IntersectsWith(drawn.Footprint(layout.Scale, layout.View.Location)))
            return;
        var before = graphics.Save();
        graphics.TranslateTransform(drawn.Feet.X - layout.View.X, drawn.Feet.Y - layout.View.Y);
        graphics.ScaleTransform(layout.Scale, layout.Scale);
        drawn.Paint(graphics);
        graphics.Restore(before);
    }

    public void Dispose()
    {
        DestroyHandle();
        frameBuffer.Dispose();
        landscape?.Dispose();
        landscape = null;
    }
}
