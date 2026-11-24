using System.ComponentModel;
using System.Diagnostics;

namespace DesctopNewYear;

internal sealed class YardContext : ApplicationContext
{
    private readonly YardSettings settings = YardSettings.Load();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 650 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem pause = new("Пауза");
    private readonly ToolStripMenuItem wallpaperOnly = new("Только обои");
    private readonly ToolStripMenuItem monitors = new("Экран");
    private readonly ToolStripMenuItem status = new() { Enabled = false };
    private readonly SpriteWindow snowman = new(owl: false);
    private readonly SpriteWindow owl = new(owl: true);
    private readonly Icon icon;
    private readonly NotifyIcon tray;
    private readonly YardScene scene;
    private WallpaperWindow? wallpaper;
    private Rectangle foreground;
    private double lastFrame, nextProbe, nextAttach;
    private bool fullscreen, paused, disposed;
    private (int Ornaments, int Snow) saved;

    public YardContext()
    {
        scene = new(settings, PickLayout());
        saved = (settings.Ornaments, settings.Snow);
        icon = DrawIcon();
        tray = new NotifyIcon
        {
            Icon = icon,
            Text = "DesctopNewYear",
            ContextMenuStrip = menu,
            Visible = true
        };
        ConfigureMenu();

        snowman.Clicked += _ => Run(scene.GreetSnowman);
        owl.Clicked += cursor => Run(() => scene.TouchOwl(cursor));
        owl.Dragged += scene.DragOwl;
        owl.Dropped += _ => scene.DropOwl(Native.ForegroundBounds());
        owl.CaptureLost += scene.CancelDrag;
        tray.DoubleClick += (_, _) => TogglePause();

        timer.Tick += (_, _) => Tick();
        Tick();
        timer.Start();
    }

    private void ConfigureMenu()
    {
        menu.Items.AddRange([
            status, new ToolStripSeparator(),
            Item("Сходить за игрушкой", () => Run(scene.StartToy)),
            Item("Включить снегопад", () => Run(scene.StartSnow)),
            Item("Позвать снеговика с лопатой", () => Run(scene.CallCleaner)),
            Item("Убрать снег сразу", () => Run(scene.ClearSnow)),
            new ToolStripSeparator(),
            Item("Освещение…", ConfigureLight),
            monitors, wallpaperOnly, pause,
            Item("Выйти", ExitThread)
        ]);
        pause.Click += (_, _) => TogglePause();
        wallpaperOnly.Click += (_, _) =>
        {
            settings.WallpaperOnly = !settings.WallpaperOnly;
            Save();
            Render(DateTime.Now);
        };
        menu.Opening += (_, _) =>
        {
            pause.Checked = paused;
            wallpaperOnly.Checked = settings.WallpaperOnly;
            status.Text = wallpaper is not { Available: true } ? "Фон пока не подключён" :
                fullscreen ? "Полноэкранный режим" : "Новогодний двор";
            RefreshMonitors();
        };
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshMonitors()
    {
        while (monitors.DropDownItems.Count > 0)
            monitors.DropDownItems[0].Dispose();

        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var title = $"Экран {i + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}";
            if (screen.Primary)
                title += " (основной)";
            var item = Item(title, () =>
            {
                settings.Monitor = screen.DeviceName;
                Save();
                nextProbe = nextAttach = 0;
                Tick();
            });
            item.Checked = scene.Layout.View == screen.Bounds;
            monitors.DropDownItems.Add(item);
        }
    }

    private void ConfigureLight()
    {
        using var dialog = new LightingDialog(settings);
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        settings.Dusk = dialog.Dusk;
        settings.LightsOff = dialog.LightsOff;
        Save();
        Render(DateTime.Now);
    }

    private void TogglePause()
    {
        paused = !paused;
        lastFrame = clock.Elapsed.TotalSeconds;
        Tick();
    }

    private void Run(Action action)
    {
        action();
        Tick();
    }

    private void Tick()
    {
        if (disposed)
            return;

        var elapsed = clock.Elapsed.TotalSeconds;
        var seconds = (float)Math.Clamp(elapsed - lastFrame, 0, .7);
        lastFrame = elapsed;
        if (elapsed >= nextProbe)
        {
            nextProbe = elapsed + .65;
            var layout = PickLayout();
            if (scene.Layout != layout)
            {
                scene.SetLayout(layout);
                wallpaper?.Dispose();
                wallpaper = null;
                nextAttach = 0;
            }
            foreground = Native.ForegroundBounds();
            fullscreen = Native.IsFullscreen(foreground, layout.View);
        }

        if (wallpaper is not { Available: true } && elapsed >= nextAttach)
        {
            nextAttach = elapsed + 12;
            wallpaper?.Dispose();
            wallpaper = null;
            try
            {
                var host = DesktopHost.TryFind();
                if (host.Available)
                    wallpaper = new WallpaperWindow(host, scene);
            }
            catch (Win32Exception)
            {
                // Explorer can recreate its wallpaper host during display changes.
            }
        }

        var now = DateTime.Now;
        owl.UpdateDrag();
        if (!paused && !fullscreen)
            scene.Advance(seconds, Daylight.At(now, settings), foreground, now);
        Render(now);
        if (saved != (settings.Ornaments, settings.Snow))
            Save();

        var interval = scene.InMotion && !paused && !fullscreen ? 40 : 650;
        if (interval != timer.Interval)
            timer.Interval = interval;
    }

    private void Render(DateTime now)
    {
        var layout = scene.Layout;
        var snowmanFrame = scene.Snowman;
        ActorFrame? owlFrame = scene.Owl;
        ActorFrame? backSnowman = null, backOwl = null;
        var hasWallpaper = wallpaper is { Available: true };
        if (hasWallpaper)
        {
            if (settings.WallpaperOnly || scene.SnowmanInYard && Covered(snowmanFrame, layout))
            {
                backSnowman = snowmanFrame;
                snowmanFrame = null;
            }
            if (settings.WallpaperOnly || scene.OwlInYard && Covered(owlFrame, layout))
            {
                backOwl = owlFrame;
                owlFrame = null;
            }
            wallpaper!.Draw(Daylight.At(now, settings), settings, backSnowman, backOwl, !fullscreen && !paused);
        }
        if (fullscreen || settings.WallpaperOnly)
            snowmanFrame = owlFrame = null;

        snowman.Draw(snowmanFrame, layout.Scale, layout.View);
        owl.Draw(owlFrame, layout.Scale, layout.View);
    }

    private bool Covered(ActorFrame? actor, YardLayout layout) =>
        actor is { } frame && foreground.IntersectsWith(frame.Footprint(layout.Scale, Point.Empty));

    private YardLayout PickLayout()
    {
        var screen = Screen.AllScreens.FirstOrDefault(item => item.DeviceName == settings.Monitor)
            ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        return new(screen.Bounds, screen.WorkingArea);
    }

    private void Save()
    {
        saved = (settings.Ornaments, settings.Snow);
        if (!settings.Save())
            tray.ShowBalloonTip(2500, "DesctopNewYear", "Не удалось сохранить состояние двора.", ToolTipIcon.Warning);
    }

    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        YardArt.Prepare(graphics);
        graphics.TranslateTransform(16, 31);
        graphics.ScaleTransform(.23f, .23f);
        YardArt.Snowman(graphics, new(PointF.Empty, false, Pose.Rest, 1, -1, 0));
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            timer.Stop();
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
            owl.Dispose();
            snowman.Dispose();
            wallpaper?.Dispose();
        }
        base.Dispose(disposing);
    }
}
