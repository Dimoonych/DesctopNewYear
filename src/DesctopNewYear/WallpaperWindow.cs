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
    private Bitmap? landscape;
    private ActorFrame? snowman, owl;
    private Rectangle weather;
    private bool weatherEnabled;
    private PrankFrame prank;
    private (Daylight Light, int Ornaments, int Snow, int Missing)? painted;

    public bool Available => Handle != 0 && Native.IsWindow(Handle) && host.Available;
    public nint IconHost => host.IconHost;

    public WallpaperWindow(DesktopHost host, YardScene scene)
    {
        this.host = host;
        this.scene = scene;
        layout = scene.Layout;
        var random = new Random(31417);
        flakes = Enumerable.Range(0, 54).Select(_ => new Snowflake(
            random.NextSingle(), random.NextSingle(), 21 + random.NextSingle() * 60,
            1.6f + random.NextSingle() * 2.8f, .36f + random.NextSingle() * .64f)).ToArray();

        var position = new Native.NativePoint(layout.View.X, layout.View.Y);
        if (!Native.ScreenToClient(host.Parent, ref position))
            throw new Win32Exception("Не удалось определить положение рабочего стола.");

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
            throw;
        }
    }

    public void Draw(Daylight daylight, YardSettings settings, ActorFrame? backSnowman, ActorFrame? backOwl,
        bool showWeather)
    {
        var key = (daylight, settings.Ornaments, settings.Snow, scene.StolenOrnament);
        var dirty = Rectangle.Empty;
        if (painted != key)
        {
            var next = YardArt.Landscape(layout, daylight, settings.Ornaments, settings.Snow, scene.StolenOrnament);
            landscape?.Dispose();
            landscape = next;
            painted = key;
            dirty = new Rectangle(Point.Empty, layout.View.Size);
        }

        MarkActor(ref dirty, snowman, backSnowman);
        MarkActor(ref dirty, owl, backOwl);
        snowman = backSnowman;
        owl = backOwl;

        var nextWeather = showWeather ? scene.WeatherFootprint() : Rectangle.Empty;
        dirty = Union(Union(dirty, weather), nextWeather);
        weather = nextWeather;
        weatherEnabled = showWeather;
        var nextPrank = scene.Prank;
        if (nextPrank != prank)
        {
            Invalidate(Union(PrankRegion(prank.Hat, 32), PrankRegion(nextPrank.Hat, 32)));
            Invalidate(Union(PrankRegion(prank.Snowball, 8), PrankRegion(nextPrank.Snowball, 8)));
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

    private void MarkActor(ref Rectangle dirty, ActorFrame? previous, ActorFrame? next)
    {
        if (previous == next)
            return;
        if (previous is { } old)
            dirty = Union(dirty, old.Footprint(layout.Scale, layout.View.Location));
        if (next is { } active)
            dirty = Union(dirty, active.Footprint(layout.Scale, layout.View.Location));
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
            if (dc == 0 || landscape is null || paint.Region.Rectangle.IsEmpty)
                return;
            using var graphics = Graphics.FromHdc(dc);
            var region = paint.Region.Rectangle;
            if (region.Width <= 0 || region.Height <= 0)
                return;
            graphics.DrawImage(landscape, region, region, GraphicsUnit.Pixel);
            YardArt.Prepare(graphics);
            if (weatherEnabled)
                scene.DrawWeather(graphics, flakes);
            DrawPrank(graphics);
            DrawActor(graphics, snowman, region);
            DrawActor(graphics, owl, region);
        }
        finally
        {
            Native.EndPaint(message.HWnd, ref paint);
        }
    }

    private void DrawPrank(Graphics graphics)
    {
        if (prank.Hat is { } hat)
            YardArt.Hat(graphics, new(hat.X - layout.View.X, hat.Y - layout.View.Y), layout.Scale * .88f, prank.Angle);
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
        landscape?.Dispose();
        landscape = null;
    }
}
