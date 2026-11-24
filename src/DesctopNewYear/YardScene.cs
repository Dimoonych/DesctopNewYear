namespace DesctopNewYear;

internal enum Pose { Rest, Walk, Hang, Shovel, Fly, React }

internal readonly record struct ActorFrame(PointF Feet, bool Owl, Pose Pose, int Direction, int Toy, float Time)
{
    public Rectangle Footprint(float scale, Point origin)
    {
        var half = (Owl ? 54 : 64) * scale;
        var height = (Owl ? 99 : 139) * scale;
        return Rectangle.FromLTRB(
            (int)Math.Floor(Feet.X - origin.X - half),
            (int)Math.Floor(Feet.Y - origin.Y - height + 4 * scale),
            (int)Math.Ceiling(Feet.X - origin.X + half),
            (int)Math.Ceiling(Feet.Y - origin.Y + 4 * scale));
    }

    public void Paint(Graphics graphics)
    {
        if (Owl)
            YardArt.Owl(graphics, this);
        else
        {
            graphics.ScaleTransform(Direction, 1);
            YardArt.Snowman(graphics, this);
        }
    }
}

internal sealed class YardScene
{
    private enum SnowmanAction { Rest, Leave, Away, Carry, Hang, Return, ToPath, Clean }
    private enum OwlAction { Roam, Steal, ReturnToy, ShakeBranch }

    private readonly YardSettings settings;
    private readonly Random random;
    private SnowmanAction snowmanAction;
    private OwlAction owlAction;
    private PointF snowman, owl, owlStart, owlTarget;
    private int direction = -1, owlLook, held = -1, stolen = -1;
    private float time, snowmanTime, owlTime, owlFlight, snowmanReaction, owlReaction;
    private float nextToy = 5, nextOwl = 12, nextSnow = 32, nextSmoke = 8;
    private float snowfall, smoke, powder;
    private bool settled, askedToClean, mouseDown, owlDragged, swayUsed;
    private Point dragStart;
    private PointF dragOffset;

    public YardLayout Layout { get; private set; }
    public bool Snowing => snowfall > 0;
    public bool Smoking => smoke > 0;
    public bool PowderFalling => powder > 0;
    public int StolenOrnament => stolen;
    public bool InMotion => snowmanAction is not (SnowmanAction.Rest or SnowmanAction.Away) ||
        snowmanReaction > 0 || owlFlight > 0 || owlReaction > 0 || mouseDown ||
        Snowing || Smoking || PowderFalling;

    public YardScene(YardSettings settings, YardLayout layout, Random? random = null)
    {
        this.settings = settings;
        this.random = random ?? Random.Shared;
        Layout = layout;
        snowman = layout.Home;
        owl = layout.Roof;
    }

    public void SetLayout(YardLayout layout)
    {
        Layout = layout;
        snowman = snowmanAction is SnowmanAction.Away or SnowmanAction.Carry ? Offscreen : layout.Home;
        if (snowmanAction == SnowmanAction.Hang)
            snowman = layout.Tree;
        if (snowmanAction == SnowmanAction.Clean)
            snowman = layout.Path;

        owl = layout.Roof;
        owlFlight = 0;
        owlAction = OwlAction.Roam;
        stolen = -1;
        mouseDown = owlDragged = false;
    }

    public ActorFrame? Snowman
    {
        get
        {
            if (snowmanAction == SnowmanAction.Away)
                return null;

            var pose = snowmanReaction > 0 ? Pose.React : snowmanAction switch
            {
                SnowmanAction.Clean => powder > .5f ? Pose.Rest : Pose.Shovel,
                SnowmanAction.Hang => Pose.Hang,
                SnowmanAction.Rest => Pose.Rest,
                _ => Pose.Walk
            };
            return new(snowman, false, pose, direction, held, pose is Pose.Walk or Pose.Shovel ? time : 0);
        }
    }

    public ActorFrame Owl => new(owl, true, owlFlight > 0 && !mouseDown ? Pose.Fly :
        owlReaction > 0 || mouseDown ? Pose.React : Pose.Rest, owlLook, stolen, owlFlight > 0 && !mouseDown ? time : 0);

