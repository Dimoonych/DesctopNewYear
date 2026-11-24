namespace DesctopNewYear;

internal enum Pose { Rest, Walk, Hang, Shovel, Fly, React, Tractor, Throw, PickUp }

internal readonly record struct ActorFrame(PointF Feet, bool Owl, Pose Pose, int Direction, int Toy, float Time,
    bool NoHat = false, bool HoldingHat = false, bool Plowing = false)
{
    public int ArtWidth => Pose == Pose.Tractor ? 214 : Owl ? 108 : 128;
    public int ArtHeight => Pose == Pose.Tractor ? 122 : Owl ? 99 : 139;
    public float WalkStep => Pose == Pose.Walk ? MathF.Sin(Time * 14) : 0;
    public float Bob => Math.Abs(WalkStep) * 2.5f;

    public Rectangle Footprint(float scale, Point origin)
    {
        var half = ArtWidth * scale / 2;
        var height = ArtHeight * scale;
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
            if (Pose == Pose.Tractor)
                YardArt.Tractor(graphics, this);
            else
                YardArt.Snowman(graphics, this);
        }
    }
}

internal sealed class YardScene
{
    private enum SnowmanAction
    {
        Rest, WalkAround, Inspect, Leave, Away, Carry, Hang, Return, ToPath, Clean,
        FetchTractor, TractorAway, Plow, DriveOut, FindHat, PickHat
    }
    private enum OwlAction { Roam, Steal, ReturnToy, ShakeBranch, Leave, TakeHat, DropHat, AtWindow, LeaveWindow }
    private enum Chore { Walk, Tree, Lantern }

    private readonly YardSettings settings;
    private readonly Random random;
    private SnowmanAction snowmanAction;
    private OwlAction owlAction;
    private PointF snowman, owl, owlStart, owlTarget;
    private int direction = -1, owlLook, held = -1, stolen = -1;
    private double time;
    private float snowmanTime, owlTime, owlFlight, snowmanReaction, owlReaction;
    private double nextToy = 5, nextWalk = 3, nextOwl = 8, nextSnow = 16, nextSmoke = 8, nextHatTheft = 55;
    private double nextShot;
    private bool askedForToy, askedForWalk, askedForTractor, askedForHat, owlAway;
    private bool askedForPerch, visibleDeparture;
    private double nextWindowVisit;
    private bool hatMissing, owlHasHat;
    private bool hatGrabbed, hatDragged;
    private SnowmanAction afterHat = SnowmanAction.Return;
    private int plowed, snowballsLeft;
    private float hatFall, hatFallDuration, hatSpin, shotAge, throwPose;
    private PointF strollStop, hatTarget, hatFallOrigin, shotStart, shotTarget;
    private Point hatPress;
    private PointF hatPressPosition, grabbedHat;
    private Chore chore;
    private float snowfall, smoke, powder;
    private bool settled, askedToClean, mouseDown, owlDragged, swayUsed;
    private Point dragStart;
    private PointF dragOffset;

    public YardLayout Layout { get; private set; }
    public bool Snowing => snowfall > 0;
    public bool Smoking => smoke > 0;
    public bool PowderFalling => powder > 0;
    public int StolenOrnament => stolen;
    public WindowPerch? CurrentPerch { get; private set; }
    public bool OwlOverWindows => !owlAway &&
        (CurrentPerch is { } perch && Distance(owl, perch.Feet) < 153 * Layout.Scale ||
         owlAction == OwlAction.LeaveWindow && visibleDeparture && owlFlight > 0 && owlTime < .65f);
    public bool InMotion => snowmanAction is not (SnowmanAction.Rest or SnowmanAction.Away) ||
        snowmanReaction > 0 || owlFlight > 0 || owlReaction > 0 || mouseDown || hatGrabbed || CurrentPerch.HasValue ||
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
        snowman = snowmanAction is SnowmanAction.Away or SnowmanAction.Carry or
            SnowmanAction.TractorAway or SnowmanAction.Plow ? Offscreen : layout.Home;
        if (snowmanAction == SnowmanAction.Hang)
            snowman = layout.Tree;
        if (snowmanAction == SnowmanAction.Clean)
            snowman = layout.Path;

