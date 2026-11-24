using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DesctopNewYear;

internal readonly record struct YardLayout(Rectangle View, Rectangle Work)
{
    public const int MiniatureSlots = 4;
    public const int PondTreeCount = 4;
    public float Scale => Math.Clamp(Math.Min(View.Width / 1440f, View.Height / 900f), .46f, 1.65f);
    public PointF Anchor => new(View.Width - 64 * Scale,
        Math.Min(View.Height - 103 * Scale, Work.Bottom - View.Y - 57 * Scale));
    public PointF Local(float x, float y) => new(Anchor.X + x * Scale, Anchor.Y + y * Scale);
    public PointF Screen(float x, float y)
    {
        var local = Local(x, y);
        return new(View.X + local.X, View.Y + local.Y);
    }

    public PointF Home => Screen(-85, 2);
    public PointF Tree => Screen(-360, 1);
    public PointF Path => Screen(-277, 9);
    public PointF Roof => Screen(-134, -202);
    public PointF Lantern => Screen(-465, -106);
    public PointF Branch => Screen(-393, -112);
    public PointF PondTree(int index)
    {
        var (x, y) = index switch
        {
            0 => (.048f, .849f),
            1 => (.109f, .809f),
            2 => (.423f, .746f),
            _ => (.49f, .773f)
        };
        return new(View.Left + View.Width * x, View.Top + View.Height * y);
    }
    public Rectangle SnowArea => new(Point.Empty, View.Size);
    public Rectangle DriftArea => Rectangle.FromLTRB(0, (int)MathF.Floor(Anchor.Y - 146 * Scale),
        View.Width, (int)MathF.Ceiling(Anchor.Y + 70 * Scale));
    public PointF PondBench => new(View.Width * .265f + Math.Min(View.Width * .163f, 285 * Scale) * .75f,
        Anchor.Y + 18 * Scale);
    public PointF MiniatureStand(int slot)
    {
        var (x, y) = slot switch
        {
            0 => (73, 9),
            1 => (-92, 17),
            2 => (178, 2),
            _ => (-197, 8)
        };
        return new(View.X + PondBench.X + x * Scale, View.Y + PondBench.Y + y * Scale);
    }
}

internal static class YardArt
{
    public const float SnowmanHatHeight = 102;
    private static readonly Color[] OrnamentColors =
    [
        C("#e76b58"), C("#f6c774"), C("#99d9d5"), C("#e68fae"), C("#9bc8ed"), C("#ee9c66"), C("#ffe4a2")
    ];
    private static readonly PointF[] Branches =
    [
        new(-25, -141), new(25, -113), new(-48, -89), new(44, -58),
        new(-18, -51), new(12, -80)
    ];

    public static PointF OrnamentPosition(YardLayout layout, int index) =>
        layout.Screen(-393 + Branches[index].X, Branches[index].Y);