    public bool SnowmanInYard => snowmanAction is SnowmanAction.Rest or SnowmanAction.Hang or SnowmanAction.Clean;
    public bool OwlInYard => owlFlight == 0 && !mouseDown &&
        (owl == Layout.Roof || owl == Layout.Branch || owl == Layout.Lantern);

    private PointF Offscreen => Layout.Screen((Layout.View.Width - Layout.Anchor.X) / Layout.Scale + 76, 2);

    public void CallCleaner() => askedToClean = settings.Snow > 0;

    public void ClearSnow()
    {
        snowfall = powder = 0;
        settings.Snow = 0;
        askedToClean = false;
        nextSnow = time + random.Next(100, 170);
        if (owlAction == OwlAction.ShakeBranch)
            owlAction = OwlAction.Roam;
        if (snowmanAction is SnowmanAction.ToPath or SnowmanAction.Clean)
            SetSnowman(held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return);
    }

    public void StartSnow()
    {
        if (Snowing)
            return;

        snowfall = 20;
        settled = false;
    }

    public void StartToy()
    {
        if (snowmanAction == SnowmanAction.Rest)
            nextToy = time;
    }

    public void GreetSnowman()
    {
        if (snowmanAction is not (SnowmanAction.Away or SnowmanAction.Clean or SnowmanAction.Hang))
            snowmanReaction = .85f;
    }

    public void TouchOwl(Point cursor)
    {
        mouseDown = true;
        owlDragged = false;
        dragStart = cursor;
        dragOffset = new(owl.X - cursor.X, owl.Y - cursor.Y);
        owlLook = cursor.X < owl.X ? -1 : 1;
        owlReaction = 1.2f;
    }

    public void DragOwl(Point cursor)
    {
        if (!mouseDown || !owlDragged && Math.Abs(cursor.X - dragStart.X) + Math.Abs(cursor.Y - dragStart.Y) < 5)
            return;

        owlDragged = true;
        owlFlight = 0;
        owlAction = OwlAction.Roam;
        var area = Layout.Work;
        owl = new(Math.Clamp(cursor.X + dragOffset.X, area.Left + 30, area.Right - 30),
            Math.Clamp(cursor.Y + dragOffset.Y, area.Top + 50, area.Bottom - 10));
    }

    public void DropOwl(Rectangle foreground)
    {
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        var perch = Perches(foreground).MinBy(point => Distance(owl, point));
        FlyTo(perch, perch == Layout.Branch && stolen >= 0 ? OwlAction.ReturnToy : OwlAction.Roam);
    }

    public void CancelDrag()
    {
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        FlyTo(Layout.Roof, OwlAction.Roam);
    }

    public void Advance(float seconds, Daylight daylight, Rectangle foreground, DateTime localTime)
    {
        time += seconds;
        snowmanTime += seconds;
        snowmanReaction = Math.Max(0, snowmanReaction - seconds);
        owlReaction = Math.Max(0, owlReaction - seconds);
        powder = Math.Max(0, powder - seconds);
        AdvanceWeather(seconds, daylight);
        AdvanceSnowman(seconds, localTime.Hour >= 23 || localTime.Hour < 7);
        if (!mouseDown)
            AdvanceOwl(seconds, foreground);
    }

    private void AdvanceWeather(float seconds, Daylight daylight)
    {
        if (!Snowing && time >= nextSnow)
            StartSnow();
        if (Snowing)
        {
            snowfall = Math.Max(0, snowfall - seconds);
            if (snowfall <= 6 && !settled)
            {
                settings.Snow = Math.Min(3, settings.Snow + 1);
                settled = true;
            }
            if (!Snowing)
                nextSnow = time + random.Next(105, 185);
        }

        if (daylight.WindowLit && time >= nextSmoke)
        {
            smoke = 7;
            nextSmoke = time + random.Next(40, 82);
        }
        smoke = daylight.WindowLit ? Math.Max(0, smoke - seconds) : 0;
    }

