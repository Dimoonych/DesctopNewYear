namespace DesctopNewYear;

internal enum Pose { Rest, Walk, Hang, Shovel, Fly, React, Tractor, Throw, PickUp, RollableSnow, Wipe, FatherFrost }

internal readonly record struct ActorFrame(PointF Feet, bool Owl, Pose Pose, int Direction, int Toy, float Time,
    bool NoHat = false, bool HoldingHat = false, bool Plowing = false, bool RecoveredToy = false, float Snowball = 0,
    bool SpecialGift = false)
{
    public int ArtWidth => Pose == Pose.RollableSnow ? (int)MathF.Ceiling(Snowball * 2 + 8) :
        Pose == Pose.Tractor ? 214 : Owl ? 108 : 128;
    public int ArtHeight => Pose == Pose.RollableSnow ? ArtWidth :
        Pose == Pose.Tractor ? 122 : Owl ? 99 : 139;
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
        if (Pose == Pose.RollableSnow)
            YardArt.DrawSnowball(graphics, Snowball, new(0, -Snowball));
        else if (Owl)
            YardArt.Owl(graphics, this);
        else
        {
            graphics.ScaleTransform(Direction, 1);
            if (Pose == Pose.FatherFrost)
                YardArt.FatherFrost(graphics, this);
            else if (Pose == Pose.Tractor)
                YardArt.Tractor(graphics, this);
            else
                YardArt.Snowman(graphics, this);
        }
    }
}

internal sealed class YardScene
{
    private const float SnowDuration = 20;
    private const int SnowUpdatesPerSecond = 16;
    private enum SnowmanAction
    {
        Rest, WalkAround, Inspect, Leave, Away, Carry, Hang, Return, ToPath, Clean,
        FetchTractor, TractorAway, Plow, ParkTractor, FindHat, PickHat, FindToy, PickToy,
        ShapeBase, GoForHead, ShapeHead, CarryHead, StackHead, FetchGift, PickGift, HangGift
    }
    private enum OwlAction
    {
        Roam, Steal, DiscardToy, ShakeBranch, Leave, TakeHat, DropHat, AtWindow, LeaveWindow,
        FetchMiniHat, DeliverMiniHat
    }
    private enum MiniStage { Empty, Shaping, Loose, Parked }
    private enum Chore { Walk, Tree, Lantern }

    private readonly YardSettings settings;
    private readonly Random random;
    private readonly WinterVisit winterVisit;
    private SnowmanAction snowmanAction;
    private OwlAction owlAction;
    private PointF snowman, owl, owlStart, owlTarget;
    private int direction = -1, owlLook, held = -1, stolen = -1;
    private int decorationTree = -1;
    private double time;
    private float snowmanTime, owlTime, owlFlight, snowmanReaction, owlReaction;
    private double nextToy = 8, nextWalk = 5, nextOwl = 12, nextSnow = 16, nextSmoke = 8, nextHatTheft = 55;
    private double nextShot;
    private double nextToyTheft = 110;
    private int decorationsAtLastTheft;
    private bool askedForToy, askedForWalk, askedForTractor, askedForHat, owlAway;
    private bool askedForToyTheft, owlHasToy, returningToy, threwAtToy;
    private int plannedToy;
    private float toyFall;
    private PointF toyFallOrigin, toyLanding;
    private bool askedForPerch, visibleDeparture;
    private double nextWindowVisit = 24;
    private bool hatMissing, owlHasHat;
    private bool hatGrabbed, hatDragged;
    private SnowmanAction afterHat = SnowmanAction.Return;
    private int snowballsLeft;
    private float plowDepth;
    private float hatFall, hatFallDuration, hatSpin, shotAge, throwPose;
    private PointF strollStop, hatTarget, hatFallOrigin, shotStart, shotTarget;
    private Point hatPress;
    private PointF hatPressPosition, grabbedHat;
    private Chore chore;
    private float snowfall, smoke, powder;
    private bool settled, askedToClean, mouseDown, owlDragged, swayUsed;
    private Point dragStart;
    private PointF dragOffset;
    private MiniStage miniStage;
    private bool askedForMini, miniGrabbed;
    private float miniRadius;
    private PointF miniBall, miniDragOffset;
    private double nextMiniHat = 8;
    private int miniSlot, miniHatSlot = -1;

