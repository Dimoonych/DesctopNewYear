using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DesctopNewYear;

internal readonly record struct YardLayout(Rectangle View, Rectangle Work)
{
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
    public Rectangle SnowArea => Rectangle.FromLTRB(Math.Max(0, (int)(Anchor.X - 740 * Scale)), 0,
        View.Width, (int)Anchor.Y + 35);
}

internal static class YardArt
{
    private static readonly Color[] OrnamentColors =
    [
        C("#e76b58"), C("#f6c774"), C("#99d9d5"), C("#e68fae"), C("#9bc8ed"), C("#ee9c66"), C("#ffe4a2")
    ];
    private static readonly PointF[] Branches =
    [
        new(-25, -141), new(25, -113), new(-48, -89), new(44, -58),
        new(-18, -51), new(12, -80)
    ];

    public static Bitmap Landscape(YardLayout layout, Daylight light, int ornaments, int snow, int missing = -1)
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
        DrawForest(graphics, w, h, layout.Scale, dark);
        DrawGround(graphics, w, h, dark);

        graphics.TranslateTransform(layout.Anchor.X, layout.Anchor.Y);
        graphics.ScaleTransform(layout.Scale, layout.Scale);
        DrawYard(graphics, light, ornaments, snow, dark, missing);
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

    private static void DrawYard(Graphics g, Daylight light, int ornaments, int snow, float dark, int missing)
    {
        var snowColor = Mix(C("#f7fcf8"), C("#bdd3e2"), dark);
        var ice = Mix(C("#b2cbd2"), C("#506e89"), dark);
        Ellipse(g, Color.FromArgb(64, C("#294362")), -552, -15, 575, 55);
        using (var path = new Pen(Mix(C("#8faeb9"), C("#4e6078"), dark), 19)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
            g.DrawBezier(path, -496, 20, -407, -8, -270, 47, -91, -7);

        if (light.LanternLit)
        {
            Glow(g, new(-465, -78), 94, C("#ffd68d"), 72);
            Ellipse(g, Color.FromArgb(49, C("#ffdc9a")), -523, 1, 114, 26);
        }

        DrawHouse(g, snowColor, light.WindowLit, dark);
        DrawTree(g, -393, 0, ornaments, snowColor, dark, missing);
        DrawLantern(g, light.LanternLit, snowColor);
        DrawDrifts(g, snow, ice, snowColor);
    }

    private static void DrawHouse(Graphics g, Color snow, bool lit, float dark)
    {
        var siding = Mix(C("#986053"), C("#553d50"), dark);
        var trim = Mix(C("#e7b899"), C("#a17f86"), dark);
        var darkWood = C("#443445");
        Rect(g, C("#6e4b53"), -89, -195, 24, 55);
        Rect(g, snow, -93, -198, 31, 7);
        Poly(g, siding, new(-233, -105), new(-134, -179), new(-34, -105), new(-34, -9), new(-233, -9));
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

    private static void DrawTree(Graphics g, float x, float y, int count, Color snow, float dark, int missing)
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
                Ornament(g, new(x + Branches[i].X, y + Branches[i].Y), i, 1.05f);
        }
        if (count == 7)
        {
            Glow(g, new(x, -189), 31, C("#ffd886"), 126);
            Ornament(g, new(x, -190), 6, 1.25f);
        }
    }

    private static void DrawLantern(Graphics g, bool lit, Color snow)
    {
        var metal = C("#353e50");
        Line(g, metal, 5, -465, -91, -465, 9);
        Line(g, C("#768c9b"), 1, -466, -88, -466, 3);
        Rect(g, metal, -472, 5, 14, 5);
        Poly(g, metal, new(-477, -106), new(-453, -106), new(-457, -84), new(-473, -84));
        Poly(g, lit ? C("#ffdf94") : C("#879baa"), new(-473, -103), new(-457, -103), new(-460, -89), new(-470, -89));
        Line(g, metal, 1.5f, -465, -103, -465, -88);
        Poly(g, snow, new(-481, -106), new(-465, -120), new(-449, -106));
    }

    private static void DrawDrifts(Graphics g, int snow, Color ice, Color powder)
    {
        Ellipse(g, ice, -438, -7, 87, 16);
        Ellipse(g, powder, -435, -9, 78, 12);
        if (snow == 0)
            return;

        Ellipse(g, ice, -391, 8 - snow * 6, 94 + snow * 25, 12 + snow * 9);
        Ellipse(g, powder, -387, 4 - snow * 6, 86 + snow * 25, 10 + snow * 9);
        if (snow >= 2)
        {
            Ellipse(g, ice, -294, 4, 70 + snow * 12, 22);
            Ellipse(g, powder, -293, 1, 66 + snow * 12, 18);
        }
    }

    public static void Snowman(Graphics g, ActorFrame actor)
    {
        var step = actor.Pose == Pose.Walk ? MathF.Sin(actor.Time * 14) : 0;
        var bob = Math.Abs(step) * 2.5f;
        g.TranslateTransform(0, -bob);
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
        var arm = actor.Pose is Pose.React or Pose.Hang ? -88 : -51 + step * 6;
        Line(g, twig, 3.5f, 15, -53, 31, arm);
        Line(g, twig, 2, 29, arm + 2, 36, arm - 4);

        Ellipse(g, ice, -20, -105, 41, 37);
        Ellipse(g, white, -20, -106, 37, 35);
        Poly(g, C("#ed8b55"), new(11, -87), new(30, -83), new(10, -80));
        Ellipse(g, dark, 3, -93, 4, 4);
        Ellipse(g, dark, 13, -92, 4, 4);
        Ellipse(g, C("#edb0a8"), -3, -83, 7, 4);
        Ellipse(g, dark, 7, -78, 2.5f, 2.5f);
        Ellipse(g, dark, 11, -77, 2.5f, 2.5f);
        Poly(g, dark, new(-17, -103), new(-15, -123), new(11, -125), new(17, -105));
        Poly(g, C("#63788b"), new(-15, -121), new(-12, -122), new(-12, -105), new(-16, -105));
        Rect(g, scarf, -16, -109, 31, 5);
        Ellipse(g, dark, -25, -107, 52, 8);
        Ellipse(g, scarf, -20, -75, 41, 11);
        Poly(g, scarf, new(-11, -68), new(0, -68), new(-6 + step * 3, -48), new(-18 + step * 3, -49));
        Ellipse(g, dark, 6, -56, 4.5f, 4.5f);
        Ellipse(g, dark, 8, -45, 4.5f, 4.5f);

        if (actor.Toy >= 0)
        {
            var held = actor.Pose is Pose.React or Pose.Hang
                ? new PointF(35, -92) : new PointF(32, -54);
            if (actor.Toy == 3)
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

        if (actor.Pose != Pose.Shovel)
            return;

        var endX = 38 + 14 * MathF.Sin(actor.Time * 7);
        var endY = -12 + 9 * MathF.Cos(actor.Time * 7);
        Line(g, C("#a0735f"), 4, 14, -65, endX, endY);
        Poly(g, C("#7194ab"), new(endX - 7, endY - 4), new(endX + 8, endY - 6),
            new(endX + 13, endY + 7), new(endX - 4, endY + 12));
        Line(g, C("#e3edf1"), 2, endX - 3, endY + 10, endX + 12, endY + 6);
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