    private void AdvanceSnowman(float seconds, bool late)
    {
        var clean = askedToClean || settings.Snow == 3;
        if (clean && snowmanAction is SnowmanAction.Rest or SnowmanAction.Carry or SnowmanAction.Return)
        {
            askedToClean = false;
            swayUsed = false;
            SetSnowman(SnowmanAction.ToPath);
        }

        if (snowmanReaction > 0)
            return;

        switch (snowmanAction)
        {
            case SnowmanAction.Rest when time >= nextToy:
                SetSnowman(SnowmanAction.Leave);
                break;
            case SnowmanAction.Leave when WalkTo(Offscreen, seconds):
                SetSnowman(SnowmanAction.Away);
                break;
            case SnowmanAction.Away when snowmanTime >= 3.2f:
                held = settings.Ornaments < 7 ? settings.Ornaments : random.Next(0, 6);
                SetSnowman(SnowmanAction.Carry);
                break;
            case SnowmanAction.Carry when WalkTo(Layout.Tree, seconds):
                direction = -1;
                SetSnowman(SnowmanAction.Hang);
                break;
            case SnowmanAction.Hang when snowmanTime >= 1.3f:
                settings.Ornaments = Math.Min(7, settings.Ornaments + 1);
                held = -1;
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.Return when WalkTo(Layout.Home, seconds):
                nextToy = time + (late ? random.Next(80, 155) : random.Next(27, 55));
                SetSnowman(SnowmanAction.Rest);
                break;
            case SnowmanAction.ToPath when WalkTo(Layout.Path, seconds):
                SetSnowman(SnowmanAction.Clean);
                break;
            case SnowmanAction.Clean when snowmanTime >= 1.3f:
                settings.Snow = Math.Max(0, settings.Snow - 1);
                if (settings.Snow == 0)
                    SetSnowman(held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return);
                else
                    snowmanTime = 0;
                break;
        }
    }

    private bool WalkTo(PointF target, float seconds)
    {
        var distance = Distance(snowman, target);
        var stride = seconds * 168 * Layout.Scale;
        if (distance <= stride)
        {
            snowman = target;
            return true;
        }

        direction = target.X > snowman.X ? 1 : -1;
        snowman = Lerp(snowman, target, stride / distance);
        return false;
    }

    private void SetSnowman(SnowmanAction action)
    {
        snowmanAction = action;
        snowmanTime = 0;
    }

    private IEnumerable<PointF> Perches(Rectangle foreground)
    {
        yield return Layout.Roof;
        yield return Layout.Lantern;
        yield return Layout.Branch;
        var visible = Rectangle.Intersect(foreground, Layout.Work);
        if (visible.Width > 140 && foreground.Top > Layout.Work.Top + 82 * Layout.Scale)
            yield return new(visible.Right - 74 * Layout.Scale, foreground.Top + 3);
    }

    private void AdvanceOwl(float seconds, Rectangle foreground)
    {
        if (owlFlight > 0)
        {
            owlTime += seconds;
            var progress = Math.Min(1, owlTime / owlFlight);
            owl = Lerp(owlStart, owlTarget, progress);
            owl.Y -= MathF.Sin(progress * MathF.PI) * 75 * Layout.Scale;
            if (progress < 1)
                return;

            owl = owlTarget;
            owlFlight = 0;
            FinishFlight();
            return;
        }

        if (snowmanAction == SnowmanAction.Clean && !swayUsed && settings.Snow >= 2 && random.Next(3) != 0)
        {
            swayUsed = true;
            if (owl == Layout.Branch)
            {
                owlAction = OwlAction.ShakeBranch;
                FinishFlight();
            }
            else
                FlyTo(Layout.Branch, OwlAction.ShakeBranch);
            return;
        }
        if (time < nextOwl)
            return;

        if (stolen >= 0)
            FlyTo(Layout.Branch, OwlAction.ReturnToy);
        else if (snowmanAction is SnowmanAction.Leave or SnowmanAction.Away && settings.Ornaments is > 0 and < 7)
            FlyTo(Layout.Branch, OwlAction.Steal);
        else
        {
            var choices = Perches(foreground).Where(point => point != owl).ToArray();
            FlyTo(choices[random.Next(choices.Length)], OwlAction.Roam);
        }
    }

    private void FlyTo(PointF target, OwlAction action)
    {
        owlStart = owl;
        owlTarget = target;
        owlTime = 0;
        owlFlight = Math.Clamp(Distance(owl, target) / (355 * Layout.Scale), .45f, 2.7f);
        owlAction = action;
    }

