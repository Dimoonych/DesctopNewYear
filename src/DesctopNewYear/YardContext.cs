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
    private readonly Icon icon;
    private readonly NotifyIcon tray;
    private readonly ExitShortcut exitShortcut;
    private readonly YardScene scene;
    private WallpaperWindow? wallpaper;
    private SpriteWindow? snowman, owl, visitingOwl;
    private WindowPerch? perchCandidate;
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
        exitShortcut = new ExitShortcut(ExitThread);
        ConfigureMenu();

        tray.DoubleClick += (_, _) => TogglePause();

        timer.Tick += (_, _) => Tick();
        Tick();
        timer.Start();
    }

    private void ConfigureMenu()
    {
        var exit = Item("Выйти", ExitThread);
        exit.ShortcutKeyDisplayString = exitShortcut.Registered ? ExitShortcut.Display : "";
        exit.ToolTipText = exitShortcut.Registered ? "" : "Горячая клавиша занята другим приложением.";
        menu.Items.AddRange([
            status, new ToolStripSeparator(),
            Item("Позвать снеговика на прогулку", () => Run(scene.StartWalk)),
            Item("Сходить за игрушкой", () => Run(scene.StartToy)),
            Item("Позвать сову", () => Run(scene.CallOwl)),
            Item("Сова, на окно", () => Run(scene.AskForPerch)),
            Item("Сова, верни шляпу!", () => Run(scene.CallHatPrank)),
            Item("Включить снегопад", () => Run(scene.StartSnow)),
            Item("Позвать снеговика с лопатой", () => Run(scene.CallCleaner)),
            Item("Выпустить трактор", () => Run(scene.CallTractor)),
            Item("Убрать снег сразу", () => Run(scene.ClearSnow)),
            new ToolStripSeparator(),
            Item("Освещение…", ConfigureLight),
            monitors, wallpaperOnly, pause,
            exit
        ]);
        pause.Click += (_, _) => TogglePause();
        wallpaperOnly.Click += (_, _) =>
        {
            settings.WallpaperOnly = !settings.WallpaperOnly;
            if (settings.WallpaperOnly)
                scene.DismissWindowPerch(showDeparture: false);
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
        if (paused)
            scene.DismissWindowPerch(showDeparture: false);
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
                ReleaseDesktop();
                nextAttach = 0;
            }
            var foreground = Native.ForegroundApp();
            var bounds = Native.FrameBounds(foreground);
            fullscreen = Native.IsFullscreen(bounds, layout.View);
            perchCandidate = Native.FindPerch(foreground, bounds, layout);
            if (fullscreen)
                scene.DismissWindowPerch(showDeparture: false);
        }

        if (elapsed >= nextAttach &&
            (wallpaper is not { Available: true } || snowman is not { Available: true } ||
             owl is not { Available: true } || visitingOwl is not { Available: true }))
        {
            nextAttach = elapsed + 12;
            AttachDesktop();
        }

        var now = DateTime.Now;
        owl?.UpdateDrag();
        if (!paused && !fullscreen)
            scene.Advance(seconds, Daylight.At(now, settings), now, PickPerch());
        Render(now);
        if (saved != (settings.Ornaments, settings.Snow))
            Save();

        var interval = scene.InMotion && !paused && !fullscreen ? 40 : 650;
        if (interval != timer.Interval)
            timer.Interval = interval;
    }

    private WindowPerch? PickPerch()
    {
        if (settings.WallpaperOnly || visitingOwl is not { Available: true })
            return null;

        if (scene.CurrentPerch is not { } current)
            return perchCandidate;

        // During a visit, look at the exact target every animation tick so even small moves scare her off.
        if (Native.GetForegroundWindow() != current.Window)
            return null;

        return Native.FindPerch(current.Window, Native.FrameBounds(current.Window), scene.Layout);
    }

    private void AttachDesktop()
    {
        ReleaseSprites();
        try
        {
            if (wallpaper is not { Available: true })
            {
                ReleaseDesktop();
                var host = DesktopHost.TryFind();
                if (!host.Available)
                    return;
                wallpaper = new WallpaperWindow(host, scene);
            }

            snowman = new SpriteWindow(owl: false, wallpaper.IconHost);
            owl = new SpriteWindow(owl: true, wallpaper.IconHost);
            visitingOwl = new SpriteWindow(owl: true, desktop: 0);
            snowman.Clicked += _ => Run(scene.GreetSnowman);
            owl.Clicked += cursor => Run(() => scene.TouchOwl(cursor));
            owl.Dragged += scene.DragOwl;
            owl.Dropped += _ => scene.DropOwl();
            owl.CaptureLost += scene.CancelDrag;
        }
        catch (Win32Exception)
        {
            // Explorer may be restarting. Draw on the wallpaper while the input layers recover.
            ReleaseSprites();
        }
    }

    private void Render(DateTime now)
    {
        if (wallpaper is not { Available: true })
            return;

        var layout = scene.Layout;
        var snowmanFrame = scene.Snowman;
        var owlFrame = scene.Owl;
        var isVisiting = !settings.WallpaperOnly && !fullscreen && !paused &&
            visitingOwl is { Available: true } && scene.OwlOverWindows;
        var interactive = !settings.WallpaperOnly && snowman is { Available: true } && owl is { Available: true };

        try
        {
            snowman?.Draw(interactive && !fullscreen ? snowmanFrame : null, layout.Scale, layout.View);
            owl?.Draw(interactive && !fullscreen && !isVisiting ? owlFrame : null, layout.Scale, layout.View);
            visitingOwl?.Draw(isVisiting ? owlFrame : null, layout.Scale, layout.View);
        }
        catch (Win32Exception)
        {
            ReleaseSprites();
            isVisiting = false;
        }
        wallpaper.Draw(Daylight.At(now, settings), settings, snowmanFrame,
            isVisiting ? null : owlFrame, !fullscreen && !paused);
    }

    private void ReleaseSprites()
    {
        owl?.Dispose();
        snowman?.Dispose();
        visitingOwl?.Dispose();
        owl = snowman = visitingOwl = null;
    }

    private void ReleaseDesktop()
    {
        ReleaseSprites();
        wallpaper?.Dispose();
        wallpaper = null;
    }

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
            exitShortcut.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
            ReleaseDesktop();
        }
        base.Dispose(disposing);
    }
}