    public static Bitmap Landscape(YardLayout layout, Daylight light, int ornaments, int missing = -1,
        int pondOrnaments = 0, int lanternFrost = 0, int winterStars = 0)
    {
        var bitmap = new Bitmap(layout.View.Width, layout.View.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        Prepare(graphics);
        var w = bitmap.Width;
        var h = bitmap.Height;
        var dark = light.Shade / 16f;
        var twilight = 1 - Math.Abs(2 * dark - 1);
        var skyTop = Mix(C("#86aac3"), C("#14263f"), dark);
        var skyBottom = Mix(Mix(C("#e3dfd4"), C("#465779"), dark), C("#bb8b87"), twilight * .35f);
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, w, h), skyTop, skyBottom, 90))
            graphics.FillRectangle(sky, 0, 0, w, h);

        DrawSky(graphics, w, h, dark);
        DrawMountains(graphics, w, h, dark);
        DrawForest(graphics, w, h, layout.Scale, dark);
        DrawGround(graphics, w, h, dark);
        DrawMeadow(graphics, layout, dark, pondOrnaments, winterStars);

        graphics.TranslateTransform(layout.Anchor.X, layout.Anchor.Y);
        graphics.ScaleTransform(layout.Scale, layout.Scale);
        DrawYard(graphics, light, ornaments, dark, missing, lanternFrost);
        return bitmap;
    }

    private static void DrawSky(Graphics g, int w, int h, float dark)
    {
        var random = new Random(719);
        using var stars = new SolidBrush(Color.FromArgb((int)(dark * dark * 140), C("#fff1d4")));
        for (var i = 0; i < 86; i++)
        {
            var x = random.NextSingle() * w;
            var y = random.NextSingle() * h * .53f;
            var size = i % 13 == 0 ? 2.3f : 1.3f;
            g.FillEllipse(stars, x, y, size, size);
        }

        var radius = Math.Max(16, h * .031f);
        var moon = new PointF(w * .70f, h * .15f);
        Glow(g, moon, radius * 2.3f, C("#ffe5b5"), (int)(36 * dark));
        Ellipse(g, Color.FromArgb((int)(220 * dark), C("#fff2d6")), moon.X - radius,
            moon.Y - radius, radius * 2, radius * 2);
        Ellipse(g, Color.FromArgb((int)(47 * dark), C("#c4a999")), moon.X - radius * .47f,
            moon.Y + 1, radius * .45f, radius * .29f);

        using var cloud = new SolidBrush(Color.FromArgb((int)(77 - 52 * dark), C("#e8e4e5")));
        SkyRibbon(g, cloud, w * .08f, h * .25f, w * .39f, h * .041f);
        SkyRibbon(g, cloud, w * .48f, h * .34f, w * .39f, h * .028f);
    }

    private static void SkyRibbon(Graphics g, Brush fill, float x, float y, float length, float height)
    {
        using var ribbon = new GraphicsPath();
        ribbon.AddBezier(x, y + height, x + length * .25f, y - height * .55f,
            x + length * .39f, y - height * .16f, x + length, y + height * .40f);
        ribbon.AddBezier(x + length, y + height * .40f, x + length * .68f, y + height * .62f,
            x + length * .31f, y + height * .18f, x, y + height);
        g.FillPath(fill, ribbon);
    }

    private static void DrawMountains(Graphics g, int w, int h, float dark)
    {
        var ridge = Mix(C("#b2c1cf"), C("#3b4a66"), dark);
        var snow = Mix(C("#e2e6e6"), C("#63718c"), dark);
        Poly(g, ridge,
            new(0, h * .56f), new(w * .10f, h * .50f), new(w * .23f, h * .403f),
            new(w * .31f, h * .47f), new(w * .39f, h * .425f), new(w * .54f, h * .56f),
            new(w * .63f, h * .49f), new(w * .76f, h * .445f), new(w * .91f, h * .53f),
            new(w, h * .51f), new(w, h * .73f), new(0, h * .73f));
        Poly(g, snow, new(w * .165f, h * .452f), new(w * .23f, h * .403f),
            new(w * .286f, h * .45f), new(w * .251f, h * .443f),
            new(w * .235f, h * .431f), new(w * .202f, h * .465f), new(w * .204f, h * .442f));
        Poly(g, snow, new(w * .345f, h * .451f), new(w * .39f, h * .425f),
            new(w * .437f, h * .467f), new(w * .41f, h * .462f),
            new(w * .386f, h * .448f), new(w * .36f, h * .457f));
        Poly(g, Color.FromArgb(150, snow), new(w * .715f, h * .463f), new(w * .76f, h * .445f),
            new(w * .806f, h * .471f), new(w * .775f, h * .466f), new(w * .75f, h * .46f));
    }

    private static void DrawForest(Graphics g, int w, int h, float scale, float dark)
    {
        var far = Mix(C("#a1b5c6"), C("#374969"), dark);
        var near = Mix(C("#7999ae"), C("#263d56"), dark);
        using (var hills = new GraphicsPath())
        {
            hills.AddBezier(-20, h * .67f, w * .21f, h * .37f, w * .34f, h * .83f, w * .61f, h * .58f);
            hills.AddBezier(w * .61f, h * .58f, w * .83f, h * .44f, w * .91f, h * .57f, w + 20, h * .50f);
            hills.AddLine(w + 20, h, -20, h);
            hills.CloseFigure();
            using var fill = new SolidBrush(far);
            g.FillPath(fill, hills);
        }

        var random = new Random(824);
        for (var layer = 0; layer < 2; layer++)
        {
            using var fill = new SolidBrush(layer == 0 ? far : near);
            for (var x = -70f; x < w + 80; x += (16 + random.Next(24)) * scale)
            {
                var y = h * (.65f + layer * .055f + .033f * MathF.Sin(x / w * 9 + layer));
                var height = (42 + random.Next(105)) * scale * (layer == 0 ? 1 : 1.21f);
                var half = height * (.18f + .14f * random.NextSingle());
                Fir(g, fill, x, y, height, half);
            }
        }
    }

    private static void DrawGround(Graphics g, int w, int h, float dark)
    {
        using var ground = new GraphicsPath();
        ground.AddBezier(-4, h * .67f, w * .25f, h * .58f, w * .47f, h * .78f, w * .65f, h * .69f);
        ground.AddBezier(w * .65f, h * .69f, w * .84f, h * .60f, w * .95f, h * .67f, w + 4, h * .62f);
        ground.AddLine(w + 4, h + 2, -4, h + 2);
        ground.CloseFigure();
        using var fill = new LinearGradientBrush(new Rectangle(0, (int)(h * .58f), w, Math.Max(1, (int)(h * .43f))),
            Mix(C("#eaf1ee"), C("#627b96"), dark), Mix(C("#c8dce2"), C("#39536f"), dark), 90);
        g.FillPath(fill, ground);

        using var drift = new Pen(Color.FromArgb(31, C("#eef5f2")), 1.25f);
        g.DrawBezier(drift, 0, h * .86f, w * .25f, h * .79f, w * .43f, h * .94f, w * .60f, h * .86f);
        g.DrawBezier(drift, w * .12f, h * .96f, w * .23f, h * .90f, w * .33f, h * .99f, w * .37f, h * .95f);
    }

    private static void DrawMeadow(Graphics g, YardLayout layout, float dark, int ornaments, int winterStars)
    {
        var scale = layout.Scale;
        var w = layout.View.Width;
        var h = layout.View.Height;
        var center = new PointF(w * .265f, layout.Anchor.Y - 66 * scale);
        var halfWidth = Math.Min(w * .163f, 285 * scale);
        var halfHeight = 55 * scale;
        var end = layout.Local(-475, 16);
        var pathY = layout.Anchor.Y + 34 * scale;
        using (var trail = new Pen(Mix(C("#acc7d1"), C("#4c6784"), dark), 16 * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
            g.DrawBezier(trail, center.X - halfWidth * .54f, pathY - 9 * scale,
                w * .33f, pathY + 73 * scale, w * .48f, layout.Anchor.Y - 61 * scale, end.X, end.Y);

        var iceEdge = Mix(C("#ecf4f1"), C("#9db5ca"), dark);
        var ice = Mix(C("#9dc8d8"), C("#416d8d"), dark);
        using var pond = new GraphicsPath();
        pond.AddBezier(center.X - halfWidth, center.Y,
            center.X - halfWidth * .90f, center.Y - halfHeight * 1.05f,
            center.X + halfWidth * .50f, center.Y - halfHeight * 1.14f,
            center.X + halfWidth * .88f, center.Y - halfHeight * .30f);
        pond.AddBezier(center.X + halfWidth * .88f, center.Y - halfHeight * .30f,
            center.X + halfWidth * 1.48f, center.Y + halfHeight * .88f,
            center.X - halfWidth * .77f, center.Y + halfHeight * 1.27f,
            center.X - halfWidth, center.Y);
        pond.CloseFigure();
        using (var surface = new SolidBrush(ice))
            g.FillPath(surface, pond);
        using (var bank = new Pen(iceEdge, 5 * scale) { LineJoin = LineJoin.Round })
            g.DrawPath(bank, pond);

        var clip = g.Save();
        g.SetClip(pond);
        using (var glint = new Pen(Color.FromArgb(113, iceEdge), 1.5f * scale))
        {
            g.DrawBezier(glint, center.X - halfWidth * .66f, center.Y + 10 * scale,
                center.X - halfWidth * .35f, center.Y + 19 * scale, center.X + halfWidth * .26f,
                center.Y - 9 * scale, center.X + halfWidth * .66f, center.Y - 8 * scale);
            g.DrawBezier(glint, center.X - halfWidth * .53f, center.Y + 28 * scale,
                center.X - halfWidth * .26f, center.Y + 22 * scale, center.X + halfWidth * .49f,
                center.Y + 27 * scale, center.X + halfWidth * .72f, center.Y + 10 * scale);
        }
        g.Restore(clip);

        // A few landmarks carry the winter scene across the monitor.
        for (var i = 0; i < YardLayout.PondTreeCount; i++)
        {
            var position = layout.PondTree(i);
            var treeScale = scale * (i switch { 0 => .80f, 1 => .56f, 2 => .50f, _ => .67f });
            var placed = (ornaments + YardLayout.PondTreeCount - 1 - i) / YardLayout.PondTreeCount;
            var state = g.Save();
            g.TranslateTransform(position.X - layout.View.X, position.Y - layout.View.Y);
            g.ScaleTransform(treeScale, treeScale);
            DrawTree(g, 0, 0, placed, iceEdge, dark, -1, i);
            if ((winterStars & (1 << i)) != 0)
            {
                var branch = new PointF(23, -130);
                Glow(g, branch, 26, C("#c9edff"), 120);
                WinterStar(g, branch, 10);
            }
            g.Restore(state);
        }
        DrawBench(g, layout.PondBench, scale, iceEdge, dark);
    }

    private static void DrawBench(Graphics g, PointF position, float scale, Color snow, float dark)
    {
        var state = g.Save();
        g.TranslateTransform(position.X, position.Y);
        g.ScaleTransform(scale, scale);
        var wood = Mix(C("#936964"), C("#655162"), dark);
        var frame = Mix(C("#566778"), C("#3a4e65"), dark);
        Ellipse(g, Color.FromArgb(61, C("#37536e")), -49, -3, 98, 13);
        Line(g, frame, 3.6f, -32, -29, -32, 2);
        Line(g, frame, 3.6f, 32, -29, 32, 2);
        Rect(g, wood, -39, -29, 78, 8);
        Rect(g, wood, -42, -13, 84, 7);
        Rect(g, snow, -41, -15, 82, 3);
        g.Restore(state);
    }

    private static void DrawYard(Graphics g, Daylight light, int ornaments, float dark, int missing, int frost)
    {
        var snowColor = Mix(C("#f7fcf8"), C("#bdd3e2"), dark);
        Ellipse(g, Color.FromArgb(64, C("#294362")), -552, -15, 575, 55);
        using (var path = new Pen(Mix(C("#8faeb9"), C("#4e6078"), dark), 19)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
            g.DrawBezier(path, -496, 20, -407, -8, -270, 47, -91, -7);

        if (light.LanternLit)
        {
            Glow(g, new(-465, -78), 94, C("#ffd68d"), 81 - 15 * frost);
            Ellipse(g, Color.FromArgb(55 - 9 * frost, C("#ffdc9a")), -523, 1, 114, 26);
        }

        DrawHouse(g, snowColor, light.WindowLit, dark);
        DrawTree(g, -393, 0, ornaments, snowColor, dark, missing);
        DrawLantern(g, light.LanternLit, snowColor, frost);
    }

    private static void DrawHouse(Graphics g, Color snow, bool lit, float dark)
    {
        var siding = Mix(C("#986053"), C("#553d50"), dark);
        var trim = Mix(C("#e7b899"), C("#a17f86"), dark);
        var darkWood = C("#443445");
        Rect(g, C("#6e4b53"), -89, -195, 24, 55);
        Rect(g, snow, -93, -198, 31, 7);
        // Extend the gable behind the fascia so neither slope exposes the sky at its seam.
        Poly(g, siding, new(-233, -105), new(-134, -195), new(-34, -105), new(-34, -9), new(-233, -9));
        using (var board = new Pen(Color.FromArgb(72, darkWood), 2))
        {
            for (var y = -100; y < -10; y += 11)
                g.DrawLine(board, -232, y, -35, y);
        }

        Poly(g, C("#513f54"), new(-256, -102), new(-136, -204), new(-10, -102), new(-31, -94),
            new(-136, -181), new(-234, -95));
        Poly(g, snow, new(-256, -108), new(-140, -206), new(-128, -206), new(-10, -109),
            new(-24, -105), new(-42, -111), new(-54, -110), new(-135, -189), new(-231, -107));
        Line(g, Color.FromArgb(154, Color.White), 1.4f, -244, -113, -138, -201);

        Rect(g, darkWood, -113, -83, 44, 74);
        Rect(g, C("#644957"), -109, -79, 36, 70);
        Ellipse(g, C("#e4b276"), -82, -48, 5, 5);
        Rect(g, trim, -121, -11, 61, 8);
        Rect(g, snow, -123, -4, 65, 5);

        if (lit)
        {
            Glow(g, new(-175, -60), 83, C("#ffbc68"), 82);
            Poly(g, Color.FromArgb(43, C("#ffc884")), new(-196, -47), new(-155, -47), new(-128, 8), new(-226, 8));
        }

        Rect(g, trim, -206, -86, 62, 47);
        Rect(g, lit ? C("#ffcf82") : C("#354860"), -202, -82, 54, 39);
        if (lit)
            Rect(g, C("#ffe3a2"), -202, -82, 54, 17);
        Line(g, darkWood, 2.4f, -175, -83, -175, -42);
        Line(g, darkWood, 2.4f, -202, -63, -148, -63);
        Rect(g, snow, -211, -41, 72, 5);
    }

    private static void DrawTree(Graphics g, float x, float y, int count, Color snow, float dark, int missing,
        int pondTree = -1)
    {
        Rect(g, C("#685358"), x - 7, y - 25, 14, 29);
        var green = Mix(C("#447775"), C("#294b57"), dark);
        using var fill = new SolidBrush(green);
        Fir(g, fill, x, y - 12, 178, 80);
        for (var i = 0; i < 3; i++)
        {
            var high = y - 190 + i * 46;
            var half = 44 + i * 16;
            Poly(g, snow, new(x, high), new(x - half, high + 55), new(x - half * .67f, high + 51),
                new(x - half * .37f, high + 40), new(x - 1, high + 42), new(x + half * .50f, high + 50),
                new(x + half, high + 53));
        }

        if (count >= 4)
        {
            using var cord = new Pen(C("#657e7e"), 1.4f);
            g.DrawBezier(cord, x - 32, -117, x - 8, -102, x + 21, -97, x + 40, -108);
            g.DrawBezier(cord, x - 53, -76, x - 19, -59, x + 27, -49, x + 55, -61);
            for (var i = 0; i < 8; i++)
            {
                var bulb = new PointF(x - 49 + i * 14, -70 + 11 * MathF.Sin(i * .44f));
                if (count == 7)
                    Glow(g, bulb, 12, OrnamentColors[i % 7], 96);
                Ellipse(g, OrnamentColors[i % 7], bulb.X - 2.3f, bulb.Y - 2, 4.6f, 5);
            }
        }

        for (var i = 0; i < Math.Min(count, Branches.Length); i++)
        {
            if (i != missing)
                Ornament(g, new(x + Branches[i].X, y + Branches[i].Y),
                    pondTree < 0 ? i : PondOrnament(pondTree, i), 1.05f);
        }
        if (count == 7)
        {
            Glow(g, new(x, -189), 31, C("#ffd886"), 126);
            Ornament(g, new(x, -190), 6, 1.25f);
        }
    }

    public static int PondOrnament(int tree, int ordinal) => (tree + 2 * ordinal) % 6;

    private static void DrawLantern(Graphics g, bool lit, Color snow, int frost)
    {
        var metal = C("#353e50");
        Line(g, metal, 5, -465, -91, -465, 9);
        Line(g, C("#768c9b"), 1, -466, -88, -466, 3);
        Rect(g, metal, -472, 5, 14, 5);
        Poly(g, metal, new(-477, -106), new(-453, -106), new(-457, -84), new(-473, -84));
        Poly(g, lit ? C("#ffdf94") : C("#879baa"), new(-473, -103), new(-457, -103), new(-460, -89), new(-470, -89));
        Line(g, metal, 1.5f, -465, -103, -465, -88);
        if (frost > 0)
            Poly(g, Color.FromArgb(75 + frost * 44, snow), new(-473, -103), new(-457, -103),
                new(-459, -95 + frost), new(-465, -97 + frost), new(-472, -94 + frost));
        Poly(g, snow, new(-481, -106), new(-465, -120), new(-449, -106));
    }

    public static void DrawGroundSnow(Graphics g, YardLayout layout, Daylight light, SnowCover cover,
        IReadOnlyList<Miniature> miniatures)
    {
        var dark = light.Shade / 16f;
        var scale = layout.Scale;
        var width = layout.View.Width;
        if (cover.Depth > 0 && cover.RightEdge > 0)
        {
            var layer = g.Save();
            g.SetClip(new Rectangle(0, 0, cover.RightEdge, layout.View.Height), CombineMode.Intersect);
            var ice = Mix(C("#a0bdcc"), C("#496782"), dark);
            var powder = Mix(C("#f3f8f7"), C("#c0d5e6"), dark);
            var height = cover.Depth / 3;
            var expansion = .33f + height * .67f;
            var y = layout.Anchor.Y;

            void Bank(float x, float offset, float wide, float high) =>
                DrawSnowBank(g, x, y + offset * scale, wide * expansion * scale, high * height * scale, ice, powder);

            Bank(width * .09f, 31, 150, 51);
            Bank(width * .23f, 48, 133, 43);
            Bank(width * .49f, 37, 142, 57);
            Bank(layout.Local(-493, 0).X, 28, 166, 47);
            Bank(layout.Local(-326, 0).X, 18, 189, 69);
            Bank(layout.Local(-149, 0).X, 25, 151, 53);
            g.Restore(layer);
        }

        foreach (var miniature in miniatures)
        {
            var layer = g.Save();
            if (miniature.Snowfalls >= Miniature.BurialLimit)
                g.SetClip(new Rectangle(0, 0, cover.RightEdge, layout.View.Height), CombineMode.Intersect);
            var stand = layout.MiniatureStand(miniature.Slot);
            g.TranslateTransform(stand.X - layout.View.X, stand.Y - layout.View.Y);
            g.ScaleTransform(scale, scale);
            var burial = miniature.Snowfalls + (miniature.HasHat ? cover.Fresh : 0);
            DrawMiniSnowman(g, miniature, dark, Math.Min(burial, Miniature.BurialLimit));
            g.Restore(layer);
        }
    }

    private static void DrawSnowBank(Graphics g, float x, float y, float width, float height, Color ice, Color snow)
    {
        var left = x - width / 2;
        var right = x + width / 2;
        using var contour = new GraphicsPath();
        contour.AddBezier(left, y, left + width * .14f, y - height * .18f, x - width * .27f,
            y - height, x - width * .06f, y - height * .96f);
        contour.AddBezier(x - width * .06f, y - height * .96f, x + width * .16f, y - height * .88f,
            right - width * .14f, y - height * .37f, right, y);
        contour.AddBezier(right, y, x + width * .24f, y + height * .19f, x - width * .25f,
            y + height * .14f, left, y);
        contour.CloseFigure();
        using var fill = new LinearGradientBrush(new RectangleF(left, y - height, width, height * 1.18f),
            snow, ice, 90);
        g.FillPath(fill, contour);
        using var rim = new Pen(Color.FromArgb(100, snow), Math.Max(1, height * .04f));
        g.DrawBezier(rim, left + width * .15f, y - height * .33f, x - width * .20f, y - height * .69f,
            x - width * .074f, y - height * .88f, x + width * .04f, y - height * .74f);
    }

    public static void Snowman(Graphics g, ActorFrame actor)
    {
        if (actor.Pose == Pose.PickUp)
            g.RotateTransform(14);
        var step = actor.WalkStep;
        g.TranslateTransform(0, -actor.Bob);
        var white = C("#f8faf6");
        var ice = C("#b5d1dc");
        var dark = C("#33435b");
        var scarf = C("#bb494b");
        var twig = C("#634656");
        Ellipse(g, dark, -26 + step * 6, -15, 21, 13);
        Ellipse(g, dark, 5 - step * 6, -15, 21, 13);
        Ellipse(g, ice, -26, -50, 52, 47);
        Ellipse(g, white, -25, -51, 46, 41);
        Ellipse(g, ice, -20, -75, 40, 44);
        Ellipse(g, white, -20, -77, 37, 42);
        Line(g, twig, 3.5f, -15, -49, -32, -57 - step * 4);
        var arm = actor.Pose switch
        {
            Pose.React or Pose.Hang => -88,
            Pose.Throw => -74,
            Pose.PickUp => -7,
            Pose.Wipe => -96 + 6 * MathF.Sin(actor.Time * 12),
            _ => -51 + step * 6
        };
        var reach = actor.Pose == Pose.Wipe ? 25 + 5 * MathF.Sin(actor.Time * 7) :
            actor.Pose is Pose.Throw or Pose.PickUp ? 43 : 31;
        Line(g, twig, 3.5f, 15, -53, reach, arm);
        Line(g, twig, 2, reach - 2, arm + 2, reach + 5, arm - 4);
        if (actor.Pose == Pose.Wipe)
            Poly(g, C("#a7ddd9"), new(reach - 4, arm - 4), new(reach + 5, arm - 7),
                new(reach + 8, arm + 3), new(reach - 1, arm + 6));

        Ellipse(g, ice, -20, -105, 41, 37);
        Ellipse(g, white, -20, -106, 37, 35);
        Poly(g, C("#ed8b55"), new(11, -87), new(30, -83), new(10, -80));
        Ellipse(g, dark, 3, -93, 4, 4);
        Ellipse(g, dark, 13, -92, 4, 4);
        Ellipse(g, C("#edb0a8"), -3, -83, 7, 4);
        Ellipse(g, dark, 7, -78, 2.5f, 2.5f);
        Ellipse(g, dark, 11, -77, 2.5f, 2.5f);
        if (!actor.NoHat)
            Hat(g, new(0, -SnowmanHatHeight), 1, 0);
        Ellipse(g, scarf, -20, -75, 41, 11);
        Poly(g, scarf, new(-11, -68), new(0, -68), new(-6 + step * 3, -48), new(-18 + step * 3, -49));
        Ellipse(g, dark, 6, -56, 4.5f, 4.5f);
        Ellipse(g, dark, 8, -45, 4.5f, 4.5f);

        if (actor.Toy >= 0)
        {
            var held = actor.Pose is Pose.React or Pose.Hang
                ? new PointF(35, -92) : new PointF(32, -54);
            if (actor.Toy == 3 && !actor.RecoveredToy)
            {
                using var cord = new Pen(C("#63867c"), 2);
                var end = new PointF(held.X + 27, -12);
                g.DrawBezier(cord, held.X, held.Y, held.X + 19, held.Y + 6, held.X - 3, end.Y, end.X, end.Y);
                for (var i = 0; i < 4; i++)
                {
                    var dot = new PointF(held.X + 7 + i * 4, held.Y + (end.Y - held.Y) * (i + 1) / 4.5f);
                    Ellipse(g, OrnamentColors[i], dot.X, dot.Y, 5, 5);
                }
            }
            else
                Ornament(g, held, actor.Toy, 1.35f);
        }

        if (actor.Snowball > 0)
            DrawSnowball(g, actor.Snowball, new(30, actor.Pose == Pose.Hang ? -88 : -56));
        if (actor.SpecialGift)
            WinterStar(g, new(40, -91), 10);

        if (actor.Pose != Pose.Shovel)
            return;

        var endX = 38 + 14 * MathF.Sin(actor.Time * 7);
        var endY = -12 + 9 * MathF.Cos(actor.Time * 7);
        Line(g, C("#a0735f"), 4, 14, -65, endX, endY);
        Poly(g, C("#7194ab"), new(endX - 7, endY - 4), new(endX + 8, endY - 6),
            new(endX + 13, endY + 7), new(endX - 4, endY + 12));
        Line(g, C("#e3edf1"), 2, endX - 3, endY + 10, endX + 12, endY + 6);
    }

    public static void FatherFrost(Graphics g, ActorFrame actor)
    {
        var stride = MathF.Sin(actor.Time * 8) * 5;
        var trim = C("#e5eff5");
        var coat = C("#8b3a53");
        var shadow = C("#542b47");
        Ellipse(g, Color.FromArgb(80, shadow), -39, -8, 77, 12);
        Ellipse(g, shadow, -24 + stride, -13, 23, 12);
        Ellipse(g, shadow, 4 - stride, -13, 21, 12);
        Poly(g, coat, new(-21, -85), new(20, -85), new(32, -12), new(-32, -12));
        Poly(g, shadow, new(-21, -82), new(-9, -80), new(-12, -14), new(-31, -14));
        Rect(g, trim, -30, -17, 60, 7);
        Rect(g, trim, -3, -78, 8, 62);
        Rect(g, shadow, -26, -47, 52, 6);
        Ellipse(g, C("#e4ad95"), -19, -114, 39, 39);
        Poly(g, trim, new(-19, -98), new(-4, -91), new(18, -98),
            new(21, -83), new(9, -63), new(-7, -60), new(-18, -78));
        Ellipse(g, shadow, 6, -101, 4, 4);
        Ellipse(g, C("#da9b89"), 12, -96, 8, 6);
        Poly(g, coat, new(-23, -109), new(-18, -129), new(12, -132), new(22, -109));
        Rect(g, trim, -23, -114, 45, 8);
        Line(g, coat, 10, 16, -78, 32, -58);
        Ellipse(g, trim, 26, -64, 12, 11);
        Line(g, C("#9fc1db"), 4, -40 + stride * .4f, -5, -40, -115);
        WinterStar(g, new(-40, -119), 9);
        if (actor.SpecialGift)
            DrawWinterGift(g, new(36, -47), .8f);
    }

    public static void DrawWinterGift(Graphics g, PointF basePoint, float scale)
    {
        var state = g.Save();
        g.TranslateTransform(basePoint.X, basePoint.Y);
        g.ScaleTransform(scale, scale);
        var ribbon = C("#f0ce87");
        Rect(g, C("#6e415d"), -15, -21, 30, 20);
        Rect(g, C("#a65967"), -16, -24, 32, 7);
        Rect(g, ribbon, -2, -24, 4, 23);
        Line(g, ribbon, 2, -6, -28, 0, -24);
        Line(g, ribbon, 2, 6, -28, 0, -24);
        g.Restore(state);
    }

    private static void WinterStar(Graphics g, PointF center, float radius)
    {
        for (var i = 0; i < 3; i++)
        {
            var angle = i * MathF.PI / 3;
            var x = MathF.Cos(angle) * radius;
            var y = MathF.Sin(angle) * radius;
            Line(g, C("#cdefff"), 2.1f, center.X - x, center.Y - y, center.X + x, center.Y + y);
        }
        Ellipse(g, Color.White, center.X - 2, center.Y - 2, 4, 4);
    }

    public static void DrawSnowball(Graphics g, float radius, PointF center)
    {
        Ellipse(g, C("#9fbed1"), center.X - radius, center.Y - radius, radius * 2, radius * 2);
        Ellipse(g, C("#e6f2f5"), center.X - radius * .96f, center.Y - radius,
            radius * 1.80f, radius * 1.79f);
        Ellipse(g, Color.FromArgb(146, Color.White), center.X - radius * .57f, center.Y - radius * .60f,
            radius * .55f, radius * .31f);
        Ellipse(g, Color.FromArgb(105, C("#a1bece")), center.X + radius * .31f, center.Y + radius * .29f,
            radius * .20f, radius * .14f);
    }

    private static void DrawMiniSnowman(Graphics g, Miniature miniature, float dark, float burial)
    {
        var body = miniature.Size;
        var head = body * .62f;
        var face = -2 * body - head * .64f;
        var coal = C("#43536b");
        Ellipse(g, Color.FromArgb(91, coal), -body * 1.25f, -5, body * 2.5f, 11);
        DrawSnowball(g, body, new(0, -body));
        DrawSnowball(g, head, new(0, face));
        Ellipse(g, C("#80a2aa"), -head * .80f, face + head * .61f, head * 1.64f, head * .30f);
        Poly(g, C("#80a2aa"), new(-head * .49f, face + head * .73f),
            new(-head * .09f, face + head * .77f), new(-head * .43f, face + head * 1.38f),
            new(-head * .67f, face + head * 1.28f));
        Ellipse(g, coal, -head * .36f, face - head * .29f, head * .16f, head * .19f);
        Ellipse(g, coal, head * .22f, face - head * .29f, head * .16f, head * .19f);
        using (var smile = new Pen(coal, Math.Max(1, head * .075f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(smile, -head * .31f, face - head * .03f, head * .62f, head * .47f, 15, 150);
        Ellipse(g, coal, -body * .06f, -body * 1.06f, body * .15f, body * .15f);
        if (miniature.HasHat)
            Hat(g, new(0, face - head * .77f), .46f + body / 220f, -7);
        if (burial > 0)
        {
            var height = body * burial * 1.29f;
            DrawSnowBank(g, body * .19f, 4, body * (2.7f + .52f * burial), height,
                Mix(C("#a5bdcc"), C("#58728d"), dark), Mix(C("#eff5f5"), C("#c0d5e6"), dark));
        }
    }

    public static void DrawSnowballTarget(Graphics g, PointF feet, float scale)
    {
        using var rim = new Pen(Color.FromArgb(125, C("#dbefee")), 1.6f * scale) { DashStyle = DashStyle.Dot };
        g.DrawEllipse(rim, feet.X - 39 * scale, feet.Y - 17 * scale, 78 * scale, 28 * scale);
    }

    public static void Hat(Graphics g, PointF position, float scale, float angle)
    {
        var state = g.Save();
        g.TranslateTransform(position.X, position.Y);
        g.RotateTransform(angle);
        g.ScaleTransform(scale, scale);
        var navy = C("#33435b");
        Poly(g, navy, new(-17, -1), new(-15, -21), new(11, -23), new(17, -3));
        Poly(g, C("#63788b"), new(-15, -19), new(-12, -20), new(-12, -3), new(-16, -3));
        Rect(g, C("#bb494b"), -16, -7, 31, 5);
        Ellipse(g, navy, -25, -5, 52, 8);
        g.Restore(state);
    }

    public static void Tractor(Graphics g, ActorFrame actor)
    {
        var ink = C("#283445");
        var trim = C("#deb379");
        var body = C("#c46348");
        var highlight = C("#f2a562");
        Ellipse(g, Color.FromArgb(75, ink), -84, -9, 172, 12);

        var saved = g.Save();
        g.TranslateTransform(-32, -44);
        g.ScaleTransform(.51f, .51f);
        Snowman(g, actor with { Pose = Pose.Rest, Toy = -1 });
        g.Restore(saved);

        Line(g, ink, 4, -65, -50, -65, -112);
        Line(g, ink, 4, -2, -49, -2, -112);
        Line(g, body, 6, -72, -113, 6, -113);
        Line(g, highlight, 2, -72, -116, 6, -116);
        Poly(g, body, new(-70, -42), new(-61, -56), new(59, -56), new(67, -36), new(58, -22), new(-68, -22));
        Rect(g, highlight, -3, -57, 62, 7);
        Rect(g, trim, 45, -49, 15, 8);
        Rect(g, C("#fcdfa0"), 60, -47, 7, 9);
        for (var i = 0; i < 3; i++)
            Line(g, C("#743d43"), 1.5f, 18 + i * 6, -44, 18 + i * 6, -34);
        Line(g, ink, 5, 35, -57, 35, -70);
        Line(g, ink, 4, 35, -70, 42, -70);

        Wheel(g, new(-40, -23), 23, actor.Time * 8, ink, trim);
        Wheel(g, new(44, -15), 15, actor.Time * 12, ink, trim);
        Line(g, ink, 5, 55, -29, 81, -22);
        Poly(g, C("#64879f"), new(75, -39), new(90, -37), new(88, -21),
            new(96, -9), new(80, -6), new(73, -17));
        Line(g, C("#d7e5e7"), 2.5f, 79, -8, 95, -11);

        if (!actor.Plowing)
            return;
        for (var i = 0; i < 7; i++)
        {
            var progress = (actor.Time * 5 + i * .23f) % 1;
            var x = 82 + 20 * progress;
            var y = -10 - MathF.Sin(progress * MathF.PI) * (9 + 5 * (i % 3));
            Ellipse(g, Color.FromArgb((int)(245 * (1 - progress)), C("#eff7f9")), x, y, 3, 3);
        }
    }

    private static void Wheel(Graphics g, PointF center, float radius, float rotation, Color ink, Color trim)
    {
        Ellipse(g, ink, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        Ellipse(g, C("#566474"), center.X - radius * .68f, center.Y - radius * .68f, radius * 1.36f, radius * 1.36f);
        for (var i = 0; i < 5; i++)
        {
            var angle = rotation + i * 2 * MathF.PI / 5;
            var x = MathF.Cos(angle) * radius * .56f;
            var y = MathF.Sin(angle) * radius * .56f;
            Line(g, trim, 2, center.X + x * .5f, center.Y + y * .5f, center.X + x, center.Y + y);
        }
        Ellipse(g, trim, center.X - 4, center.Y - 4, 8, 8);
    }

    public static void Owl(Graphics g, ActorFrame actor)
    {
        var flap = actor.Pose == Pose.Fly ? MathF.Sin(actor.Time * 17) * 25 : 0;
        var dark = C("#4d465b");
        var brown = C("#92747a");
        var feather = C("#cdaba3");
        var cream = C("#f4ddbf");
        if (actor.Pose == Pose.Fly)
        {
            Poly(g, dark, new(-10, -39), new(-31, -52 - flap), new(-48, -56 - flap),
                new(-36, -40 - flap), new(-24, -31), new(-9, -24));
            Poly(g, dark, new(10, -39), new(31, -52 - flap), new(48, -56 - flap),
                new(36, -40 - flap), new(24, -31), new(9, -24));
        }

        Ellipse(g, dark, -20, -53, 41, 49);
        Ellipse(g, feather, -14, -42, 28, 35);
        Ellipse(g, brown, -22, -64, 44, 33);
        Poly(g, brown, new(-20, -55), new(-20, -72), new(-7, -61));
        Poly(g, brown, new(20, -55), new(20, -72), new(7, -61));
        Ellipse(g, cream, -19, -60, 19, 22);
        Ellipse(g, cream, 0, -60, 19, 22);
        var look = actor.Pose == Pose.React ? actor.Direction * 2.5f : 0;
        Ellipse(g, C("#d89b58"), -14, -54, 10, 11);
        Ellipse(g, C("#d89b58"), 4, -54, 10, 11);
        Ellipse(g, dark, -11 + look, -53, 5, 8);
        Ellipse(g, dark, 7 + look, -53, 5, 8);
        Poly(g, C("#e1a76b"), new(-4, -45), new(4, -45), new(0, -38));
        Line(g, dark, 1.4f, -7, -27, -4, -25);
        Line(g, dark, 1.4f, 1, -20, 4, -18);
        Line(g, C("#ac795a"), 2.5f, -11, -5, -4, -4);
        Line(g, C("#ac795a"), 2.5f, 4, -4, 11, -5);
        if (actor.Toy >= 0)
            Ornament(g, new(9, -29), actor.Toy, .85f);
        if (actor.HoldingHat)
            Hat(g, new(9, -26), .76f, -22);
    }

    public static void Ornament(Graphics g, PointF at, int number, float scale)
    {
        var color = OrnamentColors[number];
        if (number == 6)
        {
            var points = new PointF[10];
            for (var i = 0; i < points.Length; i++)
            {
                var angle = -MathF.PI / 2 + i * MathF.PI / 5;
                var radius = (i % 2 == 0 ? 13 : 5.9f) * scale;
                points[i] = new(at.X + radius * MathF.Cos(angle), at.Y + radius * MathF.Sin(angle));
            }

            Poly(g, color, points);
            Line(g, C("#fff6dc"), scale, at.X, at.Y - 10 * scale, at.X - 3 * scale, at.Y - 2 * scale);
            return;
        }

        Rect(g, C("#e8c389"), at.X - 2 * scale, at.Y - 9 * scale, 4 * scale, 4 * scale);
        Ellipse(g, color, at.X - 7 * scale, at.Y - 6 * scale, 14 * scale, 14 * scale);
        Ellipse(g, Color.FromArgb(177, Color.White), at.X - 4 * scale, at.Y - 3 * scale, 3 * scale, 4 * scale);
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static void Fir(Graphics g, Brush fill, float x, float y, float height, float half)
    {
        PointF[] points = [
            new(x, y - height), new(x - half * .58f, y - height * .50f),
            new(x - half * .30f, y - height * .53f), new(x - half * .82f, y - height * .26f),
            new(x - half * .48f, y - height * .29f), new(x - half, y),
            new(x + half, y), new(x + half * .45f, y - height * .28f),
            new(x + half * .81f, y - height * .23f), new(x + half * .32f, y - height * .54f),
            new(x + half * .56f, y - height * .50f)
        ];
        g.FillPolygon(fill, points);
    }

    private static void Glow(Graphics g, PointF center, float radius, Color color, int alpha)
    {
        if (alpha == 0)
            return;

        using var ring = new GraphicsPath();
        ring.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var fill = new PathGradientBrush(ring)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = [Color.FromArgb(0, color)]
        };
        g.FillPath(fill, ring);
    }

    private static Color C(string html) => ColorTranslator.FromHtml(html);
    private static Color Mix(Color first, Color last, float amount) => Color.FromArgb(
        (int)(first.R + (last.R - first.R) * amount),
        (int)(first.G + (last.G - first.G) * amount),
        (int)(first.B + (last.B - first.B) * amount));

    private static void Rect(Graphics g, Color color, float x, float y, float width, float height)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y, width, height);
    }

    private static void Ellipse(Graphics g, Color color, float x, float y, float width, float height)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, y, width, height);
    }

    private static void Poly(Graphics g, Color color, params PointF[] points)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }

    private static void Line(Graphics g, Color color, float width, float x, float y, float endX, float endY)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, x, y, endX, endY);
    }
}
