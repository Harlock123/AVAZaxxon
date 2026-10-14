using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAZaxxon.Engine;
using Barrier = AVAZaxxon.Engine.Barrier;
using Effect = AVAZaxxon.Engine.Effect;

namespace AVAZaxxon.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAZaxxon/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Green = 0xFF70E070, Cyan = 0xFF5AD8FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    // ───────────────────────────── the projection ─────────────────────────────

    /// <summary>Screen pixels per unit forward and across, and per unit up.</summary>
    private const double S = 2.75, ZS = 3.5;
    /// <summary>Where the camera's point on the deck's centre line falls on screen.</summary>
    private const double OX = 130, OY = 600;
    private double _cam;

    /// <summary>A point of the world on screen: forward runs up to the right, across down to the right.</summary>
    private Point P(double x, double y, double z) =>
        new(OX + (x - _cam + y) * 0.866 * S, OY + (y - (x - _cam)) * 0.5 * S - z * ZS);

    /// <summary>The deck point under a screen point, for placing things on the title.</summary>
    private (double X, double Y) World(double sx, double sy)
    {
        double u = (sx - OX) / (0.866 * S), v = (sy - OY) / (0.5 * S);
        return (_cam + (u - v) / 2, (u + v) / 2);
    }

    /// <summary>A block: its top and the two faces turned to the viewer.</summary>
    private void Box(DrawingContext dc, double x0, double x1, double y0, double y1, double z0, double z1, uint colour, uint? edge = 0x60000000)
    {
        Poly(dc, Shade(colour, 0.82), edge, 1, P(x0, y0, z0), P(x0, y1, z0), P(x0, y1, z1), P(x0, y0, z1));
        Poly(dc, Shade(colour, 0.6), edge, 1, P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1));
        Poly(dc, Shade(colour, 1.12), edge, 1, P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1));
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        _cam = g.Cam;
        DrawSpace(dc);
        DrawDeck(dc);
        DrawScene(dc);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 26), 28, Yellow, center: true);
    }

    // ───────────────────────────── space and the deck ─────────────────────────────

    private void DrawSpace(DrawingContext dc)
    {
        for (int i = 0; i < 16; i++)
        {
            double t = i / 15.0;
            uint c = 0xFF000000 | (uint)(4 + 10 * t) << 16 | (uint)(6 + 12 * t) << 8 | (uint)(20 + 34 * t);
            dc.FillRectangle(Brush(c), new Rect(0, Field.Top + i * 44, Field.Width, 45));
        }
        // Stars, drifting down to the left as the ship flies on: the far ones slower.
        for (int i = 0; i < 150; i++)
        {
            uint h = Hash(i, 7, 3);
            double depth = 0.15 + (h % 100) / 100.0 * 0.5;
            double sx = (h >> 8) % 1400 - (_cam * depth * 2.5) % 1400;
            double sy = (h >> 18) % 900 + (_cam * depth * 1.45) % 900;
            sx = ((sx % 1400) + 1400) % 1400 - 180;
            sy = ((sy % 900) + 900) % 900 - 60;
            if (sy < Field.Top) continue;
            byte a = (byte)(90 + depth * 250);
            double r = depth > 0.5 ? 1.6 : 1.0;
            dc.FillRectangle(Brush((uint)a << 24 | 0x00E0E8FF), new Rect(sx, sy, r, r));
        }
        // In open space a faint grid far below gives the eye a floor to judge heights by.
        if (!Engine.HasDeck)
        {
            double start = Math.Floor((_cam - 160) / 40) * 40;
            for (double x = start; x < _cam + 420; x += 40)
                dc.DrawLine(Pen(0x302E6AC8, 1), P(x, -Field.Deck, 0), P(x, Field.Deck, 0));
            for (double y = -Field.Deck; y <= Field.Deck; y += 28)
                dc.DrawLine(Pen(0x302E6AC8, 1), P(_cam - 160, y, 0), P(_cam + 420, y, 0));
        }
    }

    private const uint Steel = 0xFF505A6E, SteelB = 0xFF5A6478;

    private void DrawDeck(DrawingContext dc)
    {
        var g = Engine;
        if (!g.HasDeck) return;
        double from = Math.Max(g.DeckStart, _cam - 170), to = Math.Min(g.DeckEnd, _cam + 420);
        if (to <= from) return;
        // The near edge: the deck is a slab, and its side shows.
        Poly(dc, 0xFF262B38, 0xFF181C26, 1, P(from, Field.Deck, 0), P(to, Field.Deck, 0), P(to, Field.Deck, -16), P(from, Field.Deck, -16));
        if (g.DeckStart > _cam - 170)
            Poly(dc, 0xFF343A4A, 0xFF181C26, 1, P(from, -Field.Deck, 0), P(from, Field.Deck, 0), P(from, Field.Deck, -16), P(from, -Field.Deck, -16));
        // Plates.
        double first = Math.Floor((from - g.DeckStart) / 30) * 30 + g.DeckStart;
        for (double x = first; x < to; x += 30)
        {
            double x0 = Math.Max(x, from), x1 = Math.Min(x + 30, to);
            int row = (int)Math.Round((x - g.DeckStart) / 30);
            for (int j = 0; j < 4; j++)
            {
                double y0 = -Field.Deck + j * 28, y1 = y0 + 28;
                uint c = (row + j) % 2 == 0 ? Steel : SteelB;
                Poly(dc, c, 0xFF3A4252, 1, P(x0, y0, 0), P(x1, y0, 0), P(x1, y1, 0), P(x0, y1, 0));
            }
            // Rivets and a vent here and there.
            if (Hash(row, 1, 9) % 5 == 0)
            {
                double vy = -40 + Hash(row, 2, 9) % 70;
                for (int k = 0; k < 4; k++)
                    dc.DrawLine(Pen(0xFF3A4252, 2), P(x0 + 6 + k * 4, vy, 0), P(x0 + 6 + k * 4, vy + 12, 0));
            }
        }
        // Rails along both edges.
        dc.DrawLine(Pen(0xFF8A96B0, 2), P(from, -Field.Deck, 0), P(to, -Field.Deck, 0));
        dc.DrawLine(Pen(0xFF8A96B0, 2), P(from, Field.Deck, 0), P(to, Field.Deck, 0));
        // Hazard stripes before each wall and field.
        foreach (var b in g.Barriers)
            for (int k = 0; k < 8; k++)
            {
                double y0 = -Field.Deck + k * 14;
                Poly(dc, k % 2 == 0 ? 0xFFD8B020 : 0xFF202020, null, 0, P(b.X - 10, y0, 0), P(b.X - 4, y0, 0), P(b.X - 4, y0 + 14, 0), P(b.X - 10, y0 + 14, 0));
            }
        // Scorch marks where things were shot.
        foreach (var t in g.Targets.Where(t => t.Dead))
        {
            var c = P(t.X, t.Y, 0);
            dc.DrawEllipse(Brush(0xA0141418), null, c, t.HalfWidth * S * 1.2, t.HalfWidth * S * 0.6);
        }
        // The ship's shadow, straight below it: the way to judge its height.
        var s = g.Ship;
        if (g.Phase != Phase.Dying && g.State is not (GameState.Title or GameState.HighScores or GameState.EnterInitials or GameState.GameOver) && s.X > g.DeckStart && s.X < g.DeckEnd)
            Poly(dc, 0x80000000, null, 0, P(s.X + 10, s.Y, 0), P(s.X - 5, s.Y + 8, 0), P(s.X - 7, s.Y, 0), P(s.X - 5, s.Y - 8, 0));
    }

    // ───────────────────────────── everything above the deck ─────────────────────────────

    private void DrawScene(DrawingContext dc)
    {
        var g = Engine;
        var items = new List<(double Key, Action Draw)>();
        void Add(double x, double y, double z, Action draw) => items.Add((y - x + z * 0.002, draw));
        double far = _cam + 430, near = _cam - 180;

        foreach (var t in g.Targets)
            if (!t.Dead && t.X < far && t.X > near) Add(t.X, t.Y, 0, () => DrawTarget(dc, t));
        foreach (var b in g.Barriers)
        {
            if (b.X > far || b.X < near) continue;
            foreach (var (y0, y1, z0) in WallPieces(b))
            {
                double a = y0, c = y1, bottom = z0;
                Add(b.X + 2, (a + c) / 2, 0, () => DrawWallPiece(dc, b, a, c, bottom));
            }
        }
        if (g.Robot is { } r && (!r.Dead || r.DeadTime < 1.3)) Add(r.X, r.Y, 0, () => DrawRobot(dc, r));
        bool showShip = g.State is GameState.Playing or GameState.Attract or GameState.Paused && g.Phase != Phase.Dying;
        if (showShip) Add(g.Ship.X, g.Ship.Y, g.Ship.Z, () => DrawShip(dc, g.Ship.X, g.Ship.Y, g.Ship.Z, g.Ship.Dx, g.Ship.Dy, false));
        foreach (var f in g.Fighters)
            if (!f.Dead) Add(f.X, f.Y, f.Z, () => DrawShip(dc, f.X, f.Y, f.Z, 0, 0, true));
        foreach (var s in g.Shots) Add(s.X, s.Y, s.Z, () => DrawShot(dc, s));
        foreach (var s in g.Shells) Add(s.X, s.Y, s.Z, () => DrawShell(dc, s));
        foreach (var rk in g.Rockets) Add(rk.X, rk.Y, rk.Z, () => DrawRocket(dc, rk));
        if (g.Missile is { } m) Add(m.X, m.Y, m.Z, () => DrawMissile(dc, m));
        // In open space, lines down to the grid show how high things are.
        if (!g.HasDeck && g.State is GameState.Playing or GameState.Attract or GameState.Paused)
        {
            if (showShip) DropLine(dc, g.Ship.X, g.Ship.Y, g.Ship.Z, Cyan);
            foreach (var f in g.Fighters.Where(f => !f.Dead)) DropLine(dc, f.X, f.Y, f.Z, 0xFFFF7050);
        }
        foreach (var (_, draw) in items.OrderBy(i => i.Key)) draw();
    }

    private void DropLine(DrawingContext dc, double x, double y, double z, uint colour)
    {
        dc.DrawLine(Pen(colour & 0x50FFFFFF, 1), P(x, y, 0), P(x, y, z));
        var c = P(x, y, 0);
        dc.DrawEllipse(Brush(colour & 0x60FFFFFF), null, c, 9, 4.5);
    }

    /// <summary>A wall in pieces across the deck, so the ship can be drawn between them as it passes the hole.</summary>
    private static IEnumerable<(double Y0, double Y1, double Z0)> WallPieces(Barrier b)
    {
        if (b.IsField)
        {
            for (double y = -Field.Deck; y < Field.Deck; y += 14) yield return (y, y + 14, b.FieldLow);
            yield break;
        }
        double lo = Math.Clamp(b.GapLo, -Field.Deck, Field.Deck), hi = Math.Clamp(b.GapHi, -Field.Deck, Field.Deck);
        foreach (var (a, c, z0) in new[] { (-Field.Deck, lo, 0.0), (lo, hi, b.GapTop), (hi, Field.Deck, 0.0) })
        {
            if (c - a < 0.01) continue;
            int n = (int)Math.Ceiling((c - a) / 10);
            for (int i = 0; i < n; i++) yield return (a + (c - a) * i / n, a + (c - a) * (i + 1) / n, z0);
        }
    }

    private void DrawWallPiece(DrawingContext dc, Barrier b, double y0, double y1, double z0)
    {
        if (b.IsField)
        {
            // The pylons at each end, and the crackling field between.
            if (y0 <= -Field.Deck) Box(dc, b.X - 1, b.X + 5, -Field.Deck - 6, -Field.Deck, 0, Field.Ceiling + 4, 0xFF6A7084);
            double flick = 0.6 + 0.4 * Math.Sin(_clock * 23 + y0);
            uint glow = (uint)(60 + 40 * flick) << 24 | 0x0050C8FF;
            double x = b.X + 2;
            Poly(dc, glow, null, 0, P(x, y0, z0), P(x, y1, z0), P(x, y1, Field.Ceiling), P(x, y0, Field.Ceiling));
            for (int k = 0; k < 4; k++)
            {
                double z = z0 + ((k * 13 + _clock * 30) % (Field.Ceiling - z0));
                dc.DrawLine(Pen(0xC0B0F0FF, 1.5), P(x, y0, z), P(x, y1, z + Math.Sin(_clock * 40 + y0 + k) * 2));
            }
            // The bottom edge burns brightest: that's the line to stay under.
            dc.DrawLine(Pen(0xFFB8F4FF, 3), P(x, y0, z0), P(x, y1, z0));
            if (y1 >= Field.Deck)
            {
                Box(dc, b.X - 1, b.X + 5, Field.Deck, Field.Deck + 6, 0, Field.Ceiling + 4, 0xFF6A7084);
                var lamp = P(b.X + 2, Field.Deck + 3, Field.Ceiling + 4);
                dc.DrawEllipse(Brush((int)(_clock * 3) % 2 == 0 ? Red : 0xFF601810), null, lamp, 4, 3);
            }
            return;
        }
        const uint brick = 0xFFA07A48;
        Box(dc, b.X, b.X + Barrier.Thickness, y0, y1, z0, Field.Ceiling, brick, 0x50000000);
        // Courses of bricks on the face towards the viewer.
        for (double z = z0 + 8; z < Field.Ceiling; z += 8)
            dc.DrawLine(Pen(0x40000000, 1), P(b.X, y0, z), P(b.X, y1, z));
        if (z0 > 0)
            // The lintel over the hole, lit so it reads as an opening.
            dc.DrawLine(Pen(0xFFFFD060, 2), P(b.X, y0, z0), P(b.X, y1, z0));
    }

    private void DrawTarget(DrawingContext dc, Target t)
    {
        double x = t.X, y = t.Y;
        switch (t.Kind)
        {
            case TargetKind.Turret:
            {
                Box(dc, x - 5, x + 5, y - 5, y + 5, 0, 4, 0xFF5E7A52);
                var c = P(x, y, 6);
                dc.DrawEllipse(Brush(0xFF7E9A68), Pen(0xFF2A3A24, 1), c, 11, 8);
                // The barrel, turned towards the ship.
                double dy = Math.Clamp((Engine.Ship.Y - y) / 60, -0.8, 0.8);
                dc.DrawLine(Pen(0xFF2A3A24, 4), c, P(x - 9, y + dy * 9, 8));
                break;
            }
            case TargetKind.FuelTank:
            {
                var b = P(x, y, 0);
                var top = P(x, y, t.Height);
                double rx = 6 * S * 1.0, ry = rx * 0.5;
                dc.DrawEllipse(Brush(0xFFA03018), null, b, rx, ry);
                dc.FillRectangle(Brush(0xFFC84020), new Rect(b.X - rx, top.Y, rx * 2, b.Y - top.Y));
                dc.FillRectangle(Brush(0xFF9A2C14), new Rect(b.X + rx * 0.3, top.Y, rx * 0.7, b.Y - top.Y));
                dc.DrawEllipse(Brush(0xFFE86A40), Pen(0xFF7A2010, 1), top, rx, ry);
                Text(dc, "FUEL", new Point(b.X, (b.Y + top.Y) / 2 + 2), 18, White, center: true, shadow: false);
                break;
            }
            case TargetKind.Radar:
            {
                Box(dc, x - 5, x + 5, y - 5, y + 5, 0, 5, 0xFF6A6E80);
                dc.DrawLine(Pen(0xFF9AA0B8, 3), P(x, y, 5), P(x, y, 11));
                var c = P(x, y, 12);
                double turn = Math.Sin(_clock * 2.2 + x);
                double rx = 4 + 9 * Math.Abs(turn);
                dc.DrawEllipse(Brush(0xFFD8DCE8), Pen(0xFF5A6070, 1.5), c, rx, 9);
                dc.DrawEllipse(Brush(0xFF8A90A8), null, new Point(c.X + turn * 3, c.Y), rx * 0.45, 4);
                dc.DrawLine(Pen(0xFF5A6070, 1.5), c, new Point(c.X + turn * 10, c.Y - 4));
                break;
            }
            case TargetKind.Plane:
                DrawPlane(dc, x, y, 1.5, 0xFF7A8A6A, 0xFF4A5A3E, -1);
                break;
            case TargetKind.Silo:
            {
                Poly(dc, 0xFF2A2A2E, 0xFFD8B020, 2, P(x - 4, y - 4, 0.2), P(x + 4, y - 4, 0.2), P(x + 4, y + 4, 0.2), P(x - 4, y + 4, 0.2));
                if (!t.Launched)
                {
                    Poly(dc, 0xFF8A8E9A, 0xFF404048, 1, P(x - 3, y - 3, 0.4), P(x + 3, y - 3, 0.4), P(x + 3, y + 3, 0.4), P(x - 3, y + 3, 0.4));
                    dc.DrawLine(Pen(0xFF404048, 1.5), P(x - 3, y, 0.4), P(x + 3, y, 0.4));
                }
                break;
            }
        }
    }

    /// <summary>A plane's outline lying flat at a height, nose towards +x or -x.</summary>
    private void DrawPlane(DrawingContext dc, double x, double y, double z, uint colour, uint dark, int dir, double bank = 0, double pitch = 0)
    {
        var nose = P(x + 10 * dir, y, z + pitch);
        var wingL = P(x - 5 * dir, y - 9, z - bank);
        var wingR = P(x - 5 * dir, y + 9, z + bank);
        var tail = P(x - 7 * dir, y, z);
        var mid = P(x + 1 * dir, y, z + 0.6);
        // Underside first, then the two wing halves lit differently.
        Poly(dc, Shade(colour, 1.1), dark, 1, nose, wingL, tail, mid);
        Poly(dc, Shade(colour, 0.75), dark, 1, nose, mid, tail, wingR);
        // The tail fin.
        Poly(dc, Shade(colour, 0.9), dark, 1, tail, P(x - 2 * dir, y, z + 0.5), P(x - 6 * dir, y, z + 5));
    }

    private void DrawShip(DrawingContext dc, double x, double y, double z, int dx, int dy, bool enemy)
    {
        if (enemy)
        {
            DrawPlane(dc, x, y, z, 0xFFE05A3A, 0xFF6A1A10, -1);
            var cock = P(x - 3, y, z + 1.2);
            dc.DrawEllipse(Brush(0xFFFFD040), null, cock, 3, 2);
            return;
        }
        DrawPlane(dc, x, y, z, 0xFFC8D4E8, 0xFF2A3448, 1, dx * 2.2, dy * 1.6);
        var c = P(x + 2, y, z + 1.4);
        dc.DrawEllipse(Brush(0xFF3AA8FF), Pen(0xFF10304A, 1), c, 4, 2.4);
        // The engine's glow at the tail.
        var tail = P(x - 7.5, y, z);
        double f = 0.7 + 0.3 * Math.Sin(_clock * 40);
        dc.DrawEllipse(Brush(0xC0FFA030), null, tail, 4 * f, 3 * f);
        dc.DrawEllipse(Brush(0xFFFFF0B0), null, tail, 1.8, 1.4);
    }

    private void DrawShot(DrawingContext dc, Bolt s)
    {
        var a = P(s.X - 7, s.Y, s.Z);
        var b = P(s.X + 2, s.Y, s.Z);
        dc.DrawLine(Pen(0x70FFE060, 6), a, b);
        dc.DrawLine(Pen(0xFFFFFFC0, 2.5), a, b);
    }

    private void DrawShell(DrawingContext dc, Bolt s)
    {
        var c = P(s.X, s.Y, s.Z);
        double f = 0.8 + 0.2 * Math.Sin(_clock * 30 + s.X);
        dc.DrawEllipse(Brush(0xA0FF5020), null, c, 7 * f, 7 * f);
        dc.DrawEllipse(Brush(0xFFFFE0A0), null, c, 3.5, 3.5);
    }

    private void DrawRocket(DrawingContext dc, Bolt r)
    {
        var b = P(r.X, r.Y, r.Z);
        var t = P(r.X, r.Y, r.Z + 9);
        dc.DrawEllipse(Brush(0xB0FFA030), null, new Point(b.X, b.Y + 6), 4, 9);
        dc.DrawLine(Pen(0xFFE8E8F0, 6), b, t);
        dc.DrawLine(Pen(0xFF9098A8, 2), new Point(b.X + 2, b.Y), new Point(t.X + 2, t.Y));
        Poly(dc, Red, null, 0, new Point(t.X - 3, t.Y), new Point(t.X + 3, t.Y), new Point(t.X, t.Y - 8));
        Poly(dc, 0xFFB03020, null, 0, new Point(b.X - 6, b.Y), new Point(b.X - 3, b.Y - 8), new Point(b.X - 3, b.Y));
        Poly(dc, 0xFFB03020, null, 0, new Point(b.X + 6, b.Y), new Point(b.X + 3, b.Y - 8), new Point(b.X + 3, b.Y));
    }

    private void DrawMissile(DrawingContext dc, Bolt m)
    {
        // Pointing back at the ship, with a trail of fire behind it.
        for (int i = 1; i <= 5; i++)
        {
            var p = P(m.X + 6 + i * 4, m.Y, m.Z);
            dc.DrawEllipse(Brush((uint)(180 - i * 30) << 24 | 0x00FF8030), null, p, 5 - i * 0.6, 4 - i * 0.5);
        }
        var nose = P(m.X - 10, m.Y, m.Z);
        var tail = P(m.X + 7, m.Y, m.Z);
        dc.DrawLine(Pen(0x60FF4020, 14), tail, nose);
        dc.DrawLine(Pen(0xFFE04040, 8), tail, nose);
        dc.DrawLine(Pen(0xFFFFB0A0, 2.5), tail, nose);
        dc.DrawEllipse(Brush((int)(_clock * 10) % 2 == 0 ? White : Red), null, nose, 4, 4);
    }

    private void DrawRobot(DrawingContext dc, Robot r)
    {
        double x = r.X, y = r.Y;
        double sink = r.Dead ? r.DeadTime * 30 : 0;
        uint body = r.Flash > 0 ? 0xFFF0F0FF : 0xFF5A78B8;
        uint dark = r.Flash > 0 ? 0xFFD0D0E0 : 0xFF3A4C80;
        // A slow stomp as it walks.
        double step = r.Dead ? 0 : Math.Max(0, Math.Sin(r.Age * 3)) * 3;
        Box(dc, x - 4, x + 4, y - 12, y - 6, 0, 18 - sink + step, dark);
        Box(dc, x - 4, x + 4, y + 6, y + 12, 0, 18 - sink + (r.Dead ? 0 : Math.Max(0, -Math.Sin(r.Age * 3)) * 3), dark);
        if (sink > 40) return;
        double charge = r.Dead ? 0 : r.Charge / Engine.ChargeTime;
        // The other arm hangs at its far side.
        Box(dc, x - 3, x + 3, y - 21, y - 15, 16 - sink, 40 - sink, 0xFF8A94A8);
        Box(dc, x - 7, x + 7, y - 15, y + 15, 18 - sink, 44 - sink, body);
        // A chest plate and a grille.
        for (int k = 0; k < 4; k++)
            dc.DrawLine(Pen(0x80101830, 2), P(x - 7, y - 8 + k * 5, 24 - sink), P(x - 7, y - 8 + k * 5, 38 - sink));
        Box(dc, x - 5, x + 5, y - 7, y + 7, 44 - sink, 54 - sink, body);
        // The eye.
        dc.DrawLine(Pen(r.Dead ? 0xFF402020 : 0xFFFF3020, 4), P(x - 5, y - 5, 50 - sink), P(x - 5, y + 5, 50 - sink));
        // The launcher arm on the near side, held out towards the ship, its muzzle glowing as the missile charges.
        uint arm = r.Flash > 0 ? 0xFFFFF0C0 : 0xFF9AA2B4;
        Box(dc, x - 4, x + 2, y + 15, y + 21, 26 - sink, 40 - sink, arm);
        Box(dc, x - 20, x - 2, y + 10, y + 18, 22 - sink, 30 - sink, arm);
        var muzzle = P(x - 20, y + 14, 26 - sink);
        uint glow = (uint)(90 + 165 * Math.Clamp(charge, 0, 1)) << 24 | 0x00FF3010;
        dc.DrawEllipse(Brush(0xFF1A1E28), Pen(0xFFFFD060, 1.5), muzzle, 9, 8);
        dc.DrawEllipse(Brush(glow), null, muzzle, 3 + 5 * charge, 3 + 4.5 * charge);
        // The hits so far, as lamps on its shoulder.
        if (!r.Dead)
            for (int k = 0; k < Robot.HitsToKill; k++)
                dc.DrawEllipse(Brush(k < r.Hits ? Yellow : 0xFF303848), null, P(x - 7, y - 12 + k * 4.8, 46 - sink), 3, 2);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = P(e.X, e.Y, e.Z);
        switch (e.Kind)
        {
            case EffectKind.Boom:
            case EffectKind.BigBoom:
            {
                double size = e.Kind == EffectKind.BigBoom ? 60 : 30;
                double r = size * (0.3 + t);
                byte a = (byte)(255 * (1 - t));
                dc.DrawEllipse(Brush((uint)a << 24 | 0x00FF6020), null, c, r, r * 0.8);
                dc.DrawEllipse(Brush((uint)a << 24 | 0x00FFD060), null, c, r * 0.55, r * 0.45);
                for (int i = 0; i < 12; i++)
                {
                    double ang = i * 0.524 + Hash(i, (int)e.X, 5) % 10 * 0.05, sp = size * (1 + Hash(i, 2, 7) % 10 / 10.0);
                    double px = c.X + Math.Cos(ang) * sp * t * 1.6, py = c.Y + Math.Sin(ang) * sp * t - 30 * t + 90 * t * t;
                    dc.FillRectangle(Brush((uint)a << 24 | (i % 2 == 0 ? 0x00FFF0A0u : 0x00808898u)), new Rect(px, py, 4, 4));
                }
                break;
            }
            case EffectKind.Spark:
                dc.DrawEllipse(Brush((uint)(255 * (1 - t)) << 24 | 0x00FFFFC0), null, c, 8 * (1 - t) + 2, 8 * (1 - t) + 2);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 20 - t * 30), 26, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the panel and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0C16), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF3A5A9A), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(14, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(14, 42), 36, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 42), 30, Dim);
        Text(dc, $"LEVEL {g.Level}", new Point(320, 16), 22, Dim);
        string where = g.Section switch
        {
            Section.Fortress => "FORTRESS",
            Section.Space => $"FIGHTERS {Math.Max(0, g.PlanesLeft)}",
            Section.Inner => "INNER FORTRESS",
            _ => "GOLIATH",
        };
        Text(dc, where, new Point(320, 42), 30, Yellow);
        // Fuel.
        bool low = g.Fuel < GameEngine.LowFuel;
        Text(dc, "FUEL", new Point(560, 16), 22, low && (int)(_clock * 4) % 2 == 0 ? Red : Dim);
        dc.DrawRectangle(Brush(0xFF1A1E2A), Pen(0xFF6A7AA0, 1.5), new Rect(560, 32, 220, 20));
        double f = g.Fuel / GameEngine.FuelMax;
        uint fc = f > 0.5 ? Green : f > 0.25 ? Yellow : Red;
        dc.FillRectangle(Brush(fc), new Rect(562, 34, 216 * f, 16));
        // Ships in reserve.
        Text(dc, "SHIPS", new Point(820, 16), 22, Dim);
        for (int i = 0; i < Math.Min(6, g.Lives - (g.Phase == Phase.Dying ? 0 : 1)); i++)
        {
            double x = 830 + i * 30, y = 44;
            Poly(dc, 0xFFC8D4E8, 0xFF2A3448, 1, new Point(x + 12, y - 6), new Point(x - 8, y - 4), new Point(x - 4, y + 2), new Point(x + 4, y + 6));
        }
        if (g.State is not (GameState.Playing or GameState.Attract or GameState.Paused)) return;
        DrawAltimeter(dc);
    }

    /// <summary>The altimeter down the left: the ship's height, and the way through the next wall or field.</summary>
    private void DrawAltimeter(DrawingContext dc)
    {
        var g = Engine;
        const double top = 110, bottom = 560, x = 18, w = 30;
        double Y(double z) => bottom - (bottom - top) * z / Field.Ceiling;
        dc.FillRectangle(Brush(0xB00A0C16), new Rect(x - 6, top - 30, w + 12, bottom - top + 46));
        Text(dc, "ALT", new Point(x + w / 2, top - 16), 22, Dim, center: true);
        dc.DrawRectangle(Brush(0xFF14182A), Pen(0xFF4A5A80, 1), new Rect(x, top, w, bottom - top));
        var next = g.Barriers.Where(b => b.X + Barrier.Thickness > g.Ship.X && b.X - g.Ship.X < 300).OrderBy(b => b.X).FirstOrDefault();
        if (next != null)
        {
            double open = next.IsField ? next.FieldLow : next.GapTop;
            dc.FillRectangle(Brush(0x6040E070), new Rect(x + 1, Y(open), w - 2, bottom - Y(open)));
            dc.DrawLine(Pen(next.IsField ? Cyan : 0xFFFFD060, 2), new Point(x, Y(open)), new Point(x + w, Y(open)));
        }
        for (int z = 0; z <= 60; z += 10)
            dc.DrawLine(Pen(0xFF4A5A80, 1), new Point(x, Y(z)), new Point(x + 6, Y(z)));
        if (g.Phase != Phase.Dying)
        {
            double sy = Y(g.Ship.Z);
            Poly(dc, Cyan, 0xFF0A2030, 1, new Point(x + w + 4, sy), new Point(x - 2, sy - 7), new Point(x - 2, sy + 7));
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Message is not { } msg) return;
        bool bad = g.Phase == Phase.Dying;
        Text(dc, msg, new Point(cx, 170), 66, bad ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 222), 32, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB8080A14), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA ZAXXON", new Point(cx, 150), 128, 0xFF5AB4FF, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 222), 28, Dim, center: true);
        string[] lines =
        [
            "FLY OVER THE FORTRESS: THROUGH THE HOLES IN ITS WALLS,",
            "UNDER ITS FORCE FIELDS - WATCH YOUR SHADOW AND THE ALTIMETER",
            "SHOOT FUEL TANKS TO KEEP FLYING, BEAT THE FIGHTERS IN SPACE,",
            "AND HIT THE GOLIATH'S LAUNCHER SIX TIMES BEFORE IT FIRES",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 280 + i * 34), 28, White, center: true);
        // What things are worth, with a picture of each.
        (TargetKind Kind, string Name, string Points)[] table =
        [
            (TargetKind.FuelTank, "FUEL", "300"), (TargetKind.Turret, "GUN", "200"), (TargetKind.Radar, "RADAR", "1000"),
            (TargetKind.Plane, "PLANE", "50"), (TargetKind.Silo, "SILO", "150"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double sx = 132 + i * 152;
            var (wx, wy) = World(sx, 500);
            DrawTarget(dc, new Target { Kind = table[i].Kind, X = wx, Y = wy });
            Text(dc, table[i].Name, new Point(sx, 528), 24, Dim, center: true);
            Text(dc, table[i].Points, new Point(sx, 554), 28, Yellow, center: true);
        }
        var (fx, fy) = World(892, 512);
        DrawShip(dc, fx, fy, 6, 0, 0, true);
        Text(dc, "FIGHTER", new Point(892, 528), 24, Dim, center: true);
        Text(dc, "150", new Point(892, 554), 28, Yellow, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 620), 48, Yellow, center: true);
        Text(dc, "ARROWS: ACROSS, CLIMB AND DIVE    Z / SPACE: FIRE", new Point(cx, 680), 28, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0080A14), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP PILOTS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0080A14), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