    private void FinishFlight()
    {
        nextOwl = time + random.Next(14, 34);
        switch (owlAction)
        {
            case OwlAction.Steal when settings.Ornaments is > 0 and < 7:
                stolen = settings.Ornaments - 1;
                FlyTo(Layout.Lantern, OwlAction.Roam);
                break;
            case OwlAction.ReturnToy:
                stolen = -1;
                break;
            case OwlAction.ShakeBranch when snowmanAction == SnowmanAction.Clean:
                settings.Snow = Math.Min(3, settings.Snow + 1);
                powder = .95f;
                snowmanTime = 0;
                break;
        }
        if (stolen >= 0)
            nextOwl = time + 8;
    }

    public void DrawWeather(Graphics graphics, IReadOnlyList<Snowflake> flakes)
    {
        if (Snowing)
            DrawSnow(graphics, flakes);

        var chimney = Layout.Local(-76, -198);
        if (Smoking)
        {
            for (var i = 0; i < 6; i++)
            {
                var age = 7 - smoke - i * .52f;
                if (age < 0 || age > 3.8f)
                    continue;
                var diameter = (7 + age * 6) * Layout.Scale;
                var alpha = (int)(32 * Math.Sin(Math.PI * age / 3.8f));
                using var hazy = new SolidBrush(Color.FromArgb(alpha, Color.White));
                graphics.FillEllipse(hazy, chimney.X + age * 8 * Layout.Scale - diameter / 2,
                    chimney.Y - (9 + age * 16) * Layout.Scale, diameter * 1.3f, diameter);
            }
        }

        if (!PowderFalling)
            return;

        var branch = Layout.Local(-377, -104);
        using var falling = new SolidBrush(Color.FromArgb((int)(190 * powder), Color.White));
        for (var i = 0; i < 18; i++)
        {
            var elapsed = 1 - powder;
            var x = branch.X + ((i * 43 % 38) - 19 + (i % 3 - 1) * elapsed * 25) * Layout.Scale;
            var y = branch.Y + (i % 11 + elapsed * elapsed * 120) * Layout.Scale;
            graphics.FillEllipse(falling, x, y, 2.8f * Layout.Scale, 2.8f * Layout.Scale);
        }
    }

    public Rectangle WeatherFootprint()
    {
        var region = Snowing ? Layout.SnowArea : Rectangle.Empty;
        if (Smoking)
        {
            var chimney = Layout.Local(-88, -280);
            var smokeArea = new Rectangle((int)chimney.X, (int)chimney.Y,
                (int)Math.Ceiling(90 * Layout.Scale), (int)Math.Ceiling(90 * Layout.Scale));
            region = region.IsEmpty ? smokeArea : Rectangle.Union(region, smokeArea);
        }
        if (PowderFalling)
        {
            var branch = Layout.Local(-433, -111);
            var powderArea = new Rectangle((int)branch.X, (int)branch.Y,
                (int)Math.Ceiling(113 * Layout.Scale), (int)Math.Ceiling(134 * Layout.Scale));
            region = region.IsEmpty ? powderArea : Rectangle.Union(region, powderArea);
        }
        return region;
    }

    private void DrawSnow(Graphics graphics, IReadOnlyList<Snowflake> flakes)
    {
        var brightness = Math.Clamp(Math.Min(20 - snowfall, snowfall) / 3, 0, 1);
        var area = Layout.SnowArea;
        var lantern = Layout.Local(-465, -78);
        using var brush = new SolidBrush(Color.White);
        foreach (var flake in flakes)
        {
            var elapsed = 20 - snowfall;
            var y = (flake.Y * area.Height + elapsed * flake.Speed * Layout.Scale) % area.Height;
            var x = area.X + (flake.X * area.Width + elapsed * 12 + 8 * MathF.Sin(elapsed + flake.Y * 17)) % area.Width;
            if (x < area.X)
                x += area.Width;
            var glow = Distance(new(x, y), lantern) < 112 * Layout.Scale;
            brush.Color = Color.FromArgb((int)((glow ? 210 : 131) * brightness * flake.Intensity),
                Color.White);
            var size = flake.Size * Layout.Scale;
            graphics.FillEllipse(brush, x, y, size, size);
        }
    }

    private static PointF Lerp(PointF from, PointF to, float t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
    private static float Distance(PointF a, PointF b) => float.Hypot(b.X - a.X, b.Y - a.Y);
}

internal readonly record struct Snowflake(float X, float Y, float Speed, float Size, float Intensity);