    public YardLayout Layout { get; private set; }
    public bool Snowing => snowfall > 0;
    public int SnowFrame => Snowing ? (int)((SnowDuration - snowfall) * SnowUpdatesPerSecond) : -1;
    private float FreshSnow => Snowing && !settled
        ? MathF.Floor(Math.Clamp((SnowDuration - snowfall - 2) / (SnowDuration - 4), 0, 1) * 12) / 12
        : 0;
    public SnowCover GroundSnow => snowmanAction == SnowmanAction.Plow
        ? new(plowDepth, 0, Math.Clamp((int)MathF.Floor(BladeX - Layout.View.X), 0, Layout.View.Width))
        : new(Math.Min(3, settings.Snow + FreshSnow), FreshSnow, Layout.View.Width);
    private float BladeX => snowman.X - 87 * Layout.Scale;
    public bool Smoking => smoke > 0;
    public bool PowderFalling => powder > 0;
    public int StolenOrnament => stolen >= 0 ? stolen : settings.MissingOrnament;
    public int LanternFrost { get; private set; } = 1;
    private PointF DecorationSpot => decorationTree < 0 ? Layout.Tree :
        Layout.PondTree(decorationTree) with
        {
            X = Layout.PondTree(decorationTree).X + 34 * Layout.Scale
        };
    public WindowPerch? CurrentPerch { get; private set; }
    public bool OwlOverWindows => !owlAway &&
        (CurrentPerch is { } perch && Distance(owl, perch.Feet) < 153 * Layout.Scale ||
         owlAction == OwlAction.LeaveWindow && visibleDeparture && owlFlight > 0 && owlTime < .65f);
    public bool InMotion => snowmanAction is not (SnowmanAction.Rest or SnowmanAction.Away or
        SnowmanAction.TractorAway or SnowmanAction.ParkTractor) ||
        snowmanReaction > 0 || owlFlight > 0 || owlReaction > 0 || mouseDown || hatGrabbed || CurrentPerch.HasValue ||
        Snowing || Smoking || PowderFalling || miniGrabbed || winterVisit.InMotion;

    public YardScene(YardSettings settings, YardLayout layout, Random? random = null)
    {
        this.settings = settings;
        this.random = random ?? Random.Shared;
        winterVisit = new(settings, Random.Shared);
        decorationsAtLastTheft = DecorationCount;
        Layout = layout;
        snowman = layout.Home;
        owl = layout.Roof;
        if (settings.MissingOrnament >= 0)
            nextToy = 18;
    }