        owl = layout.Roof;
        owlFlight = 0;
        owlAction = OwlAction.Roam;
        owlAway = false;
        CurrentPerch = null;
        stolen = -1;
        mouseDown = owlDragged = false;
        hatMissing = owlHasHat = false;
        hatGrabbed = hatDragged = false;
        hatFall = shotAge = throwPose = 0;
        if (snowmanAction is SnowmanAction.FindHat or SnowmanAction.PickHat)
            FinishHatRecovery();
        else if (snowmanAction is SnowmanAction.WalkAround or SnowmanAction.Inspect)
            SetSnowman(SnowmanAction.Rest);
    }

    public ActorFrame? Snowman
    {
        get
        {
            if (snowmanAction is SnowmanAction.Away or SnowmanAction.TractorAway)
                return null;

            var pose = snowmanAction switch
            {
                SnowmanAction.Plow or SnowmanAction.DriveOut => Pose.Tractor,
                SnowmanAction.FindHat when throwPose > 0 => Pose.Throw,
                SnowmanAction.PickHat => Pose.PickUp,
                _ when snowmanReaction > 0 => Pose.React,
                SnowmanAction.Clean => powder > .5f ? Pose.Rest : Pose.Shovel,
                SnowmanAction.Hang => Pose.Hang,
                SnowmanAction.Inspect => chore == Chore.Walk ? Pose.Rest : Pose.React,
                SnowmanAction.Rest => Pose.Rest,
                _ => Pose.Walk
            };
            return new(snowman, false, pose, direction, held,
                pose is Pose.Walk or Pose.Shovel or Pose.Tractor ? AnimationTime : 0,
                NoHat: hatMissing, Plowing: snowmanAction == SnowmanAction.Plow);
        }
    }

    public ActorFrame? Owl => owlAway ? null : new(owl, true, owlFlight > 0 && !mouseDown ? Pose.Fly :
        owlReaction > 0 || mouseDown ? Pose.React : Pose.Rest, owlLook, stolen,
        owlFlight > 0 && !mouseDown ? AnimationTime : 0, HoldingHat: owlHasHat);

    private float AnimationTime => (float)(time % (2 * Math.PI));
    private PointF Offscreen => new(Layout.View.Right + 113 * Layout.Scale, Layout.Home.Y);
    private PointF LeftEdge => new(Layout.View.Left - 60 * Layout.Scale, Layout.View.Top + Layout.View.Height * .30f);
    private PointF RightEdge => new(Layout.View.Right + 60 * Layout.Scale, Layout.View.Top + Layout.View.Height * .25f);

    public void CallCleaner() => askedToClean = settings.Snow > 0;

    public void CallTractor() => askedForTractor = true;

    public void StartWalk() => askedForWalk = true;

    public void CallOwl() => nextOwl = time;

    public void AskForPerch()
    {
        askedForPerch = true;
        nextOwl = time;
    }

    public void DismissWindowPerch(bool showDeparture)
    {
        if (!CurrentPerch.HasValue)
            return;

        visibleDeparture = showDeparture && OwlOverWindows;
        CurrentPerch = null;
        nextWindowVisit = time + 20;
        FlyTo(Layout.Roof, OwlAction.LeaveWindow);
    }

    public void CallHatPrank()
    {
        if (!hatMissing && owlAction != OwlAction.TakeHat)
            askedForHat = true;
    }

    public void ClearSnow()
    {
        snowfall = powder = 0;
        settings.Snow = 0;
        askedToClean = false;
        askedForTractor = false;
        nextSnow = time + random.Next(43, 76);
        if (owlAction == OwlAction.ShakeBranch)
            owlAction = OwlAction.Roam;
        if (snowmanAction is SnowmanAction.ToPath or SnowmanAction.Clean)
            SetSnowman(held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return);
        if (snowmanAction == SnowmanAction.Plow)
            SetSnowman(SnowmanAction.DriveOut);
        else if (snowmanAction is SnowmanAction.FetchTractor or SnowmanAction.TractorAway)
            SetSnowman(SnowmanAction.Return);
    }

    public void StartSnow()
    {
        if (Snowing)
            return;

        snowfall = 20;
        settled = false;
    }

    public void StartToy() => askedForToy = true;

    public void GreetSnowman()
    {
        if (snowmanAction is not (SnowmanAction.Away or SnowmanAction.Clean or SnowmanAction.Hang or
            SnowmanAction.Plow or SnowmanAction.DriveOut or SnowmanAction.TractorAway))
            snowmanReaction = .85f;
        if (snowmanAction == SnowmanAction.Rest)
            StartWalk();
    }

    public bool TryGrabHat(Point cursor)
    {
        if (hatMissing || hatGrabbed || owlAction == OwlAction.TakeHat ||
            Snowman is not { Pose: not Pose.Tractor } actor)
            return false;

        var scale = Layout.Scale;
        var brim = new PointF(actor.Feet.X, actor.Feet.Y - (YardArt.SnowmanHatHeight + actor.Bob) * scale);
        if (Math.Abs(cursor.X - brim.X) > 26 * scale ||
            cursor.Y < brim.Y - 24 * scale || cursor.Y > brim.Y + 4 * scale)
            return false;

        hatGrabbed = true;
        hatPress = cursor;
        hatPressPosition = brim;
        return true;
    }

    public void DragGrabbedHat(Point cursor)
    {
        var dx = cursor.X - hatPress.X;
        var dy = cursor.Y - hatPress.Y;
        if (!hatGrabbed || !hatDragged && Math.Abs(dx) + Math.Abs(dy) < 5)
            return;

        hatDragged = hatMissing = true;
        var work = Layout.Work;
        var scale = Layout.Scale;
        grabbedHat = new(
            Math.Clamp(hatPressPosition.X + dx, work.Left + 27 * scale, work.Right - 27 * scale),
            Math.Clamp(hatPressPosition.Y + dy, work.Top + 26 * scale, work.Bottom - 5 * scale));
    }

    public void DropGrabbedHat(Point cursor)
    {
        if (!hatGrabbed)
            return;

        DragGrabbedHat(cursor);
        if (!hatDragged)
        {
            hatGrabbed = false;
            GreetSnowman();
            return;
        }

        ReleaseGrabbedHat();
    }

    public void ReleaseGrabbedHat()
    {
        hatGrabbed = false;
        if (!hatDragged)
            return;

        hatDragged = false;
        afterHat = snowmanAction switch
        {
            SnowmanAction.FetchTractor => SnowmanAction.FetchTractor,
            SnowmanAction.ToPath or SnowmanAction.Clean => SnowmanAction.ToPath,
            SnowmanAction.Leave => SnowmanAction.Leave,
            _ when held >= 0 => SnowmanAction.Carry,
            _ => SnowmanAction.Return
        };
        var landing = new PointF(grabbedHat.X, Layout.Home.Y);
        var distance = landing.Y - grabbedHat.Y;
        var origin = distance > 0 ? grabbedHat : landing;
        var duration = distance > 0 ? Math.Clamp(MathF.Sqrt(2 * distance / (1010 * Layout.Scale)), .12f, 1.1f) : 0;
        BeginHatFall(origin, landing, duration, spin: 34);
        snowballsLeft = 0;
        snowmanReaction = 0;
        SetSnowman(SnowmanAction.FindHat);
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
        CurrentPerch = null;
        owlAction = OwlAction.Roam;
        var area = Layout.Work;
        owl = new(Math.Clamp(cursor.X + dragOffset.X, area.Left + 30, area.Right - 30),
            Math.Clamp(cursor.Y + dragOffset.Y, area.Top + 50, area.Bottom - 10));
    }

    public void DropOwl()
    {
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        if (owlHasHat)
            ReleaseHat(aimUnderOwl: true);
        var perch = Perches().MinBy(point => Distance(owl, point));
        FlyTo(perch, perch == Layout.Branch && stolen >= 0 ? OwlAction.ReturnToy : OwlAction.Roam);
    }

    public void CancelDrag()
    {
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        if (owlHasHat)
            ReleaseHat(aimUnderOwl: true);
        FlyTo(Layout.Roof, OwlAction.Roam);
    }

    public void Advance(float seconds, Daylight daylight, DateTime localTime, WindowPerch? window = null)
    {
        time += seconds;
        if (!hatGrabbed)
            snowmanTime += seconds;
        snowmanReaction = Math.Max(0, snowmanReaction - seconds);
        owlReaction = Math.Max(0, owlReaction - seconds);
        powder = Math.Max(0, powder - seconds);
        throwPose = Math.Max(0, throwPose - seconds);
        hatFall = Math.Max(0, hatFall - seconds);
        shotAge = Math.Max(0, shotAge - seconds);
        AdvanceWeather(seconds, daylight);
        AdvanceSnowman(seconds, localTime.Hour >= 23 || localTime.Hour < 7);
        if (!mouseDown)
            AdvanceOwl(seconds, window);
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
                nextSnow = time + random.Next(43, 76);
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
        if (hatGrabbed || owlAction == OwlAction.TakeHat && owlFlight > 0)
            return;

        var free = snowmanAction is SnowmanAction.Rest or SnowmanAction.Return or
            SnowmanAction.WalkAround or SnowmanAction.Inspect;
        if ((askedForTractor || settings.Snow == 3) &&
            (free || snowmanAction is SnowmanAction.ToPath or SnowmanAction.Clean))
        {
            askedForTractor = askedToClean = false;
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.FetchTractor);
        }
        else if (askedToClean && (free || snowmanAction == SnowmanAction.Carry))
        {
            askedToClean = false;
            swayUsed = false;
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.ToPath);
        }

        if (snowmanReaction > 0)
            return;

        switch (snowmanAction)
        {
            case SnowmanAction.Rest when askedForToy || !askedForWalk && time >= nextToy:
                askedForToy = false;
                SetSnowman(SnowmanAction.Leave);
                break;
            case SnowmanAction.Rest when askedForWalk || time >= nextWalk:
                BeginWalk(late);
                break;
            case SnowmanAction.WalkAround when WalkTo(strollStop, seconds):
                if (chore == Chore.Tree)
                    powder = .95f;
                SetSnowman(SnowmanAction.Inspect);
                break;
            case SnowmanAction.Inspect when snowmanTime >= 1.7f:
                SetSnowman(SnowmanAction.Return);
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
                nextToy = time + (late ? random.Next(40, 66) : random.Next(22, 41));
                nextWalk = Math.Max(nextWalk, time + 4);
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.Return when WalkTo(Layout.Home, seconds):
                nextWalk = Math.Max(nextWalk, time + (late ? 5 : 2.5));
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
            case SnowmanAction.FetchTractor when WalkTo(Offscreen, seconds):
                SetSnowman(SnowmanAction.TractorAway);
                break;
            case SnowmanAction.TractorAway when snowmanTime >= 2.4f:
                plowed = 0;
                SetSnowman(SnowmanAction.Plow);
                break;
            case SnowmanAction.Plow:
                AdvanceTractor(seconds);
                break;
            case SnowmanAction.DriveOut when WalkTo(Offscreen, seconds, 233):
                SetSnowman(held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return);
                break;
            case SnowmanAction.FindHat:
                AdvanceChase(seconds);
                break;
            case SnowmanAction.PickHat when snowmanTime >= .52f:
                FinishHatRecovery();
                break;
        }
    }

    private void BeginWalk(bool late)
    {
        askedForWalk = false;
        nextWalk = time + (late ? random.Next(15, 25) : random.Next(7, 13));
        chore = (Chore)(((int)chore + 1) % 3);
        strollStop = chore switch
        {
            Chore.Tree => Layout.Tree,
            Chore.Lantern => Layout.Screen(-489, 11),
            _ => new(Layout.View.Left + Layout.View.Width * (.25f + random.NextSingle() * .29f),
                Layout.Home.Y + 8 * Layout.Scale)
        };
        SetSnowman(SnowmanAction.WalkAround);
    }

    private void AdvanceTractor(float seconds)
    {
        var destination = Layout.Screen(-523, 22);
        var arrived = WalkTo(destination, seconds, 210);
        var distance = Math.Max(1, Layout.Home.X - destination.X);
        var passes = Math.Clamp((int)((Layout.Home.X - snowman.X) / distance * 3.05f), 0, 3);
        if (passes > plowed)
        {
            settings.Snow = Math.Max(0, settings.Snow - (passes - plowed));
            plowed = passes;
        }
        if (arrived)
            SetSnowman(SnowmanAction.DriveOut);
    }

    private void AdvanceChase(float seconds)
    {
        var target = owlHasHat
            ? new PointF(Math.Clamp(owl.X, Layout.Work.Left + 45, Layout.Work.Right - 45), Layout.Home.Y)
            : hatTarget;
        if (snowballsLeft > 0 && time >= nextShot)
        {
            direction = owl.X < snowman.X ? -1 : 1;
            shotStart = new(snowman.X + direction * 25 * Layout.Scale, snowman.Y - 65 * Layout.Scale);
            shotTarget = new(owl.X, owl.Y - 36 * Layout.Scale);
            shotAge = .69f;
            throwPose = .31f;
            nextShot = time + .98;
            snowballsLeft--;
        }

        if (throwPose > 0 || !WalkTo(target, seconds, 213) || owlHasHat || hatFall > 0 ||
            snowballsLeft > 0 || shotAge > 0)
            return;

        SetSnowman(SnowmanAction.PickHat);
    }

    private void FinishHatRecovery()
    {
        hatMissing = false;
        snowmanReaction = .7f;
        var next = afterHat;
        if (next == SnowmanAction.ToPath && settings.Snow == 0)
            next = held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return;
        SetSnowman(next);
        afterHat = SnowmanAction.Return;
    }

    private bool WalkTo(PointF target, float seconds, int speed = 168)
    {
        var distance = Distance(snowman, target);
        var stride = seconds * speed * Layout.Scale;
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

    private IEnumerable<PointF> Perches()
    {
        yield return Layout.Roof;
        yield return Layout.Lantern;
        yield return Layout.Branch;
        yield return new(Layout.View.Left + Layout.View.Width * .30f,
            Layout.View.Top + Layout.View.Height * .54f);
    }

    private void AdvanceOwl(float seconds, WindowPerch? window)
    {
        if (owlAway)
        {
            if (time >= nextOwl)
            {
                owlAway = false;
                owl = RightEdge;
                FlyTo(Layout.Roof, OwlAction.Roam);
            }
            return;
        }

        if (CurrentPerch.HasValue && CurrentPerch != window)
            DismissWindowPerch(showDeparture: true);

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

        if (CurrentPerch.HasValue)
        {
            if (time >= nextOwl)
                DismissWindowPerch(showDeparture: true);
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
        var canStealHat = !hatMissing && !hatGrabbed && stolen < 0 && settings.Snow < 3 &&
            snowmanAction is SnowmanAction.Rest or SnowmanAction.Inspect or SnowmanAction.WalkAround or SnowmanAction.Return;
        if (canStealHat && (askedForHat || time >= nextHatTheft))
        {
            askedForHat = false;
            FlyTo(new(snowman.X, snowman.Y - 106 * Layout.Scale), OwlAction.TakeHat);
            return;
        }
        var canVisit = !hatMissing && stolen < 0 && window.HasValue;
        if (time < nextOwl && !(askedForPerch && canVisit))
            return;

        if (stolen >= 0)
            FlyTo(Layout.Branch, OwlAction.ReturnToy);
        else if (!hatMissing && window is { } perch && (askedForPerch ||
            time >= nextWindowVisit && random.Next(4) == 0))
        {
            askedForPerch = false;
            CurrentPerch = perch;
            FlyTo(perch.Feet, OwlAction.AtWindow);
        }
        else if (snowmanAction is SnowmanAction.Leave or SnowmanAction.Away && settings.Ornaments is > 0 and < 7)
            FlyTo(Layout.Branch, OwlAction.Steal);
        else if (random.Next(3) == 0)
            FlyTo(LeftEdge, OwlAction.Leave);
        else
        {
            var choices = Perches().Where(point => point != owl).ToArray();
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
        nextOwl = time + random.Next(6, 15);
        switch (owlAction)
        {
            case OwlAction.Steal when settings.Ornaments is > 0 and < 7:
                stolen = settings.Ornaments - 1;
                FlyTo(Layout.Lantern, OwlAction.Roam);
                break;
            case OwlAction.ReturnToy:
                stolen = -1;
                break;
            case OwlAction.Leave:
                owlAway = true;
                nextOwl = time + random.Next(2, 6);
                break;
            case OwlAction.TakeHat:
                BeginChase();
                break;
            case OwlAction.DropHat:
                ReleaseHat();
                FlyTo(Layout.Roof, OwlAction.Roam);
                break;
            case OwlAction.AtWindow:
                nextOwl = time + random.Next(4, 8);
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

    private void BeginChase()
    {
        hatMissing = owlHasHat = true;
        afterHat = SnowmanAction.Return;
        snowballsLeft = 3;
        nextShot = time + .45;
        nextHatTheft = time + random.Next(80, 135);
        var work = Layout.Work;
        hatTarget = new(work.Left + work.Width * (.10f + random.NextSingle() * .80f),
            work.Top + work.Height * (.71f + random.NextSingle() * .20f));
        SetSnowman(SnowmanAction.FindHat);
        FlyTo(new(hatTarget.X, hatTarget.Y - 162 * Layout.Scale), OwlAction.DropHat);
    }

    private void ReleaseHat(bool aimUnderOwl = false)
    {
        owlHasHat = false;
        if (aimUnderOwl)
        {
            var work = Layout.Work;
            hatTarget = new(Math.Clamp(owl.X, work.Left + 45, work.Right - 45),
                Math.Clamp(owl.Y + 130 * Layout.Scale, work.Top + work.Height * .71f, work.Bottom - 30));
        }
        BeginHatFall(new(owl.X, owl.Y - 30 * Layout.Scale), hatTarget, .65f, spin: 190);
    }

    private void BeginHatFall(PointF origin, PointF landing, float duration, float spin)
    {
        hatFallOrigin = origin;
        hatTarget = landing;
        hatFall = hatFallDuration = duration;
        hatSpin = spin;
    }

    public PrankFrame Prank
    {
        get
        {
            PointF? hat = null, projectile = null;
            var angle = 0f;
            if (hatMissing && !owlHasHat)
            {
                if (hatDragged)
                {
                    hat = grabbedHat;
                    angle = -12;
                }
                else if (hatFall > 0)
                {
                    var fall = 1 - hatFall / hatFallDuration;
                    hat = Lerp(hatFallOrigin, hatTarget, fall * fall);
                    angle = -(1 - fall) * hatSpin;
                }
                else
                    hat = hatTarget;
            }
            if (shotAge > 0)
            {
                var progress = 1 - shotAge / .69f;
                var point = Lerp(shotStart, shotTarget, progress);
                point.Y -= MathF.Sin(progress * MathF.PI) * 53 * Layout.Scale;
                projectile = point;
            }
            return new(hat, angle, projectile);
        }
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
internal readonly record struct PrankFrame(PointF? Hat, float Angle, PointF? Snowball);
internal readonly record struct WindowPerch(nint Window, Rectangle Frame, PointF Feet);