    public void SetLayout(YardLayout layout)
    {
        if (Layout.View == layout.View)
        {
            Layout = layout;
            if (miniStage == MiniStage.Parked)
                miniBall = MiniStand;
            return;
        }
        Layout = layout;
        miniGrabbed = false;
        if (miniStage == MiniStage.Shaping)
        {
            miniStage = MiniStage.Loose;
            miniRadius = 16;
        }
        miniBall = miniStage == MiniStage.Parked ? MiniStand : layout.Home;
        if (BuildingMiniature)
            SetSnowman(SnowmanAction.Return);
        snowman = snowmanAction is SnowmanAction.Away or SnowmanAction.Carry or
            SnowmanAction.TractorAway or SnowmanAction.Plow ? Offscreen : layout.Home;
        if (snowmanAction == SnowmanAction.Hang)
            snowman = DecorationSpot;
        if (snowmanAction == SnowmanAction.Clean)
            snowman = layout.Path;
        if (snowmanAction == SnowmanAction.ParkTractor)
            snowman = TractorExit;
        if (snowmanAction is SnowmanAction.PickGift or SnowmanAction.HangGift)
            snowman = WinterVisit.GiftSpot(layout, settings.WinterGiftTree);

        owl = layout.Roof;
        owlFlight = 0;
        owlAction = OwlAction.Roam;
        miniHatSlot = -1;
        owlAway = false;
        CurrentPerch = null;
        stolen = -1;
        owlHasToy = false;
        toyFall = 0;
        if (returningToy)
            held = -1;
        if (returningToy || snowmanAction is SnowmanAction.FindToy or SnowmanAction.PickToy)
            SetSnowman(SnowmanAction.Return);
        returningToy = false;
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
            if (snowmanAction is SnowmanAction.Away or SnowmanAction.TractorAway or SnowmanAction.ParkTractor)
                return null;

            var pose = snowmanAction switch
            {
                SnowmanAction.Plow => Pose.Tractor,
                SnowmanAction.FindHat or SnowmanAction.FindToy when throwPose > 0 => Pose.Throw,
                SnowmanAction.FindToy when owlHasToy => Pose.Rest,
                SnowmanAction.PickHat or SnowmanAction.PickToy => Pose.PickUp,
                SnowmanAction.ShapeBase or SnowmanAction.ShapeHead => Pose.PickUp,
                SnowmanAction.StackHead => Pose.Hang,
                SnowmanAction.PickGift => Pose.PickUp,
                SnowmanAction.HangGift => Pose.Hang,
                _ when owlAction == OwlAction.Steal && owlFlight > 0 => Pose.Rest,
                _ when snowmanReaction > 0 => Pose.React,
                SnowmanAction.Clean => powder > .5f ? Pose.Rest : Pose.Shovel,
                SnowmanAction.Hang => Pose.Hang,
                SnowmanAction.Inspect when chore == Chore.Lantern => Pose.Wipe,
                SnowmanAction.Inspect => chore == Chore.Walk ? Pose.Rest : Pose.React,
                SnowmanAction.Rest => Pose.Rest,
                _ => Pose.Walk
            };
            return new(snowman, false, pose, direction, held,
                pose is Pose.Walk or Pose.Shovel or Pose.Tractor or Pose.Wipe ? AnimationTime : 0,
                NoHat: hatMissing, Plowing: snowmanAction == SnowmanAction.Plow &&
                    (plowDepth > 0 || settings.Miniatures.Any(figure => figure.Snowfalls >= Miniature.BurialLimit)),
                RecoveredToy: returningToy || decorationTree >= 0,
                Snowball: snowmanAction is SnowmanAction.CarryHead or SnowmanAction.StackHead ? MiniHeadRadius : 0,
                SpecialGift: snowmanAction == SnowmanAction.HangGift);
        }
    }

    public ActorFrame? Owl => owlAway ? null : new(owl, true, owlFlight > 0 && !mouseDown ? Pose.Fly :
        owlReaction > 0 || mouseDown ? Pose.React : Pose.Rest, owlLook, owlHasToy ? stolen : -1,
        owlFlight > 0 && !mouseDown ? AnimationTime : 0, HoldingHat: owlHasHat || owlAction == OwlAction.DeliverMiniHat);

    public ActorFrame? FatherFrost => winterVisit.Frame(Layout);
    public PointF? WinterGift => settings.WinterGiftTree >= 0 && snowmanAction != SnowmanAction.HangGift
        ? WinterVisit.GiftSpot(Layout, settings.WinterGiftTree) : null;
    private bool HandlingGift => snowmanAction is SnowmanAction.FetchGift or SnowmanAction.PickGift or SnowmanAction.HangGift;
    public ActorFrame? LooseSnowball => miniStage == MiniStage.Empty ? null :
        new ActorFrame(miniBall, false, Pose.RollableSnow, 1, -1, 0,
            Snowball: miniStage == MiniStage.Shaping ? 5 + 11 * Math.Clamp(snowmanTime / 1.2f, 0, 1) : miniRadius);
    public ActorFrame? RollableBall => miniStage == MiniStage.Loose ? LooseSnowball : null;
    public PointF? MiniatureTarget => miniStage == MiniStage.Loose ? MiniStand : null;
    public bool CanBuildMiniature => miniStage == MiniStage.Empty && settings.Miniatures.Length < YardLayout.MiniatureSlots;
    private PointF MiniStand => Layout.MiniatureStand(miniSlot);
    private float MiniHeadRadius => miniRadius * .62f;
    private bool BuildingMiniature => snowmanAction is SnowmanAction.ShapeBase or SnowmanAction.GoForHead or
        SnowmanAction.ShapeHead or SnowmanAction.CarryHead or SnowmanAction.StackHead;

    private float AnimationTime => (float)(time % (2 * Math.PI));
    private PointF Offscreen => new(Layout.View.Right + 113 * Layout.Scale, Layout.Home.Y);
    private PointF TractorExit => new(Layout.View.Left - 113 * Layout.Scale, Layout.Home.Y);
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
        nextWindowVisit = time + 32;
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
            plowDepth = 0;
        else if (snowmanAction is SnowmanAction.FetchTractor or SnowmanAction.TractorAway)
            SetSnowman(SnowmanAction.Return);
    }

    public void StartSnow()
    {
        if (Snowing)
            return;

        snowfall = SnowDuration;
        settled = false;
    }

    public void StartToy() => askedForToy = true;
    public void AskForToyTheft() => askedForToyTheft = StolenOrnament < 0 && owlAction != OwlAction.Steal;

    public void GreetSnowman()
    {
        if (snowmanAction is not (SnowmanAction.Away or SnowmanAction.Clean or SnowmanAction.Hang or
            SnowmanAction.Plow or SnowmanAction.ParkTractor or SnowmanAction.TractorAway or
            SnowmanAction.FindToy or SnowmanAction.PickToy))
            snowmanReaction = .85f;
        if (snowmanAction == SnowmanAction.Rest)
            StartWalk();
    }

    public void ClickSnowman()
    {
        if (CanBuildMiniature)
        {
            askedForMini = true;
            if (SnowmanFree)
                return;
        }
        GreetSnowman();
    }

    public bool TryGrabSnowball(Point cursor)
    {
        if (miniGrabbed || miniStage != MiniStage.Loose)
            return false;

        var radius = miniRadius * Layout.Scale;
        var center = new PointF(miniBall.X, miniBall.Y - radius);
        if (Distance(center, cursor) > radius + 4)
            return false;

        miniGrabbed = true;
        miniDragOffset = new(miniBall.X - cursor.X, miniBall.Y - cursor.Y);
        return true;
    }

    public void RollSnowball(Point cursor)
    {
        if (!miniGrabbed)
            return;

        var work = Layout.Work;
        var scale = Layout.Scale;
        var next = new PointF(
            Math.Clamp(cursor.X + miniDragOffset.X, work.Left + 32 * scale, work.Right - 32 * scale),
            Math.Clamp(cursor.Y + miniDragOffset.Y, Layout.Home.Y - 84 * scale, work.Bottom - 14 * scale));
        miniRadius = Math.Min(30, miniRadius + Distance(miniBall, next) / (38 * scale));
        miniBall = next;
    }

    public void DropSnowball(Point cursor)
    {
        if (!miniGrabbed)
            return;

        RollSnowball(cursor);
        miniGrabbed = false;
        if (Distance(miniBall, MiniStand) > 76 * Layout.Scale)
            return;

        miniBall = MiniStand;
        miniRadius = Math.Max(22, miniRadius);
        miniStage = MiniStage.Parked;
    }

    public void CancelSnowballDrag() => miniGrabbed = false;

    public bool TryGrabHat(Point cursor)
    {
        if (hatMissing || hatGrabbed || BuildingMiniature || HandlingGift || owlAction is OwlAction.TakeHat or OwlAction.Steal ||
            stolen >= 0 && !returningToy ||
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

    public void DropOwl(Point? cursor = null)
    {
        if (cursor is { } finalPosition)
            DragOwl(finalPosition);
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        if (owlHasHat)
            ReleaseHat(aimUnderOwl: true);
        if (owlHasToy)
            ReleaseOrnament();
        var perch = Perches().MinBy(point => Distance(owl, point));
        FlyTo(perch, OwlAction.Roam);
    }

    public void CancelDrag()
    {
        mouseDown = false;
        if (!owlDragged)
            return;

        owlDragged = false;
        if (owlHasHat)
            ReleaseHat(aimUnderOwl: true);
        if (owlHasToy)
            ReleaseOrnament();
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
        toyFall = Math.Max(0, toyFall - seconds);
        shotAge = Math.Max(0, shotAge - seconds);
        AdvanceWeather(seconds, daylight);
        winterVisit.Advance(seconds, localTime, daylight.Shade >= 12, snowfall >= 12);
        var giftReady = settings.WinterGiftTree >= 0 && localTime.Hour is >= 7 and < 18 &&
            DateOnly.FromDateTime(localTime).DayNumber > settings.WinterGiftDay;
        AdvanceSnowman(seconds, localTime.Hour >= 23 || localTime.Hour < 7, giftReady);
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
            if (snowfall <= 2 && !settled)
            {
                if (snowmanAction != SnowmanAction.Plow)
                {
                    settings.Snow = Math.Min(3, settings.Snow + 1);
                    LanternFrost = Math.Min(3, LanternFrost + 1);
                    if (settings.Miniatures.Any(figure => figure.HasHat && figure.Snowfalls < Miniature.BurialLimit))
                        settings.Miniatures = settings.Miniatures.Select(figure =>
                            figure.HasHat && figure.Snowfalls < Miniature.BurialLimit
                                ? figure with { Snowfalls = figure.Snowfalls + 1 } : figure).ToArray();
                }
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

    private void AdvanceSnowman(float seconds, bool late, bool giftReady)
    {
        if (hatGrabbed || (owlAction is OwlAction.TakeHat or OwlAction.Steal) && owlFlight > 0)
            return;

        if ((askedForTractor || settings.Snow == 3) &&
            (SnowmanFree || snowmanAction is SnowmanAction.ToPath or SnowmanAction.Clean))
        {
            askedForTractor = askedToClean = false;
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.FetchTractor);
        }
        else if (askedToClean && (SnowmanFree || snowmanAction == SnowmanAction.Carry))
        {
            askedToClean = false;
            swayUsed = false;
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.ToPath);
        }
        else if (SnowmanFree && miniStage == MiniStage.Parked)
        {
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.GoForHead);
        }
        else if (SnowmanFree && askedForMini && CanBuildMiniature)
            ShapeSnowball();
        else if (SnowmanFree && giftReady)
        {
            snowmanReaction = 0;
            SetSnowman(SnowmanAction.FetchGift);
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
                if (chore == Chore.Lantern)
                    direction = 1;
                SetSnowman(SnowmanAction.Inspect);
                break;
            case SnowmanAction.Inspect when chore == Chore.Lantern && snowmanTime >= 2.4f:
                LanternFrost = 0;
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.Inspect when chore != Chore.Lantern && snowmanTime >= 1.7f:
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.Leave when WalkTo(Offscreen, seconds):
                SetSnowman(SnowmanAction.Away);
                break;
            case SnowmanAction.Away when snowmanTime >= 4.6f:
                ChooseDecoration();
                SetSnowman(SnowmanAction.Carry);
                break;
            case SnowmanAction.Carry when WalkTo(DecorationSpot, seconds, 132):
                direction = -1;
                SetSnowman(SnowmanAction.Hang);
                break;
            case SnowmanAction.Hang when snowmanTime >= 1.9f:
                if (returningToy)
                {
                    stolen = -1;
                    settings.MissingOrnament = -1;
                    returningToy = false;
                    nextToyTheft = time + random.Next(130, 181);
                }
                else if (decorationTree >= 0)
                    settings.PondOrnaments++;
                else
                    settings.Ornaments = Math.Min(7, settings.Ornaments + 1);
                held = -1;
                decorationTree = -1;
                nextToy = time + (late ? random.Next(48, 75) : random.Next(30, 50));
                nextWalk = Math.Max(nextWalk, time + 8);
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.Return when WalkTo(Layout.Home, seconds):
                nextWalk = Math.Max(nextWalk, time + (late ? 12 : 7));
                SetSnowman(SnowmanAction.Rest);
                break;
            case SnowmanAction.ToPath when WalkTo(Layout.Path, seconds):
                SetSnowman(SnowmanAction.Clean);
                break;
            case SnowmanAction.FetchGift when WalkTo(WinterVisit.GiftSpot(Layout, settings.WinterGiftTree), seconds, 134):
                direction = -1;
                SetSnowman(SnowmanAction.PickGift);
                break;
            case SnowmanAction.PickGift when snowmanTime >= .9f:
                SetSnowman(SnowmanAction.HangGift);
                break;
            case SnowmanAction.HangGift when snowmanTime >= 1.5f:
                settings.WinterStars |= 1 << settings.WinterGiftTree;
                settings.WinterGiftTree = -1;
                settings.WinterGiftDay = 0;
                SetSnowman(SnowmanAction.Return);
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
                plowDepth = GroundSnow.Depth;
                SetSnowman(SnowmanAction.Plow);
                break;
            case SnowmanAction.Plow:
                AdvanceTractor(seconds);
                break;
            case SnowmanAction.ParkTractor when snowmanTime >= 2:
                SetSnowman(held >= 0 ? SnowmanAction.Carry : SnowmanAction.Return);
                break;
            case SnowmanAction.FindHat:
                AdvanceChase(seconds);
                break;
            case SnowmanAction.PickHat when snowmanTime >= .52f:
                FinishHatRecovery();
                break;
            case SnowmanAction.FindToy:
                AdvanceToyChase(seconds);
                break;
            case SnowmanAction.PickToy when snowmanTime >= .48f:
                held = stolen;
                returningToy = true;
                decorationTree = -1;
                SetSnowman(SnowmanAction.Carry);
                break;
            case SnowmanAction.ShapeBase when snowmanTime >= 1.2f:
                miniStage = MiniStage.Loose;
                miniRadius = 16;
                SetSnowman(SnowmanAction.Return);
                break;
            case SnowmanAction.GoForHead when WalkTo(Layout.Path, seconds, 136):
                SetSnowman(SnowmanAction.ShapeHead);
                break;
            case SnowmanAction.ShapeHead when snowmanTime >= 1.2f:
                SetSnowman(SnowmanAction.CarryHead);
                break;
            case SnowmanAction.CarryHead when WalkTo(MiniStand with
                { X = MiniStand.X + 53 * Layout.Scale }, seconds, 132):
                direction = -1;
                SetSnowman(SnowmanAction.StackHead);
                break;
            case SnowmanAction.StackHead when snowmanTime >= 1.6f:
                settings.Miniatures = [..settings.Miniatures, new Miniature(miniSlot, (int)MathF.Round(miniRadius), false)];
                nextMiniHat = time + 6;
                miniStage = MiniStage.Empty;
                SetSnowman(SnowmanAction.Return);
                break;
        }
    }

    private void ChooseDecoration()
    {
        returningToy = settings.MissingOrnament >= 0;
        decorationTree = -1;
        if (returningToy)
        {
            held = settings.MissingOrnament;
            return;
        }

        var pondRound = settings.PondOrnaments / YardLayout.PondTreeCount;
        if (pondRound < 3 && (settings.Ornaments == 7 || settings.Ornaments > pondRound + 1))
        {
            decorationTree = settings.PondOrnaments % YardLayout.PondTreeCount;
            held = YardArt.PondOrnament(decorationTree, pondRound);
        }
        else
            held = settings.Ornaments < 7 ? settings.Ornaments : random.Next(0, 6);
    }

    private void ShapeSnowball()
    {
        askedForMini = false;
        snowmanReaction = 0;
        direction = -1;
        miniSlot = Enumerable.Range(0, YardLayout.MiniatureSlots)
            .First(slot => settings.Miniatures.All(figure => figure.Slot != slot));
        var scale = Layout.Scale;
        miniBall = new(Math.Clamp(snowman.X - 48 * scale, Layout.Work.Left + 34 * scale,
            Layout.Work.Right - 34 * scale), snowman.Y);
        miniStage = MiniStage.Shaping;
        SetSnowman(SnowmanAction.ShapeBase);
    }

    private bool SnowmanFree => snowmanAction is SnowmanAction.Rest or SnowmanAction.Return or SnowmanAction.WalkAround ||
        snowmanAction == SnowmanAction.Inspect && chore != Chore.Lantern;

    private void BeginWalk(bool late)
    {
        askedForWalk = false;
        nextWalk = time + (late ? random.Next(24, 36) : random.Next(14, 24));
        chore = (Chore)(((int)chore + 1) % 3);
        strollStop = chore switch
        {
            Chore.Tree => Layout.Tree,
            Chore.Lantern => Layout.Screen(-489, 11),
            _ => new(Layout.View.Left + Layout.View.Width * (.19f + random.NextSingle() * .23f),
                Layout.Home.Y + 23 * Layout.Scale)
        };
        SetSnowman(SnowmanAction.WalkAround);
    }

    private void AdvanceTractor(float seconds)
    {
        var arrived = WalkTo(TractorExit, seconds, 210);
        if (settings.Miniatures.Any(PassedBuriedFigure))
            settings.Miniatures = settings.Miniatures.Where(figure => !PassedBuriedFigure(figure)).ToArray();
        if (!arrived)
            return;

        settings.Snow = 0;
        if (Snowing)
            settled = true;
        SetSnowman(SnowmanAction.ParkTractor);
    }

    private bool PassedBuriedFigure(Miniature figure) => figure.Snowfalls >= Miniature.BurialLimit &&
        BladeX <= Layout.MiniatureStand(figure.Slot).X - figure.Size * 2.2f * Layout.Scale;

    private void AdvanceChase(float seconds)
    {
        var target = owlHasHat
            ? new PointF(Math.Clamp(owl.X, Layout.Work.Left + 45, Layout.Work.Right - 45), Layout.Home.Y)
            : hatTarget;
        if (snowballsLeft > 0 && time >= nextShot)
        {
            ThrowSnowball(owl);
            nextShot = time + .98;
            snowballsLeft--;
        }

        if (throwPose > 0 || !WalkTo(target, seconds, 213) || owlHasHat || hatFall > 0 ||
            snowballsLeft > 0 || shotAge > 0)
            return;

        SetSnowman(SnowmanAction.PickHat);
    }

    private void AdvanceToyChase(float seconds)
    {
        if (owlHasToy)
        {
            direction = owl.X < snowman.X ? -1 : 1;
            if (!threwAtToy && snowmanTime >= 1.25f)
            {
                var aim = owlFlight > 0 ? FlightPoint(owlTime + .52f) : owl;
                aim.Y += 35 * Layout.Scale;
                ThrowSnowball(aim);
                threwAtToy = true;
            }
            return;
        }

        if (throwPose <= 0 && WalkTo(toyLanding, seconds, 155) && toyFall <= 0)
            SetSnowman(SnowmanAction.PickToy);
    }

    private void ThrowSnowball(PointF target)
    {
        direction = target.X < snowman.X ? -1 : 1;
        shotStart = new(snowman.X + direction * 25 * Layout.Scale, snowman.Y - 65 * Layout.Scale);
        shotTarget = new(target.X, target.Y - 36 * Layout.Scale);
        shotAge = .69f;
        throwPose = .31f;
    }

    private PointF SnowballPosition
    {
        get
        {
            var progress = 1 - shotAge / .69f;
            var point = Lerp(shotStart, shotTarget, progress);
            point.Y -= MathF.Sin(progress * MathF.PI) * 53 * Layout.Scale;
            return point;
        }
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
                if (owlAction == OwlAction.FetchMiniHat)
                {
                    var figure = settings.Miniatures.First(item => item.Slot == miniHatSlot);
                    var stand = Layout.MiniatureStand(figure.Slot);
                    var size = figure.Size;
                    var height = 2 * size + size * .62f * 1.46f;
                    FlyTo(new(stand.X - 8 * Layout.Scale, stand.Y - (height - 29) * Layout.Scale),
                        OwlAction.DeliverMiniHat, speed: 238, maxDuration: 7.5f);
                }
                else
                    FlyTo(Layout.Roof, OwlAction.Roam);
            }
            return;
        }

        if (CurrentPerch.HasValue && CurrentPerch != window)
            DismissWindowPerch(showDeparture: true);

        if (owlFlight > 0)
        {
            owlTime += Math.Min(seconds, .085f);
            owl = FlightPoint(owlTime);
            if (owlTime < owlFlight)
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

        if (time >= nextMiniHat && !hatMissing && stolen < 0 &&
            settings.Miniatures.FirstOrDefault(figure => !figure.HasHat) is { } waiting)
        {
            miniHatSlot = waiting.Slot;
            nextMiniHat = time + 12;
            FlyTo(RightEdge, OwlAction.FetchMiniHat);
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
        var canStealHat = !hatMissing && !hatGrabbed && stolen < 0 && settings.Snow < 3 && SnowmanFree;
        if (canStealHat && (askedForHat || settings.MissingOrnament < 0 && time >= nextHatTheft))
        {
            askedForHat = false;
            FlyTo(new(snowman.X, snowman.Y - 106 * Layout.Scale), OwlAction.TakeHat);
            return;
        }
        var canVisit = !hatMissing && stolen < 0 && window.HasValue;
        if (canVisit && askedForPerch && window is { } requested)
        {
            LandOnWindow(requested);
            return;
        }
        var canStealToy = canStealHat && settings.Ornaments > 0 && settings.MissingOrnament < 0;
        var readyForAutomaticTheft = !askedForToy && time >= nextToyTheft &&
            (DecorationCount > decorationsAtLastTheft ||
             settings.Ornaments == 7 && settings.PondOrnaments == YardLayout.PondTreeCount * 3);
        if (canStealToy && (askedForToyTheft || readyForAutomaticTheft))
        {
            askedForToyTheft = false;
            decorationsAtLastTheft = DecorationCount;
            nextToyTheft = time + 130;
            plannedToy = random.Next(Math.Min(6, settings.Ornaments));
            var branch = YardArt.OrnamentPosition(Layout, plannedToy);
            FlyTo(new(branch.X - 8 * Layout.Scale, branch.Y + 29 * Layout.Scale), OwlAction.Steal);
            return;
        }
        if (time < nextOwl)
            return;

        if (canVisit && window is { } perch && time >= nextWindowVisit)
            LandOnWindow(perch);
        else if (random.Next(3) == 0)
            FlyTo(LeftEdge, OwlAction.Leave);
        else
        {
            var choices = Perches().Where(point => point != owl).ToArray();
            FlyTo(choices[random.Next(choices.Length)], OwlAction.Roam);
        }
    }

    private int DecorationCount => settings.Ornaments + settings.PondOrnaments;

    private void LandOnWindow(WindowPerch perch)
    {
        askedForPerch = false;
        CurrentPerch = perch;
        FlyTo(perch.Feet, OwlAction.AtWindow);
    }

    private PointF FlightPoint(float elapsed)
    {
        var progress = Math.Min(1, elapsed / owlFlight);
        var eased = progress * progress * (3 - 2 * progress);
        var point = Lerp(owlStart, owlTarget, eased);
        point.Y -= MathF.Sin(progress * MathF.PI) * 75 * Layout.Scale;
        return point;
    }

    private void FlyTo(PointF target, OwlAction action, int speed = 255, float maxDuration = 6.3f)
    {
        owlStart = owl;
        owlTarget = target;
        owlTime = 0;
        owlFlight = Math.Clamp(Distance(owl, target) / (speed * Layout.Scale), .85f, maxDuration);
        owlAction = action;
    }

    private void FinishFlight()
    {
        nextOwl = time + random.Next(12, 23);
        switch (owlAction)
        {
            case OwlAction.Steal:
                BeginToyChase();
                break;
            case OwlAction.DiscardToy:
                DiscardOrnament();
                break;
            case OwlAction.FetchMiniHat:
                owlAway = true;
                nextOwl = time + random.Next(4, 7);
                break;
            case OwlAction.DeliverMiniHat:
                settings.Miniatures = settings.Miniatures.Select(figure =>
                    figure.Slot == miniHatSlot ? figure with { HasHat = true } : figure).ToArray();
                miniHatSlot = -1;
                nextMiniHat = time + 8;
                FlyTo(Layout.Roof, OwlAction.Roam);
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
                nextOwl = time + random.Next(8, 13);
                break;
            case OwlAction.ShakeBranch when snowmanAction == SnowmanAction.Clean:
                settings.Snow = Math.Min(3, settings.Snow + 1);
                powder = .95f;
                snowmanTime = 0;
                break;
        }
    }

    private void BeginToyChase()
    {
        stolen = plannedToy;
        owlHasToy = true;
        threwAtToy = false;
        snowmanReaction = 0;
        SetSnowman(SnowmanAction.FindToy);
        var edge = LeftEdge with { Y = Layout.View.Top + Layout.View.Height * (.34f + random.NextSingle() * .12f) };
        FlyTo(edge, OwlAction.DiscardToy, speed: 224, maxDuration: 7.5f);
    }

    private void DiscardOrnament()
    {
        settings.MissingOrnament = stolen;
        stolen = -1;
        owlHasToy = false;
        owlAway = true;
        nextOwl = time + random.Next(5, 10);
        nextToy = time + random.Next(18, 30);
        snowmanReaction = 1.1f;
        SetSnowman(SnowmanAction.Return);
    }

    private void ReleaseOrnament()
    {
        owlHasToy = false;
        var scale = Layout.Scale;
        toyFallOrigin = new(owl.X + 9 * scale, owl.Y - 29 * scale);
        toyLanding = new(Math.Clamp(toyFallOrigin.X, Layout.Work.Left + 30 * scale,
            Layout.Work.Right - 30 * scale), Layout.Home.Y);
        toyFall = .56f;
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
            PointF? hat = null;
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
            OrnamentDrop? toy = null;
            if (stolen >= 0 && !owlHasToy && !returningToy)
            {
                var fall = 1 - toyFall / .56f;
                toy = new(stolen, Lerp(toyFallOrigin, toyLanding, fall * fall));
            }
            return new(hat, angle, shotAge > 0 ? SnowballPosition : null, toy);
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

    public Rectangle AmbientFootprint()
    {
        var region = Rectangle.Empty;
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
        var elapsed = SnowFrame / (float)SnowUpdatesPerSecond;
        var brightness = Math.Clamp(Math.Min(elapsed, SnowDuration - elapsed) / 3, 0, 1);
        var area = Layout.SnowArea;
        var lantern = Layout.Local(-465, -78);
        using var brush = new SolidBrush(Color.White);
        foreach (var flake in flakes)
        {
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
internal readonly record struct SnowCover(float Depth, float Fresh, int RightEdge);
internal readonly record struct OrnamentDrop(int Index, PointF Position);
internal readonly record struct PrankFrame(PointF? Hat, float Angle, PointF? Snowball, OrnamentDrop? Toy);
internal readonly record struct WindowPerch(nint Window, Rectangle Frame, PointF Feet);
