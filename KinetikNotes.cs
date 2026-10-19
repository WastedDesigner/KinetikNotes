// Kinetik Notes: sticky notes on the desktop, in the style of Kinetik's widget, Kinetik Calendar and Kinetik Clock.
// Shows one note per Markdown file in a folder (e.g. an Obsidian to-do folder) and follows the folder live.
// Tasks ("- [ ] …") can be ticked off on the note, which writes the change back to the file.
// Per-note options live in the file's front matter, so Obsidian sees them too:
//   color: pink · title: … · pinned: true · due: 2026-10-20 · tags: [web, marking]
// Copyright © 2026 Akila Sella Hennedige. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Kinetik Notes")]
[assembly: AssemblyProduct("Kinetik Notes")]
[assembly: AssemblyCopyright("Copyright © 2026 Akila Sella Hennedige")]
[assembly: AssemblyVersion("1.7.0.0")]

static class Program
{
    public const string Version = "1.7.0";
    // --demo: made-up notes in a temporary folder, no settings read from or saved to this PC.
    // "--theme Name" picks its theme (for screenshots).
    public static bool Demo;
    public static string DemoTheme;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        Demo = args.Contains("--demo");
        int ti = Array.IndexOf(args, "--theme");
        if (ti >= 0 && ti + 1 < args.Length) DemoTheme = args[ti + 1];
        bool created;
        using (new Mutex(true, Demo ? @"Local\KinetikNotesDemo" : @"Local\KinetikNotes", out created))
        {
            if (!created) return; // already running
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new DarkMenu();
            // "--settings Page" opens the settings window on that page when it starts
            int si = Array.IndexOf(args, "--settings");
            Backup.KeyName = "KinetikNotes"; Backup.AppTitle = "Kinetik Notes";
            if (!Demo) Backup.OfferRestore();
            var app = new NotesForm(si < 0 ? null : si + 1 < args.Length ? args[si + 1] : "General");
            using (new KinetikSuite.Shutdown("KinetikNotes" + (Demo ? "Demo" : ""), () => { app.Shutdown(); Application.Exit(); }))
                Application.Run(app);
        }
        if (!Demo) Backup.AfterExit();
    }
}

// ======================================================================= Demo
// Made-up notes for --demo, written fresh to a temporary folder each time, so the widget can be shown
// (and screenshotted) without reading anyone's real notes.
static class DemoData
{
    static readonly string[][] Files =
    {
        new[] { "Groceries.md", "---\ncolor: green\npinned: true\n---\n# Groceries\n\n- [x] Oat milk\n- [x] Bananas\n- [ ] Sourdough\n- [ ] Coffee beans\n- [ ] Basil\n- [ ] Parmesan\n" },
        new[] { "Website launch.md", "---\ncolor: blue\ndue: {due}\ntags: [web]\n---\n# Website launch\n\n- [x] Final copy from marketing\n- [x] Compress hero images\n- [ ] Test the contact form\n- [ ] Set up redirects\n- [ ] Announce on socials\n" },
        new[] { "Weekend.md", "---\ncolor: pink\n---\n# Weekend\n\n- [ ] Farmers market, 8 am\n- [ ] Return library books\n- [x] Book the campsite\n- [ ] Call Grandma\n" },
        new[] { "Reading list.md", "---\ncolor: purple\ntags: [books]\n---\n# Reading list\n\n- [x] The Left Hand of Darkness\n- [ ] Piranesi\n- [ ] A Psalm for the Wild-Built\n" },
        new[] { "Ideas.md", "---\ncolor: yellow\n---\n# Ideas\n\nA tiny app that waters the plants **when it rains less than 2 mm** in a week.\n\n- [ ] Sketch the sensor box\n" },
        new[] { "Gym.md", "---\ncolor: orange\n---\n# Gym\n\n- [x] Mon: legs\n- [x] Wed: push\n- [ ] Fri: pull\n- [ ] Sun: 5 km run\n" },
    };

    public static Settings Settings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "KinetikNotesDemo");
        try
        {
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.md")) File.Delete(f);
            Directory.CreateDirectory(dir);
            string due = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd");
            foreach (var f in Files) File.WriteAllText(Path.Combine(dir, f[0]), f[1].Replace("{due}", due));
        }
        catch { }
        var s = new Settings { Folder = dir, Layer = 2, Columns = 3, X = 240, Y = 160, Hotkey = false, ThemeName = Program.DemoTheme ?? "Pastel paper", ShowTags = true };
        s.Sanitize();
        return s;
    }
}

// ======================================================================= Settings
class Settings
{
    const string KeyPath = @"Software\KinetikNotes";
    public const string DefaultTemplate = "# New note\n\n- [ ] ";

    // Placement
    public int X = int.MinValue, Y = int.MinValue, Layer = 0; // Layer: 0 on the desktop, 1 normal, 2 always on top
    public bool Locked, Snap = true;
    // Look
    public int Width = 260, Scale = 100, Columns = 1, Round = 10, Gap = 10, Opacity = 100;
    public int MaxHeight = 0;      // note length in pixels (at 100% DPI): a longer note scrolls inside itself; 0 = no limit
    public int Style = 0;          // legacy (v1.1): 0 pastel, 1 vivid, 2 dark; only read to seed ThemeName
    public string ThemeName = "";  // a Notes.ThemeNames name; "Auto" matches Windows' light or dark mode
    public int DefaultColour = -1; // -1 = a different colour for each note, otherwise an index into Notes.ColourNames
    public string FontName = "";   // "" = Segoe UI
    public bool ShowCount = true, ShowProgress = true, ShowDue = true, ShowTags = false;
    // Which notes, and how
    public int Sort = 0;           // 0 file name, 1 title, 2 last changed, 3 due date, 4 most open tasks
    public int MaxLines = 0;       // 0 = no limit
    public bool PinnedFirst = true, HideDone, HideFinishedNotes, Subfolders;
    public string Folder = "", TagFilter = "", Hidden = "", Collapsed = ""; // Hidden/Collapsed: note keys joined by '|'
    // Behaviour
    public int DoubleClick = 0;    // 0 open in Obsidian, 1 open in the default editor, 2 show in File Explorer
    public bool Hotkey = true, OpenNew = true, AskTitle = true;
    public string Template = DefaultTemplate;

    static IEnumerable<FieldInfo> Fields
    {
        get
        {
            return typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType == typeof(string));
        }
    }

    // Set when the saved settings exist but couldn't be read. The app then runs on defaults, and Save does nothing,
    // so those defaults can never overwrite the real settings. Restarting the app tries again.
    public static bool Unreadable;

    // Reads the settings, retrying a few times: the key can be briefly unreadable, e.g. while a backup is being
    // restored into it or another program has it busy.
    public static Settings Load()
    {
        if (Program.Demo) return DemoData.Settings();
        Settings s = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) Thread.Sleep(500);
            bool ok;
            s = TryLoad(out ok);
            if (ok) { Unreadable = false; s.Opacity = CommonMenu.Migrate(s.Opacity); return s; }
        }
        Unreadable = true;
        MessageBox.Show("Kinetik Notes couldn't read its saved settings, so it's showing the default look and layout for now.\n\n" +
            "Your settings haven't been changed, and nothing will be saved over them while this is showing. " +
            "Restart Kinetik Notes to try again" + (Backup.Exists ? ", or use Settings → General → Restore." : "."),
            "Kinetik Notes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return s;
    }

    // ok is false when the settings are there but couldn't be read (a missing key is fine: first run).
    static Settings TryLoad(out bool ok)
    {
        ok = true;
        var s = new Settings();
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                if (k != null)
                    foreach (var f in Fields)
                    {
                        var v = k.GetValue(f.Name);
                        if (v == null) continue;
                        try
                        {
                            if (f.FieldType == typeof(string)) f.SetValue(s, v.ToString());
                            else if (f.FieldType == typeof(int)) f.SetValue(s, Convert.ToInt32(v));
                            else f.SetValue(s, Convert.ToInt32(v) != 0);
                        }
                        catch { ok = false; }
                    }
        }
        catch { ok = false; }
        s.Sanitize();
        KinetikSuite.SettingsValidation.Sanitize(s); return s;
    }

    public void Sanitize()
    {
        Func<int, int, int, int> cl = (v, lo, hi) => Math.Max(lo, Math.Min(hi, v));
        Width = cl(Width, 160, 800); Scale = cl(Scale, 50, 250); Layer = cl(Layer, 0, 2); Columns = cl(Columns, 1, 6);
        Round = cl(Round, 0, 30); Gap = cl(Gap, 0, 40); Opacity = cl(Opacity, 20, 100); Style = cl(Style, 0, 2);
        DefaultColour = cl(DefaultColour, -1, Notes.ColourNames.Length - 1); Sort = cl(Sort, 0, 4); MaxLines = cl(MaxLines, 0, 99);
        DoubleClick = cl(DoubleClick, 0, 2); MaxHeight = MaxHeight <= 0 ? 0 : cl(MaxHeight, 80, 4000);
        if (Template == null) Template = DefaultTemplate;
        if (!Notes.ThemeNames.Contains(ThemeName)) ThemeName = Style == 1 ? "Vivid paper" : Style == 2 ? "Navy" : "Pastel paper";
    }

    public void Save()
    {
        if (Program.Demo || Unreadable) return;
        KinetikSuite.SettingsValidation.Sanitize(this);
        if (KinetikSuite.RegistryFiles.CannotSave) return; // never save defaults over settings that couldn't be read
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                foreach (var f in Fields)
                {
                    var v = f.GetValue(this);
                    if (f.FieldType == typeof(bool)) k.SetValue(f.Name, (bool)v ? 1 : 0);
                    else if (f.FieldType == typeof(int)) k.SetValue(f.Name, (int)v);
                    else k.SetValue(f.Name, (string)v ?? "");
                }
        }
        catch { }
    }

    // Lists of note keys kept in one string, e.g. the hidden and rolled-up notes.
    public static bool InList(string list, string key) { return ("|" + (list ?? "") + "|").Contains("|" + key + "|"); }
    public static string WithKey(string list, string key, bool on)
    {
        var l = (list ?? "").Split('|').Where(x => x != "" && x != key).ToList();
        if (on) l.Add(key);
        return string.Join("|", l);
    }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Kinetik Notes";

    public static bool IsOn
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
        }
        catch { }
    }
}

// ======================================================================= Themes (same set as Kinetik Clock and Kinetik Calendar)
class Theme
{
    public readonly string Name;
    public readonly Color Bg, Text, Sub, Accent;
    public readonly bool Light;

    public Theme(string name, Color bg, Color text, Color sub, Color accent, bool light)
    {
        Name = name; Bg = bg; Text = text; Sub = sub; Accent = accent; Light = light;
    }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    // The terminal themes (the same green, amber and yellow as Kinetik's dashboard and Kinetik Screen) draw in a
    // monospace font unless a font is picked, and their divider lines glow in the theme's colour.
    public bool Term { get { return IsTerm(Name); } }
    public static bool IsTerm(string name) { return name == "Terminal" || name == "Amber terminal" || name == "Yellow terminal"; }
    static string mono;
    public static string Mono
    {
        get
        {
            if (mono == null)
            {
                mono = "Consolas";
                try { using (var f = new Font("Cascadia Mono", 10f)) if (f.Name == "Cascadia Mono") mono = "Cascadia Mono"; } catch { }
            }
            return mono;
        }
    }

    public static readonly Theme[] All =
    {
        new Theme("Midnight", C(22, 22, 26), C(242, 242, 246), C(168, 168, 180), C(240, 88, 88), false),
        new Theme("Navy", C(22, 27, 40), C(236, 240, 248), C(138, 150, 176), C(77, 166, 255), false),
        new Theme("Graphite", C(40, 40, 44), C(236, 236, 236), C(160, 160, 166), C(255, 159, 10), false),
        new Theme("Nord", C(46, 52, 64), C(236, 239, 244), C(152, 162, 182), C(136, 192, 208), false),
        new Theme("Dracula", C(40, 42, 54), C(248, 248, 242), C(150, 156, 192), C(255, 121, 198), false),
        new Theme("Solarized", C(0, 43, 54), C(238, 232, 213), C(131, 148, 150), C(203, 75, 22), false),
        new Theme("Forest", C(22, 33, 27), C(228, 240, 231), C(138, 166, 148), C(108, 201, 124), false),
        new Theme("Ocean", C(12, 29, 45), C(228, 242, 250), C(128, 160, 186), C(56, 198, 232), false),
        new Theme("Sunset", C(40, 23, 35), C(250, 236, 240), C(192, 150, 172), C(255, 138, 92), false),
        new Theme("Mono", C(10, 10, 10), C(245, 245, 245), C(140, 140, 140), C(245, 245, 245), false),
        new Theme("OLED black", C(0, 0, 0), C(242, 242, 245), C(136, 136, 146), C(76, 194, 255), false),
        new Theme("Mocha", C(30, 30, 46), C(205, 214, 244), C(147, 153, 178), C(203, 166, 247), false),
        new Theme("Terminal", C(3, 10, 5), C(90, 255, 120), C(46, 170, 80), C(205, 255, 215), false),
        new Theme("Amber terminal", C(12, 7, 0), C(255, 182, 40), C(185, 118, 22), C(255, 232, 180), false),
        new Theme("Yellow terminal", C(0, 0, 0), C(255, 226, 0), C(200, 178, 0), C(255, 255, 190), false),
        new Theme("Light", C(246, 246, 248), C(24, 24, 28), C(96, 96, 106), C(230, 70, 70), true),
        new Theme("Paper", C(250, 246, 236), C(52, 42, 32), C(132, 116, 96), C(204, 92, 52), true),
        new Theme("Frost", C(236, 242, 250), C(20, 30, 48), C(92, 106, 132), C(40, 120, 230), true),
    };

    public static Theme Get(string name)
    {
        // "App" follows the theme and accent colour every Kinetik app shares.
        if (name == "App") { var l = KinetikSuite.Look.Current; return new Theme("App", l.Card, l.Text, l.Sub, l.Accent, l.Light); }
        if (name == "Auto")
        {
            bool light = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    light = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            }
            catch { }
            return Get(light ? "Light" : "Midnight");
        }
        foreach (var t in All) if (t.Name == name) return t;
        return All[0];
    }
}

// A theme tile for the settings window: little sticky notes drawn in that theme, like the pickers in the other Kinetik widgets.
class ThemeTile : Control
{
    public static readonly Color Page = Color.FromArgb(20, 23, 32), Card = Color.FromArgb(34, 39, 54), CardHi = Color.FromArgb(44, 51, 70), Accent = Color.FromArgb(90, 160, 255);
    public readonly string ThemeName;
    bool selected, hot;
    float sel;
    readonly float k;
    readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 15 };

    public ThemeTile(string name, bool isSelected, float scale)
    {
        ThemeName = name; selected = isSelected; sel = isSelected ? 1 : 0; k = scale;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(I(110), I(88));
        Margin = new Padding(0, 0, I(8), I(8));
        Cursor = Cursors.Hand;
        BackColor = Page;
        t.Tick += (s, e) =>
        {
            float target = selected ? 1 : 0;
            sel += (target - sel) * 0.3f;
            if (Math.Abs(target - sel) < 0.02f) { sel = target; t.Stop(); }
            Invalidate();
        };
    }

    int I(float v) { return (int)Math.Round(v * k); }
    public void SetSelected(bool v) { if (selected == v) return; selected = v; t.Start(); }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(1, 1, Width - 3, Height - 3);
        Draw.Fill(g, outer, I(10), hot ? CardHi : Card);
        if (sel > 0) Draw.Stroke(g, outer, I(10), Color.FromArgb((int)(255 * sel), Accent), 2);
        var inner = new RectangleF(I(8), I(8), Width - I(16), Height - I(36));
        if (ThemeName == "Auto")
        {
            Mini(g, inner, "Midnight");
            var st = g.Save();
            g.SetClip(new RectangleF(inner.X + inner.Width / 2, inner.Y, inner.Width, inner.Height));
            Mini(g, inner, "Light");
            g.Restore(st);
        }
        else Mini(g, inner, ThemeName);
        using (var f = new Font("Segoe UI", 9f))
            TextRenderer.DrawText(g, ThemeName == "Auto" ? "Match Windows" : ThemeName == "App" ? "Match app theme" : ThemeName, f, new Rectangle(0, Height - I(27), Width, I(20)), selected ? Color.FromArgb(232, 236, 244) : Color.FromArgb(150, 158, 178),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // Two overlapping mini notes (yellow and blue), so the tile shows how note colours come through.
    void Mini(Graphics g, RectangleF r, string theme)
    {
        for (int i = 0; i < 2; i++)
        {
            Color card, band, ink, soft, accent;
            Notes.Look(theme, i == 0 ? 0 : 4, out card, out band, out ink, out soft, out accent);
            var nr = i == 0 ? new RectangleF(r.X, r.Y, r.Width * 0.62f, r.Height) : new RectangleF(r.X + r.Width * 0.42f, r.Y + I(8), r.Width * 0.58f, r.Height - I(8));
            Draw.Fill(g, nr, I(5), card);
            using (var p = Draw.Round(nr, I(5))) { var st = g.Save(); g.SetClip(p); using (var b = new SolidBrush(band)) g.FillRectangle(b, nr.X, nr.Y, nr.Width, I(10)); g.Restore(st); }
            Draw.Fill(g, new RectangleF(nr.X + I(5), nr.Y + I(3.5f), nr.Width * 0.5f, I(3.5f)), I(1.5f), ink);
            for (int j = 0; j < 2; j++)
            {
                float yy = nr.Y + I(15) + j * I(9);
                Draw.Stroke(g, new RectangleF(nr.X + I(5), yy, I(5), I(5)), I(1.2f), accent, 1.2f * k);
                Draw.Fill(g, new RectangleF(nr.X + I(13), yy + I(1), nr.Width * (0.55f - j * 0.15f), I(3)), I(1.5f), Color.FromArgb(170, soft));
            }
        }
    }
}

// ======================================================================= Emoji
// GDI+ can't draw colour emoji (they come out as outlines or boxes), and neither can WPF on .NET Framework. So text
// that has any is laid out by DirectWrite and drawn by Direct2D with its colour-font option, into a bitmap that's cached
// until the text, font or colour changes. Plain text stays on GDI+. Direct2D and DirectWrite are called through their
// COM vtables (slot numbers from d2d1.h / dwrite.h), as there's no wrapper for them in .NET Framework.
static class Emoji
{
    [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(int type, ref Guid riid, IntPtr opts, out IntPtr factory);
    [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(int type, ref Guid riid, out IntPtr factory);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct RTProps { public int Type, Format, Alpha; public float DpiX, DpiY; public int Usage, MinLevel; }
    [StructLayout(LayoutKind.Sequential)] struct ColorF { public float R, G, B, A; }
    [StructLayout(LayoutKind.Sequential)] struct Point2F { public float X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct TextRange { public uint Start, Length; }
    [StructLayout(LayoutKind.Sequential)] struct Trimming { public int Granularity; public uint Delimiter, DelimiterCount; }
    [StructLayout(LayoutKind.Sequential)] struct TextMetrics { public float Left, Top, Width, WidthTrailing, Height, LayoutWidth, LayoutHeight; public uint MaxBidi, LineCount; }

    delegate uint ReleaseFn(IntPtr self);
    delegate int CreateDCTargetFn(IntPtr self, ref RTProps p, out IntPtr target);
    delegate int CreateTextFormatFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string family, IntPtr collection, int weight, int style, int stretch, float size, [MarshalAs(UnmanagedType.LPWStr)] string locale, out IntPtr format);
    delegate int CreateTextLayoutFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text, uint length, IntPtr format, float maxW, float maxH, out IntPtr layout);
    delegate int CreateEllipsisFn(IntPtr self, IntPtr format, out IntPtr sign);
    delegate int SetIntFn(IntPtr self, int value);
    delegate int SetTrimmingFn(IntPtr self, ref Trimming t, IntPtr sign);
    delegate int SetStrikeFn(IntPtr self, int on, TextRange range);
    delegate int GetMetricsFn(IntPtr self, out TextMetrics m);
    delegate int BindDCFn(IntPtr self, IntPtr hdc, ref Rect r);
    delegate int CreateBrushFn(IntPtr self, ref ColorF c, IntPtr props, out IntPtr brush);
    delegate void BeginDrawFn(IntPtr self);
    delegate void SetModeFn(IntPtr self, int mode);
    delegate void ClearFn(IntPtr self, ref ColorF c);
    delegate int EndDrawFn(IntPtr self, IntPtr tag1, IntPtr tag2);
    delegate void DrawLayoutFn(IntPtr self, Point2F origin, IntPtr layout, IntPtr brush, int options);

    static T Fn<T>(IntPtr obj, int slot) where T : class
    {
        var vtable = Marshal.ReadIntPtr(obj);
        return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size), typeof(T)) as T;
    }
    static void Release(IntPtr p) { if (p != IntPtr.Zero) Fn<ReleaseFn>(p, 2)(p); }

    static IntPtr d2d, dwrite, target;
    static bool tried, ok;
    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    static bool Init()
    {
        if (tried) return ok;
        tried = true;
        try
        {
            var iidD2D = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
            var iidDW = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
            ok = D2D1CreateFactory(0, ref iidD2D, IntPtr.Zero, out d2d) >= 0 && DWriteCreateFactory(0, ref iidDW, out dwrite) >= 0;
        }
        catch { ok = false; }
        return ok;
    }

    // True when the text has emoji and they can be drawn in colour here (Windows 8.1 or later).
    public static bool Has(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        bool any = false;
        foreach (var ch in s)
            if (char.IsSurrogate(ch) || ch == '\uFE0F' || ch == '\u200D' || ch == '\u203C' || ch == '\u2049' ||
                (ch >= '\u2300' && ch <= '\u23FF') || (ch >= '\u2600' && ch <= '\u27BF') || (ch >= '\u2B00' && ch <= '\u2BFF')) { any = true; break; }
        return any && Init();
    }

    static float Px(Font f) { return f.Unit == GraphicsUnit.Pixel ? f.Size : f.SizeInPoints * 96f / 72f; }

    static IntPtr Layout(string s, Font f, float maxW, bool oneLine)
    {
        IntPtr format;
        if (Fn<CreateTextFormatFn>(dwrite, 15)(dwrite, f.FontFamily.Name, IntPtr.Zero, f.Bold ? 700 : 400, f.Italic ? 2 : 0, 5, Px(f), "en-us", out format) < 0) return IntPtr.Zero;
        try
        {
            if (!oneLine) Fn<SetIntFn>(format, 5)(format, 3); // EMERGENCY_BREAK: a word longer than the line breaks mid-word (Windows 8.1+; ignored before)
            if (oneLine)
            {
                Fn<SetIntFn>(format, 5)(format, 1); // SetWordWrapping(NO_WRAP)
                IntPtr sign;
                if (Fn<CreateEllipsisFn>(dwrite, 20)(dwrite, format, out sign) >= 0)
                {
                    var tr = new Trimming { Granularity = 1 }; // character
                    Fn<SetTrimmingFn>(format, 9)(format, ref tr, sign);
                    Release(sign);
                }
            }
            IntPtr layout;
            if (Fn<CreateTextLayoutFn>(dwrite, 18)(dwrite, s, (uint)s.Length, format, Math.Max(1, maxW), 100000, out layout) < 0) return IntPtr.Zero;
            if (f.Strikeout) Fn<SetStrikeFn>(layout, 37)(layout, 1, new TextRange { Start = 0, Length = (uint)s.Length });
            return layout;
        }
        finally { Release(format); }
    }

    static float LayoutHeight(IntPtr layout)
    {
        TextMetrics m;
        return Fn<GetMetricsFn>(layout, 60)(layout, out m) >= 0 ? m.Height : 0;
    }

    public static float Height(string s, Font f, float maxW)
    {
        var layout = Layout(s, f, maxW, false);
        if (layout == IntPtr.Zero) return f.Height;
        try { return LayoutHeight(layout); } finally { Release(layout); }
    }

    static Bitmap Render(string s, Font f, Color col, float width, bool oneLine)
    {
        var layout = Layout(s, f, width, oneLine);
        if (layout == IntPtr.Zero) return null;
        IntPtr dc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero, brush = IntPtr.Zero;
        try
        {
            int w = Math.Max(1, (int)Math.Ceiling(width) + 2), h = Math.Max(1, (int)Math.Ceiling(LayoutHeight(layout)) + 2);
            if (target == IntPtr.Zero)
            {
                // Software: it only draws small bitmaps into a DIB, and a hardware target would load the whole graphics driver
                // (some 200 MB and a dozen threads) and copy every result back from the GPU.
                var props = new RTProps { Type = 1, Format = 87, Alpha = 1, DpiX = 96, DpiY = 96 }; // SOFTWARE, B8G8R8A8, premultiplied
                if (Fn<CreateDCTargetFn>(d2d, 16)(d2d, ref props, out target) < 0) { target = IntPtr.Zero; return null; }
            }
            var bi = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 }; // top-down
            IntPtr bits;
            dc = CreateCompatibleDC(IntPtr.Zero);
            dib = CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return null;
            old = SelectObject(dc, dib);
            var rc = new Rect { R = w, B = h };
            if (Fn<BindDCFn>(target, 57)(target, dc, ref rc) < 0) return null;
            var c = new ColorF { R = col.R / 255f, G = col.G / 255f, B = col.B / 255f, A = col.A / 255f };
            if (Fn<CreateBrushFn>(target, 8)(target, ref c, IntPtr.Zero, out brush) < 0) return null;
            Fn<BeginDrawFn>(target, 48)(target);
            Fn<SetModeFn>(target, 34)(target, 2); // grayscale antialiasing: ClearType needs an opaque background
            var clear = new ColorF();
            Fn<ClearFn>(target, 47)(target, ref clear);
            Fn<DrawLayoutFn>(target, 28)(target, new Point2F(), layout, brush, 4); // ENABLE_COLOR_FONT
            if (Fn<EndDrawFn>(target, 49)(target, IntPtr.Zero, IntPtr.Zero) < 0) { Release(target); target = IntPtr.Zero; return null; }

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var buf = new byte[w * h * 4];
            Marshal.Copy(bits, buf, 0, buf.Length);
            for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }
        finally
        {
            Release(brush);
            if (old != IntPtr.Zero) SelectObject(dc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            Release(layout);
        }
    }

    public static void Draw(Graphics g, string s, Font f, Color col, RectangleF r, bool oneLine)
    {
        var key = s + "\u0001" + f.FontFamily.Name + "|" + f.Size + "|" + (int)f.Style + "|" + col.ToArgb() + "|" + (int)r.Width + "|" + oneLine;
        Bitmap bmp;
        if (!cache.TryGetValue(key, out bmp))
        {
            if (cache.Count > 400) Clear();
            bmp = Render(s, f, col, r.Width, oneLine);
            cache[key] = bmp;
        }
        if (bmp == null) { using (var b = new SolidBrush(col)) g.DrawString(s, f, b, r); return; } // Direct2D failed: plain text
        // drawn 1:1 at whole pixels so the text stays sharp
        var st = g.Save();
        g.SetClip(new RectangleF(r.X, r.Y, r.Width + 2, Math.Max(r.Height, bmp.Height)), CombineMode.Intersect);
        g.DrawImageUnscaled(bmp, (int)Math.Round(r.X), (int)Math.Round(r.Y));
        g.Restore(st);
    }

    public static void Clear()
    {
        foreach (var b in cache.Values) if (b != null) b.Dispose();
        cache.Clear();
    }
}
// ======================================================================= Drawing helpers and the dark menu
static class Draw
{
    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float rad, Color col)
    {
        using (var path = Round(r, rad)) using (var b = new SolidBrush(col)) g.FillPath(b, path);
    }

    public static void Stroke(Graphics g, RectangleF r, float rad, Color col, float width)
    {
        using (var path = Round(r, rad)) using (var p = new Pen(col, width)) g.DrawPath(p, path);
    }

    public static void Chevron(Graphics g, RectangleF r, bool up, Color col, float width)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, w = r.Width * 0.28f, h = r.Height * 0.16f;
        using (var p = new Pen(col, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            if (up) g.DrawLines(p, new[] { new PointF(cx - w, cy + h), new PointF(cx, cy - h), new PointF(cx + w, cy + h) });
            else g.DrawLines(p, new[] { new PointF(cx - w, cy - h), new PointF(cx, cy + h), new PointF(cx + w, cy - h) });
    }

    static string[] families;
    public static string[] Families
    {
        get
        {
            if (families == null)
                using (var fonts = new InstalledFontCollection())
                    families = fonts.Families.Select(f => f.Name).ToArray();
            return families;
        }
    }

    public static string DefaultFamily { get { return Families.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI"; } }
}

class DarkColors : ProfessionalColorTable
{
    static Color Bg { get { return KinetikSuite.Look.Current.Card; } }
    static Color Hi { get { return KinetikSuite.Look.Current.Hover; } }
    static Color Edge { get { return KinetikSuite.Look.Current.Border; } }
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Edge; } }
    public override Color MenuItemBorder { get { return Hi; } }
    public override Color MenuItemSelected { get { return Hi; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
    public override Color MenuItemPressedGradientBegin { get { return Hi; } }
    public override Color MenuItemPressedGradientEnd { get { return Hi; } }
    public override Color SeparatorDark { get { return Edge; } }
    public override Color SeparatorLight { get { return Edge; } }
    public override Color CheckBackground { get { return KinetikSuite.Look.Current.Hover; } }
    public override Color CheckSelectedBackground { get { return KinetikSuite.Look.Current.Hover; } }
    public override Color CheckPressedBackground { get { return KinetikSuite.Look.Current.Hover; } }
}

class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? KinetikSuite.Look.Current.Text : KinetikSuite.Look.Current.Sub;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = KinetikSuite.Look.Current.Sub;
        base.OnRenderArrow(e);
    }
}

// ======================================================================= Reading and writing the Markdown notes
class NoteLine
{
    public int Kind; // 0 text, 1 heading, 2 bullet, 3 task, 4 blank gap
    public string Text;
    public bool Done;
    public int Indent, Src; // Src = the line's index in the file, for ticking a task
}

class Note
{
    public string Path, Key, Title, Revision;
    public int Colour = -1; // index into Notes.ColourNames, -1 = not set in the file
    public bool Pinned;
    public int Tasks, DoneTasks;
    public DateTime? Due;
    public DateTime Modified;
    public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<NoteLine> Lines = new List<NoteLine>();
}

static class Notes
{
    public static readonly string[] ColourNames = { "yellow", "orange", "pink", "purple", "blue", "green" };
    // The two paper looks colour the whole note; every other theme (the same set as Kinetik's other widgets)
    // uses its own background and text, and tints the title band with the note's colour.
    public static readonly string[] PaperThemes = { "Pastel paper", "Vivid paper" };
    public static IEnumerable<string> ThemeNames { get { return PaperThemes.Concat(new[] { "Auto", "App" }).Concat(Theme.All.Select(t => t.Name)); } }
    static readonly Color[] pastel = { Color.FromArgb(255, 241, 150), Color.FromArgb(255, 207, 150), Color.FromArgb(255, 190, 210), Color.FromArgb(218, 200, 255), Color.FromArgb(185, 224, 255), Color.FromArgb(196, 240, 188) };
    static readonly Color[] vivid = { Color.FromArgb(255, 214, 10), Color.FromArgb(255, 149, 0), Color.FromArgb(255, 105, 150), Color.FromArgb(170, 128, 255), Color.FromArgb(64, 168, 255), Color.FromArgb(70, 200, 110) };
    static readonly Color[] deep = { Color.FromArgb(200, 150, 0), Color.FromArgb(220, 110, 20), Color.FromArgb(215, 60, 120), Color.FromArgb(130, 80, 220), Color.FromArgb(30, 120, 220), Color.FromArgb(40, 150, 80) };
    public static Color Swatch(int i) { return pastel[i]; }
    public static bool IsPaper(string theme) { return PaperThemes.Contains(theme); }

    // Card, title band, text, faded text and accent (tick boxes, progress) colours for a note in a theme.
    public static void Look(string theme, int colour, out Color card, out Color band, out Color ink, out Color soft, out Color accent)
    {
        if (IsPaper(theme))
        {
            bool v = theme == "Vivid paper";
            card = v ? vivid[colour] : pastel[colour]; band = Color.FromArgb(24, 0, 0, 0);
            ink = Color.FromArgb(40, 36, 24); soft = v ? Color.FromArgb(80, 70, 45) : Color.FromArgb(125, 110, 80);
            accent = Color.FromArgb(150, ink);
            return;
        }
        var t = Theme.Get(theme);
        accent = t.Light ? deep[colour] : vivid[colour];
        card = t.Bg; band = Color.FromArgb(t.Light ? 60 : 75, accent); ink = t.Text; soft = t.Sub;
    }

    static readonly Regex TaskRx = new Regex(@"^([-*+]\s+\[)([ xX])(\]\s?)(.*)$");
    static readonly Regex TaskLineRx = new Regex(@"^(\s*[-*+]\s+\[)([ xX])(\].*)$");
    static readonly Regex BulletRx = new Regex(@"^([-*+]|\d+[.)])\s+(.*)$");
    static readonly Regex WikiRx = new Regex(@"\[\[([^\]|]+)\|?([^\]]*)\]\]");
    static readonly Regex LinkRx = new Regex(@"\[([^\]]+)\]\([^)]*\)");
    static readonly Regex HeadingRx = new Regex(@"^#{1,6}\s");
    static readonly Regex TagRx = new Regex(@"(?<![\w&/])#([A-Za-z][\w/-]*)");

    sealed class CachedNote { public long Stamp, Length; public Note Value; }
    static readonly object cacheGate = new object();
    static readonly Dictionary<string, CachedNote> cache = new Dictionary<string, CachedNote>(StringComparer.OrdinalIgnoreCase);
    static DateTime verifiedAt;
    public static void Invalidate(string path) { lock (cacheGate) { if (path == null) cache.Clear(); else cache.Remove(path); } }
    static Note ReadCached(string path)
    {
        var info = new FileInfo(path);
        lock (cacheGate)
        {
            CachedNote entry;
            if (cache.TryGetValue(path, out entry) && entry.Stamp == info.LastWriteTimeUtc.Ticks && entry.Length == info.Length) return entry.Value;
        }
        var note = Read(path);
        lock (cacheGate) cache[path] = new CachedNote { Stamp = info.LastWriteTimeUtc.Ticks, Length = info.Length, Value = note };
        return note;
    }

    public static List<Note> Load(string folder, bool subfolders)
    {
        var list = new List<Note>();
        lock (cacheGate) if ((DateTime.UtcNow - verifiedAt).TotalSeconds >= 60) { cache.Clear(); verifiedAt = DateTime.UtcNow; }
        var files = KinetikSuite.Markdown.Files(folder, subfolders)
            .Where(p => !p.Contains("\\.obsidian\\") && !p.Contains("\\.trash\\"))
            .Where(p => !p.StartsWith(TemplatesDir(folder) + "\\", StringComparison.OrdinalIgnoreCase)) // templates aren't notes
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var path in files)
        {
            try
            {
                if (new FileInfo(path).Length > 256 * 1024) continue;
                var n = ReadCached(path);
                n.Key = path.Substring(folder.TrimEnd('\\').Length).TrimStart('\\').ToLowerInvariant();
                if (n.Colour < 0) n.Colour = -2 - (i % ColourNames.Length); // -2… = picked by position, kept apart from "set in the file"
                i++;
                list.Add(n);
            }
            catch { }
        }
        return list;
    }

    static string[] ReadLines(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs, Encoding.UTF8, true))
            return sr.ReadToEnd().Split('\n');
    }

    static void AddTags(Note n, string s)
    {
        foreach (var t in s.Trim('[', ']').Split(',', ' ')) { var v = t.Trim().Trim('"', '\'').TrimStart('#'); if (v != "") n.Tags.Add(v); }
    }

    static Note Read(string path)
    {
        var n = new Note { Path = path, Title = Path.GetFileNameWithoutExtension(path), Modified = File.GetLastWriteTime(path) };
        var document = KinetikSuite.TextDocument.Read(path);
        n.Revision = document.Revision;
        var lines = document.Text.Split('\n');
        int i = 0;
        bool titled = false, inCode = false, inTags = false; char fence = '\0'; int fenceLength = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (i = 1; i < lines.Length && lines[i].Trim() != "---"; i++)
            {
                var l = lines[i];
                if (inTags && l.TrimStart().StartsWith("- ")) { AddTags(n, l.TrimStart().Substring(2)); continue; }
                inTags = false;
                int colon = l.IndexOf(':');
                if (colon <= 0) continue;
                var key = l.Substring(0, colon).Trim().ToLowerInvariant();
                var val = l.Substring(colon + 1).Trim().Trim('"', '\'');
                if (key == "color" || key == "colour") n.Colour = Array.IndexOf(ColourNames, val.ToLowerInvariant());
                else if (key == "title" && val != "") { n.Title = val; titled = true; }
                else if (key == "pinned") n.Pinned = val.ToLowerInvariant() == "true";
                else if (key == "due") { DateTime d; if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) || DateTime.TryParse(val, out d)) n.Due = d.Date; }
                else if (key == "tags" || key == "tag") { if (val == "") inTags = true; else AddTags(n, val); }
            }
            i++;
        }
        for (; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var t = raw.TrimStart();
            if (KinetikSuite.Markdown.Fence(t, ref fence, ref fenceLength)) { inCode = fence != '\0'; continue; }
            if (t == "")
            {
                if (n.Lines.Count > 0 && n.Lines[n.Lines.Count - 1].Kind != 4) n.Lines.Add(new NoteLine { Kind = 4 });
                continue;
            }
            if (!inCode) foreach (Match tm in TagRx.Matches(t)) n.Tags.Add(tm.Groups[1].Value);
            int spaces = 0;
            foreach (var ch in raw) { if (ch == ' ') spaces++; else if (ch == '\t') spaces += 4; else break; }
            var line = new NoteLine { Src = i, Indent = Math.Min(4, spaces / 2) };
            Match m;
            if (inCode) { line.Kind = 0; line.Text = t; }
            else if ((m = TaskRx.Match(t)).Success)
            {
                line.Kind = 3; line.Done = m.Groups[2].Value != " "; line.Text = Clean(m.Groups[4].Value);
                n.Tasks++; if (line.Done) n.DoneTasks++;
            }
            else if (HeadingRx.IsMatch(t))
            {
                var h = Clean(t.TrimStart('#').Trim());
                if (!titled && n.Lines.Count == 0 && t.StartsWith("# ")) { n.Title = h; titled = true; continue; }
                line.Kind = 1; line.Text = h; line.Indent = 0;
            }
            else if ((m = BulletRx.Match(t)).Success) { line.Kind = 2; line.Text = Clean(m.Groups[2].Value); }
            else { line.Kind = 0; line.Text = Clean(t); }
            n.Lines.Add(line);
        }
        while (n.Lines.Count > 0 && n.Lines[n.Lines.Count - 1].Kind == 4) n.Lines.RemoveAt(n.Lines.Count - 1);
        return n;
    }

    // Strips the Markdown that would look like clutter on a note: links, bold, code ticks, highlights.
    static string Clean(string s)
    {
        s = WikiRx.Replace(s, m => m.Groups[2].Value != "" ? m.Groups[2].Value : m.Groups[1].Value);
        s = LinkRx.Replace(s, "$1");
        return s.Replace("**", "").Replace("__", "").Replace("`", "").Replace("~~", "").Replace("==", "");
    }

    // ---- writing: every change keeps the file's encoding (BOM or not) and line endings
    public static bool Toggle(string path, int line, string revision)
    {
        try
        {
            if (string.IsNullOrEmpty(revision)) throw new IOException("Refresh the note before editing it.");
            KinetikSuite.SafeEdit.Update(path, revision, doc =>
            {
                var lines = doc.Text.Split('\n');
                if (line < 0 || line >= lines.Length) throw new IOException("The task moved. Refresh the note.");
                var m = TaskLineRx.Match(lines[line]);
                if (!m.Success) throw new IOException("The task changed. Refresh the note.");
                lines[line] = m.Groups[1].Value + (m.Groups[2].Value == " " ? "x" : " ") + m.Groups[3].Value;
                return string.Join("\n", lines);
            });
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Kinetik Notes", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
    }

    // Sets (or, with null, removes) one key in the note's front matter, adding the front matter if it has none.
    public static bool SetMeta(string path, string revision, string key, string value)
    {
        try
        {
            KinetikSuite.SafeEdit.Update(path, revision, doc =>
            {
            var lines = doc.Text.Split('\n').ToList();
            string cr = lines.Count > 0 && lines[0].EndsWith("\r") ? "\r" : "";
            int end = -1;
            if (lines.Count > 0 && lines[0].Trim() == "---")
                for (int i = 1; i < lines.Count; i++) if (lines[i].Trim() == "---") { end = i; break; }
            if (end < 0)
            {
                if (value == null) return doc.Text;
                lines.InsertRange(0, new[] { "---" + cr, key + ": " + value + cr, "---" + cr });
            }
            else
            {
                int at = -1;
                for (int i = 1; i < end; i++)
                {
                    var l = lines[i]; int colon = l.IndexOf(':');
                    if (colon > 0 && l.Substring(0, colon).Trim().ToLowerInvariant() == key) { at = i; break; }
                }
                if (at >= 0) { if (value == null) lines.RemoveAt(at); else lines[at] = key + ": " + value + cr; }
                else if (value != null) lines.Insert(end, key + ": " + value + cr);
                // an empty front matter block is just clutter
                if (lines.Count > 1 && lines[0].Trim() == "---" && lines[1].Trim() == "---") { lines.RemoveRange(0, 2); if (lines.Count > 0 && lines[0].Trim() == "") lines.RemoveAt(0); }
            }
            return string.Join("\n", lines);
            });
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Kinetik Notes", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
    }

    // Opens a note in Obsidian when it's inside a vault, otherwise in the default Markdown editor.
    public static void Open(string path, int how)
    {
        try
        {
            if (how == 2) { Process.Start("explorer.exe", "/select,\"" + path + "\""); return; }
            var dir = Path.GetDirectoryName(path);
            while (how == 0 && dir != null && !Directory.Exists(Path.Combine(dir, ".obsidian"))) dir = Path.GetDirectoryName(dir);
            if (how == 0 && dir != null)
            {
                var rel = path.Substring(dir.Length).TrimStart('\\').Replace('\\', '/');
                if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(0, rel.Length - 3);
                Process.Start("obsidian://open?vault=" + Uri.EscapeDataString(Path.GetFileName(dir)) + "&file=" + Uri.EscapeDataString(rel));
            }
            else Process.Start(path);
        }
        catch { try { Process.Start(path); } catch { } }
    }

    // ---- templates: Markdown files in a "Templates" folder inside the notes folder, so they can be edited in Obsidian
    public const string TemplatesFolderName = "Templates";
    public static string TemplatesDir(string folder) { return Path.Combine(folder, TemplatesFolderName); }

    // The starter set, written the first time the folder is needed (or when restored from Settings).
    static readonly string[][] defaultTemplates =
    {
        new[] { "To-do list", "---\ncolor: yellow\n---\n# {title}\n\n- [ ] \n- [ ] \n- [ ] \n" },
        new[] { "Daily plan", "---\ncolor: orange\ndue: {date}\n---\n# {title}\n\n## Morning\n- [ ] \n\n## Afternoon\n- [ ] \n\n## Evening\n- [ ] \n\n## Notes\n" },
        new[] { "Weekly plan", "---\ncolor: blue\ndue: {friday}\n---\n# {title}\n\nWeek of {weekstart}\n\n## Must do\n- [ ] \n- [ ] \n\n## Should do\n- [ ] \n\n## Could do\n- [ ] \n" },
        new[] { "Weekly unit", "---\ncolor: blue\ndue: {friday}\ntags: [teaching]\n---\n# {title}\n\nWeek of {weekstart}\n\n## Prep\n- [ ] Update lesson slides\n- [ ] Prepare practical files\n\n## Class\n- [ ] Run practical\n- [ ] Check student progress\n\n## Admin\n- [ ] Mark submitted work\n- [ ] Record attendance\n- [ ] Reply to student questions\n" },
        new[] { "Lesson plan", "---\ncolor: green\ndue: {date}\ntags: [teaching]\n---\n# {title}\n\n## Learning objectives\n- \n- \n\n## Activities\n- [ ] Intro / recap (10 min)\n- [ ] Demo (20 min)\n- [ ] Practical (60 min)\n- [ ] Wrap-up and questions (10 min)\n\n## Resources\n- \n" },
        new[] { "Meeting notes", "---\ncolor: purple\n---\n# {title}\n\n{weekday} {date}, {time}\n\n## Attendees\n- \n\n## Agenda\n- \n\n## Actions\n- [ ] \n" },
        new[] { "Shopping list", "---\ncolor: green\n---\n# {title}\n\n- [ ] \n- [ ] \n- [ ] \n" },
        new[] { "Project", "---\ncolor: pink\ndue: {nextweek}\n---\n# {title}\n\n## Goal\n\n\n## Milestones\n- [ ] \n- [ ] \n- [ ] \n\n## Next steps\n- [ ] \n" },
        new[] { "Reminder", "---\ncolor: pink\ndue: {tomorrow}\npinned: true\n---\n# {title}\n\n- [ ] \n" },
    };

    // Writes any missing starter templates; with overwrite, puts every one back to how it shipped.
    public static void EnsureTemplates(string folder, bool overwrite)
    {
        var dir = TemplatesDir(folder);
        bool fresh = !Directory.Exists(dir);
        if (!fresh && !overwrite) return; // the user's folder: leave their edits and deletions alone
        Directory.CreateDirectory(dir);
        foreach (var t in defaultTemplates)
        {
            var p = Path.Combine(dir, t[0] + ".md");
            if (overwrite || !File.Exists(p)) File.WriteAllText(p, t[1], new UTF8Encoding(false));
        }
    }

    public static List<string> Templates(string folder)
    {
        var dir = TemplatesDir(folder);
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.GetFiles(dir, "*.md").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Fills in a template's placeholders: {title}, {date}, {time}, {weekday}, {tomorrow}, {weekstart} (Monday),
    // {friday} (this week's), {nextweek}. Dates are yyyy-MM-dd so Obsidian and the due badge both read them.
    public static string Fill(string text, string title)
    {
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        Func<DateTime, string> d = x => x.ToString("yyyy-MM-dd");
        return text.Replace("\r\n", "\n")
            .Replace("{title}", title).Replace("{date}", d(today)).Replace("{time}", DateTime.Now.ToString("HH:mm"))
            .Replace("{weekday}", today.ToString("dddd")).Replace("{tomorrow}", d(today.AddDays(1)))
            .Replace("{weekstart}", d(monday)).Replace("{friday}", d(monday.AddDays(4))).Replace("{nextweek}", d(today.AddDays(7)));
    }

    // Makes a note from a template file (null = the blank-note template in Settings), named after its title.
    public static string NewNote(string folder, string templatePath, string blank, string title)
    {
        var name = new string(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray()).Trim().TrimEnd('.');
        if (name == "") name = "New note";
        int n = 1;
        string path;
        while (File.Exists(path = Path.Combine(folder, name + (n > 1 ? " " + n : "") + ".md"))) n++;
        var text = templatePath != null ? File.ReadAllText(templatePath) : (blank ?? Settings.DefaultTemplate);
        // the blank template predates {title}; give it the title if it still starts with the stock heading
        if (templatePath == null && text.StartsWith("# New note")) text = "# {title}" + text.Substring("# New note".Length);
        text = Fill(text, title);
        File.WriteAllText(path, text.EndsWith("\n") ? text : text + "\n", new UTF8Encoding(false));
        return path;
    }

    public static void Recycle(string path)
    {
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}

// ======================================================================= The widget
class NotesForm : Form
{
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    // One laid-out note: where it sits, and where each of its lines goes (relative to the card).
    class Card
    {
        public Note N;
        public RectangleF R;
        public float BandH, HeadH; // HeadH: the title band's first row, where the badges and chevron sit; BandH grows when the title wraps
        public bool Collapsed;
        public string More;
        public float MoreY, TagsY;
        // set when the note is longer than the note length: its body scrolls inside the card by Scroll pixels
        public bool Scrolls;
        public float FullH;
        public int Scroll;
        public int ScrollMax { get { return Math.Max(0, (int)Math.Ceiling(FullH - R.Height)); } }
        public readonly List<KeyValuePair<NoteLine, RectangleF>> Lines = new List<KeyValuePair<NoteLine, RectangleF>>();
    }

    static readonly Color KeyColour = Color.FromArgb(1, 1, 2); // see-through between the notes
    readonly Settings c = Settings.Load();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
    readonly System.Windows.Forms.Timer reload = new System.Windows.Forms.Timer { Interval = 400 };
    readonly System.Windows.Forms.Timer midnight = new System.Windows.Forms.Timer { Interval = 60000 };
    List<Card> cards = new List<Card>();
    FileSystemWatcher watcher;
    bool loadingNotes, loadAgain;
    List<Note> all = new List<Note>(), notes = new List<Note>();
    string message, watchedFolder, watchedMode;
    Font fTitle, fText, fHead, fSmall, fDone;
    int layer, scroll, contentH, cardsW, sbW, sbGrab, cardGrab;
    bool sbDrag, sbHot;
    Card cardDrag, cardHot; // the note whose own scroll bar is being dragged / hovered
    readonly Dictionary<string, int> noteScroll = new Dictionary<string, int>(); // each note's scroll position, kept across reloads
    float u = 1;
    bool down, dragging, hidden;
    Point downAt, grab;
    Note menuNote;
    DateTime today = DateTime.Today;
    SettingsForm settings;

    public NotesForm(string openSettings)
    {
        layer = c.Layer;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Kinetik Notes";
        BackColor = TransparencyKey = KeyColour;
        DoubleBuffered = true;
        Opacity = c.Opacity / 100.0;
        menu.Opening += (s, e) => BuildMenu();
        try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
        tray.Text = Program.Demo ? "Kinetik Notes (demo)" : "Kinetik Notes";
        Backup.BeforeExit = () => tray.Visible = false;
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.MouseDown += (s, e) => menuNote = null;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SetHidden(!hidden); };
        reload.Tick += (s, e) => { reload.Stop(); LoadNotes(); };
        midnight.Tick += (s, e) => { today = DateTime.Today; LoadNotes(); }; // due badges change at midnight
        midnight.Start();
        Restyle();
        Load += (s, e) => LoadNotes();
        if (c.Folder == "" || openSettings != null) Load += (s, e) => BeginInvoke((Action)(() => OpenSettings(openSettings ?? "General")));
    }

    public void Shutdown()
    { if (settings != null && !settings.IsDisposed) settings.Close(); c.Save(); Close(); }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000; // TOOLWINDOW | NOACTIVATE
            if (layer == 2) cp.ExStyle |= 0x8; // TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE: clicking it never steals focus
        if (m.Msg == 0x312 && (int)m.WParam == 1) { SetHidden(!hidden); return; } // WM_HOTKEY
        if (m.Msg == 0x46 && layer == 0) // WM_WINDOWPOSCHANGING: "on the desktop" keeps it beneath other windows
        {
            var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
            if ((wp.flags & 0x4) == 0) { wp.hwndInsertAfter = new IntPtr(1); Marshal.StructureToPtr(wp, m.LParam, false); }
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SyncHotkey(); }
    protected override void OnHandleDestroyed(EventArgs e) { UnregisterHotKey(Handle, 1); base.OnHandleDestroyed(e); }

    void SyncHotkey()
    {
        if (!IsHandleCreated) return;
        UnregisterHotKey(Handle, 1);
        if (c.Hotkey) RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.N); // Ctrl+Alt+N, no auto-repeat
    }

    void SetHidden(bool h)
    {
        hidden = h;
        if (h) Hide(); else { Show(); Relayout(); }
    }

    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(value && !hidden); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (watcher != null) watcher.Dispose();
            tray.Dispose(); reload.Dispose(); midnight.Dispose(); menu.Dispose(); DisposeFonts();
        }
        base.Dispose(disposing);
    }

    void DisposeFonts()
    {
        foreach (var f in new[] { fTitle, fText, fHead, fSmall, fDone }) if (f != null) f.Dispose();
    }

    void Restyle()
    {
        u = DeviceDpi / 96f * c.Scale / 100f;
        DisposeFonts();
        Emoji.Clear();
        var fam = c.FontName != "" && Draw.Families.Contains(c.FontName) ? c.FontName : Theme.IsTerm(c.ThemeName) ? Theme.Mono : Draw.DefaultFamily;
        fTitle = new Font(fam, 14f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        fHead = new Font(fam, 12.5f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        fText = new Font(fam, 12.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        fDone = new Font(fam, 12.5f * u, FontStyle.Strikeout, GraphicsUnit.Pixel);
        fSmall = new Font(fam, 11f * u, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    // Called by the settings window after any change.
    public void ApplySettings()
    {
        c.Sanitize();
        c.Save();
        if (c.Layer != layer) { layer = c.Layer; RecreateHandle(); }
        Opacity = c.Opacity / 100.0;
        SyncHotkey();
        Restyle();
        LoadNotes();
    }

    void Changed() { ApplySettings(); if (settings != null && !settings.IsDisposed) settings.RefreshPage(); }

    public List<Note> AllNotes { get { return all; } }
    public Settings Cfg { get { return c; } }

    public void OpenSettings(string page)
    {
        if (settings == null || settings.IsDisposed) { settings = new SettingsForm(this, page); settings.Show(); }
        else { settings.ShowPage(page ?? settings.Page); settings.Activate(); }
    }

    // ---- the menus: the note's own options when a note was right-clicked, then everything else
    ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    void BuildMenu()
    {
        // A cleared submenu that was shown keeps its window otherwise, and the colour swatches are bitmaps.
        var old = menu.Items.Cast<ToolStripItem>().ToList();
        menu.Items.Clear();
        foreach (var it in old)
        {
            var mi = it as ToolStripMenuItem;
            if (mi != null) foreach (ToolStripItem sub in mi.DropDownItems) if (sub.Image != null) sub.Image.Dispose();
            it.Dispose();
        }
        bool ok = Directory.Exists(c.Folder);
        var n = menuNote;
        if (n != null)
        {
            menu.Items.Add(new ToolStripMenuItem(n.Title) { Enabled = false });
            menu.Items.Add(Item("Open", false, () => Notes.Open(n.Path, c.DoubleClick == 2 ? 0 : c.DoubleClick)));
            menu.Items.Add(Item("Show in File Explorer", false, () => Notes.Open(n.Path, 2)));
            var colour = new ToolStripMenuItem("Colour");
            for (int i = 0; i < Notes.ColourNames.Length; i++)
            {
                int ci = i;
                var it = Item(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Notes.ColourNames[i]), n.Colour == i, () => Notes.SetMeta(n.Path, n.Revision, "color", Notes.ColourNames[ci]));
                it.Image = SwatchImage(Notes.Swatch(i));
                colour.DropDownItems.Add(it);
            }
            colour.DropDownItems.Add(new ToolStripSeparator());
            colour.DropDownItems.Add(Item("Default", n.Colour < 0, () => Notes.SetMeta(n.Path, n.Revision, "color", null)));
            menu.Items.Add(colour);
            menu.Items.Add(Item("Pinned", n.Pinned, () => Notes.SetMeta(n.Path, n.Revision, "pinned", n.Pinned ? null : "true")));
            bool rolled = Settings.InList(c.Collapsed, n.Key);
            menu.Items.Add(Item("Rolled up", rolled, () => { c.Collapsed = Settings.WithKey(c.Collapsed, n.Key, !rolled); Changed(); }));
            var due = new ToolStripMenuItem("Due date");
            Action<DateTime?> setDue = d => Notes.SetMeta(n.Path, n.Revision, "due", d == null ? null : d.Value.ToString("yyyy-MM-dd"));
            due.DropDownItems.Add(Item("Today", n.Due == today, () => setDue(today)));
            due.DropDownItems.Add(Item("Tomorrow", n.Due == today.AddDays(1), () => setDue(today.AddDays(1))));
            due.DropDownItems.Add(Item("In a week", n.Due == today.AddDays(7), () => setDue(today.AddDays(7))));
            due.DropDownItems.Add(Item("Pick a date…", false, () => { var d = PickDate(n.Due ?? today); if (d != null) setDue(d); }));
            due.DropDownItems.Add(new ToolStripSeparator());
            due.DropDownItems.Add(Item("No due date", n.Due == null, () => setDue(null)));
            menu.Items.Add(due);
            menu.Items.Add(Item("Hide this note", false, () => { c.Hidden = Settings.WithKey(c.Hidden, n.Key, true); Changed(); }));
            menu.Items.Add(Item("Delete…", false, () =>
            {
                if (MessageBox.Show("Move \"" + n.Title + "\" (" + Path.GetFileName(n.Path) + ") to the Recycle Bin?", "Kinetik Notes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try { Notes.Recycle(n.Path); } catch (Exception ex) { MessageBox.Show(ex.Message, "Kinetik Notes"); }
            }));
            menu.Items.Add(new ToolStripSeparator());
        }
        var newNote = new ToolStripMenuItem("New note") { Enabled = ok };
        if (ok)
        {
            try { Notes.EnsureTemplates(c.Folder, false); } catch { }
            newNote.DropDownItems.Add(Item("Blank note", false, () => CreateNote(null, "New note")));
            var templates = Notes.Templates(c.Folder);
            if (templates.Count > 0) newNote.DropDownItems.Add(new ToolStripSeparator());
            foreach (var tp in templates)
            {
                var path = tp;
                var name = Path.GetFileNameWithoutExtension(path);
                newNote.DropDownItems.Add(Item(name, false, () => CreateNote(path, name)));
            }
            newNote.DropDownItems.Add(new ToolStripSeparator());
            newNote.DropDownItems.Add(Item("Edit templates…", false, () => { try { Process.Start("explorer.exe", "\"" + Notes.TemplatesDir(c.Folder) + "\""); } catch { } }));
        }
        menu.Items.Add(newNote);
        menu.Items.Add(Item(hidden ? "Show notes" : "Hide notes", false, () => SetHidden(!hidden)));
        if (c.Hotkey) ((ToolStripMenuItem)menu.Items[menu.Items.Count - 1]).ShortcutKeyDisplayString = "Ctrl+Alt+N";
        menu.Items.Add(Item("Open notes folder", false, () => { if (Directory.Exists(c.Folder)) try { Process.Start("explorer.exe", "\"" + c.Folder + "\""); } catch { } }));
        menu.Items[menu.Items.Count - 1].Enabled = ok;
        menu.Items.Add(Item("Refresh", false, LoadNotes));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Hide finished tasks", c.HideDone, () => { c.HideDone = !c.HideDone; Changed(); }));
        if (c.Hidden != "") menu.Items.Add(Item("Show hidden notes", false, () => { c.Hidden = ""; Changed(); }));
        menu.Items.Add(new ToolStripSeparator());
        Common().AddAll(menu.Items);
    }

    // The look, placement, startup and backup options, shared word for word with the other Kinetik apps.
    CommonMenu Common()
    {
        return new CommonMenu
        {
            Title = "Kinetik Notes", Version = Program.Version,
            OpenTheme = () => OpenSettings("Appearance"),
            Scale = () => c.Scale, SetScale = v => { c.Scale = v; Changed(); },
            Widths = new[] { 220, 260, 320 }, CardWidth = () => c.Width, SetCardWidth = v => { c.Width = v; Changed(); },
            FontName = () => c.FontName, SetFontName = v => { c.FontName = v; Changed(); },
            Opacity = () => c.Opacity, SetOpacity = v => { c.Opacity = v; Changed(); },
            Layer = () => c.Layer, SetLayer = v => { c.Layer = v; Changed(); },
            Locked = () => c.Locked, SetLocked = v => { c.Locked = v; Changed(); },
            Snap = () => c.Snap, SetSnap = v => { c.Snap = v; Changed(); },
            OpenSettings = () => OpenSettings(null),
            RunAtStartup = () => Startup.IsOn, SetRunAtStartup = Startup.Set,
            Restore = Backup.RestoreNow, LastBackup = () => Backup.SavedText,
            BackUp = () => { ApplySettings(); Backup.SaveNow(); },
            Exit = () => { tray.Visible = false; Application.Exit(); },
        };
    }

    // Asks for a title (unless that's switched off), then makes the note from the template and opens it.
    void CreateNote(string templatePath, string templateName)
    {
        var suggested = templatePath == null ? "New note" : templateName + " " + DateTime.Today.ToString("d MMM");
        var title = c.AskTitle ? AskTitle(templateName, suggested) : suggested;
        if (title == null) return;
        try
        {
            var p = Notes.NewNote(c.Folder, templatePath, c.Template, title.Trim() == "" ? suggested : title.Trim());
            if (c.OpenNew) Notes.Open(p, c.DoubleClick == 2 ? 0 : c.DoubleClick);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Kinetik Notes"); }
    }

    static string AskTitle(string template, string suggested)
    {
        var bg = Color.FromArgb(27, 31, 44);
        using (var f = new Form { Text = "New note — " + template, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, BackColor = bg, ForeColor = Color.FromArgb(232, 236, 244), Font = new Font("Segoe UI", 9.5f), TopMost = true, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96f, 96f) })
        {
            f.ClientSize = new Size(360, 112);
            var lb = new Label { Text = "Title (also the file name)", Left = 12, Top = 12, AutoSize = true };
            var tb = new TextBox { Text = suggested, Left = 12, Top = 36, Width = 336, BackColor = Color.FromArgb(44, 51, 70), ForeColor = f.ForeColor, BorderStyle = BorderStyle.FixedSingle };
            var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, Left = 192, Top = 74, Width = 75, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 74, Width = 75, FlatStyle = FlatStyle.Flat };
            f.Controls.AddRange(new Control[] { lb, tb, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Shown += (s, e) => { f.Activate(); tb.Focus(); tb.SelectAll(); };
            return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }
    }

    static Image SwatchImage(Color col)
    {
        var b = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(b)) { g.SmoothingMode = SmoothingMode.AntiAlias; Draw.Fill(g, new RectangleF(1, 1, 14, 14), 3, col); }
        return b;
    }

    static DateTime? PickDate(DateTime start)
    {
        using (var f = new Form { Text = "Due date", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(27, 31, 44), TopMost = true, Padding = new Padding(10) })
        {
            var cal = new MonthCalendar { MaxSelectionCount = 1, SelectionStart = start, Location = new Point(10, 10) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, AutoSize = true };
            f.Controls.Add(cal);
            ok.Location = new Point(cal.Right - ok.PreferredSize.Width, cal.Bottom + 8);
            f.Controls.Add(ok);
            f.AcceptButton = ok;
            return f.ShowDialog() == DialogResult.OK ? (DateTime?)cal.SelectionStart.Date : null;
        }
    }

    // ---- loading the notes and watching the folder
    void Watch()
    {
        var mode = c.Folder + "|" + c.Subfolders;
        if (watchedMode == mode && watcher != null) return;
        if (watcher != null) { watcher.Dispose(); watcher = null; }
        watchedFolder = c.Folder; watchedMode = mode;
        if (!Directory.Exists(c.Folder)) return;
        try
        {
            watcher = new FileSystemWatcher(c.Folder, "*.md") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = c.Subfolders };
            FileSystemEventHandler h = (s, e) => { Notes.Invalidate(e.FullPath); try { BeginInvoke((Action)(() => { reload.Stop(); reload.Start(); })); } catch { } };
            watcher.Changed += h; watcher.Created += h; watcher.Deleted += h;
            watcher.Renamed += (s, e) => { Notes.Invalidate(e.OldFullPath); h(s, e); };
            watcher.Error += (s, e) => { Notes.Invalidate(null); try { BeginInvoke((Action)(() => { watchedMode = null; reload.Stop(); reload.Start(); })); } catch { } };
            watcher.EnableRaisingEvents = true;
        }
        catch { watcher = null; }
    }

    void LoadNotes()
    {
        Watch();
        if (loadingNotes) { loadAgain = true; return; }
        loadingNotes = true;
        var folder = c.Folder; bool sub = c.Subfolders;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string error = null; var loaded = new List<Note>();
            if (string.IsNullOrWhiteSpace(folder)) error = "Right-click here → Settings… to pick a folder of Markdown notes.";
            else if (!Directory.Exists(folder)) error = "Can't find the notes folder:\n" + folder;
            else try { loaded = Notes.Load(folder, sub); } catch (Exception ex) { error = ex.Message; }
            try { BeginInvoke((Action)(() =>
            {
                loadingNotes = false;
                if (IsDisposed) return;
                if (!loadAgain && folder == c.Folder && sub == c.Subfolders) ApplyNotes(loaded, error);
                else { loadAgain = false; LoadNotes(); }
            })); } catch { }
        });
    }

    void ApplyNotes(List<Note> loaded, string error)
    {
        all = loaded; message = error;
        var tags = (c.TagFilter ?? "").Split(',', ' ').Select(t => t.Trim().TrimStart('#')).Where(t => t != "").ToList();
        IEnumerable<Note> q = all.Where(n => !Settings.InList(c.Hidden, n.Key));
        if (c.HideFinishedNotes) q = q.Where(n => n.Tasks == 0 || n.DoneTasks < n.Tasks);
        if (tags.Count > 0) q = q.Where(n => tags.Any(t => n.Tags.Contains(t)));
        IOrderedEnumerable<Note> o = q.OrderBy(n => c.PinnedFirst && n.Pinned ? 0 : 1);
        if (c.Sort == 1) o = o.ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase);
        else if (c.Sort == 2) o = o.ThenByDescending(n => n.Modified);
        else if (c.Sort == 3) o = o.ThenBy(n => n.Due ?? DateTime.MaxValue);
        else if (c.Sort == 4) o = o.ThenByDescending(n => n.Tasks - n.DoneTasks);
        notes = o.ThenBy(n => n.Key).ToList();
        if (message == null && notes.Count == 0) message = all.Count == 0 ? "No notes yet. Right-click → New note." : "Every note is hidden or filtered out. Right-click → Settings… → Notes.";
        Relayout();
        if (settings != null && !settings.IsDisposed) settings.NotesChanged();
    }

    IEnumerable<NoteLine> LinesToShow(Note n)
    {
        return c.HideDone ? n.Lines.Where(l => !(l.Kind == 3 && l.Done)) : n.Lines;
    }

    int ColourOf(Note n)
    {
        if (n.Colour >= 0) return n.Colour;
        return c.DefaultColour >= 0 ? c.DefaultColour : -2 - n.Colour;
    }

    Card Measure(Graphics g, Note n, float cw)
    {
        float pad = 12 * u;
        var cd = new Card { N = n, Collapsed = Settings.InList(c.Collapsed, n.Key) };
        cd.HeadH = fTitle.GetHeight(g) + pad * 1.4f;
        cd.BandH = cd.HeadH + Math.Max(0, TextHeight(g, n.Title, fTitle, TitleWidth(g, n, cw)) - fTitle.GetHeight(g));
        if (cd.Collapsed) { cd.R = new RectangleF(0, 0, cw, cd.BandH); return cd; }
        float full = LayLines(g, cd, n, cw, 0);
        float limit = c.MaxHeight > 0 ? Math.Max(cd.BandH + 48 * u, c.MaxHeight * DeviceDpi / 96f) : float.MaxValue;
        if (full > limit)
        {
            // too long: lay it out again a little narrower to leave room for its own scroll bar, and cut it to the note length
            full = LayLines(g, cd, n, cw, 12 * u);
            cd.Scrolls = true;
            cd.FullH = full;
            cd.R = new RectangleF(0, 0, cw, (float)Math.Floor(limit));
            int saved;
            cd.Scroll = noteScroll.TryGetValue(n.Key, out saved) ? Math.Max(0, Math.Min(cd.ScrollMax, saved)) : 0;
        }
        else cd.R = new RectangleF(0, 0, cw, full);
        return cd;
    }

    static string TagLine(Note n) { return string.Join("  ", n.Tags.OrderBy(t => t).Select(t => "#" + t)); }

    string DueText(Note n)
    {
        int days = (int)(n.Due.Value - today).TotalDays;
        return days < 0 ? "Overdue" : days == 0 ? "Today" : days == 1 ? "Tomorrow" : days < 7 ? n.Due.Value.ToString("ddd") : n.Due.Value.ToString("d MMM");
    }

    // How wide the title can be: the band minus the pin dot, the chevron, the task count and the due pill.
    float TitleWidth(Graphics g, Note n, float cw)
    {
        float pad = 12 * u, right = cw - pad - 18 * u;
        if (c.ShowCount && n.Tasks > 0) right -= g.MeasureString(n.DoneTasks + "/" + n.Tasks, fSmall).Width + 4 * u;
        if (c.ShowDue && n.Due != null) right -= g.MeasureString(DueText(n), fSmall).Width + 10 * u;
        return Math.Max(10, right - pad - (n.Pinned ? 7 * u : 0));
    }

    // Lays out a note's lines (positions relative to the card) and returns the card's natural height.
    float LayLines(Graphics g, Card cd, Note n, float cw, float gutter)
    {
        float pad = 12 * u;
        cd.Lines.Clear(); cd.More = null;
        float ly = cd.BandH + 8 * u;
        var shown = LinesToShow(n).ToList();
        int limit = c.MaxLines > 0 ? c.MaxLines : int.MaxValue, count = 0, left = 0;
        foreach (var l in shown)
        {
            if (l.Kind == 4) { if (count < limit) ly += 5 * u; continue; }
            if (count >= limit) { left++; continue; }
            count++;
            float lx = pad + l.Indent * 14 * u + (l.Kind == 2 || l.Kind == 3 ? 20 * u : 0);
            var f = l.Kind == 1 ? fHead : l.Kind == 3 && l.Done ? fDone : fText;
            float tw = cw - lx - pad - gutter;
            float th = Math.Max(f.GetHeight(g), TextHeight(g, l.Text == "" ? " " : l.Text, f, Math.Max(10, tw)));
            if (l.Kind == 1) ly += 3 * u;
            cd.Lines.Add(new KeyValuePair<NoteLine, RectangleF>(l, new RectangleF(lx, ly, tw, th)));
            ly += th + 3 * u;
        }
        if (left > 0) { cd.More = "+" + left + " more…"; cd.MoreY = ly; ly += fSmall.GetHeight(g) + 3 * u; }
        if (c.ShowTags && n.Tags.Count > 0) { cd.TagsY = ly + 2 * u; ly += TextHeight(g, TagLine(n), fSmall, cw - pad * 2 - gutter) + 5 * u; }
        return ly + pad - 3 * u;
    }

    // Lays the notes out in columns (each note goes in the shortest one), then sizes and places the window to fit
    // them, up to the screen's height; beyond that it scrolls.
    void Relayout()
    {
        float colW = c.Width * u, gap = c.Gap * u;
        int cols = c.Columns;
        // Fit the screen's width: narrow the notes so the whole widget fits, and drop columns if that would make
        // them too thin to read. Nothing is saved, so on a wider screen the notes go back to the chosen size.
        if (c.X == int.MinValue || !Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(c.X, c.Y, (int)colW, 40))))
        {
            var wa0 = Screen.PrimaryScreen.WorkingArea;
            c.X = wa0.Left + (int)(12 * u); c.Y = wa0.Top + (int)(12 * u);
        }
        var scr = Screen.FromPoint(new Point(c.X + 10, c.Y + 10)).WorkingArea;
        float edge = 8 * u, avail = scr.Width - edge * 2 - 14 * u; // leave room for a scroll bar
        while (cols > 1 && (avail - gap * (cols - 1)) / cols < 150 * u) cols--;
        if (colW * cols + gap * (cols - 1) > avail) colW = Math.Max(120 * u, (avail - gap * (cols - 1)) / cols);
        int w = (int)Math.Ceiling(colW * cols + gap * (cols - 1));
        var heights = new float[cols];
        var list = new List<Card>();
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.TextContrast = 2; // cards are solid, so ClearType has real pixels to blend with
            if (message != null)
            {
                float pad = 12 * u;
                float mh = TextHeight(g, message, fText, w - pad * 2) + pad * 2;
                list.Add(new Card { R = new RectangleF(0, 0, w, mh) });
                heights[0] = mh + gap;
            }
            else
                foreach (var n in notes)
                {
                    int col = 0;
                    for (int i = 1; i < cols; i++) if (heights[i] < heights[col] - 0.5f) col = i;
                    var cd = Measure(g, n, colW);
                    cd.R.X = col * (colW + gap); cd.R.Y = heights[col];
                    heights[col] += cd.R.Height + gap;
                    list.Add(cd);
                }
        }
        cards = list;
        contentH = (int)Math.Ceiling(Math.Max(0, heights.Max() - gap));
        var wa = scr;
        int limit = wa.Bottom - c.Y - (int)(8 * u);
        int h = Math.Max(40, Math.Min(contentH, limit));
        // when the notes are taller than the screen, a scroll bar gets its own strip to the right of them
        cardsW = w;
        sbW = contentH > h ? (int)Math.Ceiling(14 * u) : 0;
        // and if the widget hangs off the left or right edge, bring it back onto the screen
        if (c.X + w + sbW > wa.Right - (int)edge) c.X = wa.Right - (int)edge - w - sbW;
        if (c.X < wa.Left + (int)edge) c.X = wa.Left + (int)edge;
        scroll = Math.Max(0, Math.Min(scroll, contentH - h));
        Bounds = new Rectangle(c.X, c.Y, w + sbW, h);
        Invalidate();
    }

    static readonly StringFormat Wrap = new StringFormat { Trimming = StringTrimming.Word };
    static readonly StringFormat OneLine = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };

    // Plain text goes through GDI+; text with emoji goes through WPF so the emoji come out in colour (see Emoji).
    static float TextHeight(Graphics g, string s, Font f, float w)
    {
        return Emoji.Has(s) ? Emoji.Height(s, f, w) : g.MeasureString(s, f, (int)w, Wrap).Height;
    }

    static void DrawText(Graphics g, string s, Font f, Color col, RectangleF r, bool oneLine)
    {
        if (Emoji.Has(s)) Emoji.Draw(g, s, f, col, r, oneLine);
        else using (var b = new SolidBrush(col)) g.DrawString(s, f, b, r, oneLine ? OneLine : Wrap);
    }

    RectangleF Shift(RectangleF r, Card cd) { return new RectangleF(r.X + cd.R.X, r.Y + cd.R.Y - scroll - cd.Scroll, r.Width, r.Height); }
    // the part of a scrolling note that its lines show through (below the title band)
    RectangleF BodyRect(Card cd) { var r = CardRect(cd); return RectangleF.FromLTRB(r.X, r.Y + cd.BandH, r.Right, r.Bottom - 4 * u); }
    RectangleF CardRect(Card cd) { return new RectangleF(cd.R.X, cd.R.Y - scroll, cd.R.Width, cd.R.Height); }
    RectangleF ChevronRect(Card cd) { float s = 18 * u, pad = 12 * u; var r = CardRect(cd); return new RectangleF(r.Right - pad - s + 4 * u, r.Y + (cd.HeadH - s) / 2, s, s); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(KeyColour);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.TextContrast = 2; // cards are solid, so ClearType has real pixels to blend with
        float pad = 12 * u, rad = c.Round * u;
        var theme = c.ThemeName == "Auto" ? Theme.Get("Auto").Name : c.ThemeName; // read Windows' mode once per paint
        PaintScrollBar(g);
        foreach (var cd in cards)
        {
            var card = CardRect(cd);
            if (card.Bottom < 0 || card.Top > ClientSize.Height) continue;
            if (cd.N == null) // the "no notes" message
            {
                Color mc, mb, mi, ms, ma; Notes.Look(theme, 0, out mc, out mb, out mi, out ms, out ma);
                Draw.Fill(g, card, rad, mc);
                DrawText(g, message, fText, mi, new RectangleF(card.X + pad, card.Y + pad, card.Width - pad * 2, card.Height), false);
                continue;
            }
            var n = cd.N;
            int colour = ColourOf(n);
            Color fill, band, ink, soft, accent;
            Notes.Look(theme, colour, out fill, out band, out ink, out soft, out accent);
            Draw.Fill(g, card, rad, fill);

            // the title band, like the glue strip on a paper note, with a thin progress bar along its bottom
            using (var path = Draw.Round(card, rad)) g.SetClip(path);
            using (var b = new SolidBrush(band)) g.FillRectangle(b, card.X, card.Y, card.Width, cd.BandH);
            if (c.ShowProgress && n.Tasks > 0)
            {
                float frac = (float)n.DoneTasks / n.Tasks, ph = Math.Max(2f, 3 * u);
                using (var b = new SolidBrush(Color.FromArgb(40, ink))) g.FillRectangle(b, card.X, card.Y + cd.BandH - ph, card.Width, ph);
                using (var b = new SolidBrush(accent)) g.FillRectangle(b, card.X, card.Y + cd.BandH - ph, card.Width * frac, ph);
            }
            g.ResetClip();

            // right side of the band: roll-up chevron, then task count, then due date
            var chev = ChevronRect(cd);
            Draw.Chevron(g, chev, !cd.Collapsed, soft, Math.Max(1.2f, 1.6f * u));
            float right = chev.X - 4 * u, titleH = fTitle.GetHeight(g), smallH = fSmall.GetHeight(g), midY = card.Y + (cd.HeadH - smallH) / 2;
            if (c.ShowCount && n.Tasks > 0)
            {
                var t = n.DoneTasks + "/" + n.Tasks;
                float tw = g.MeasureString(t, fSmall).Width;
                right -= tw;
                using (var b = new SolidBrush(soft)) g.DrawString(t, fSmall, b, right, midY);
                right -= 4 * u;
            }
            if (c.ShowDue && n.Due != null)
            {
                int days = (int)(n.Due.Value - today).TotalDays;
                string t = DueText(n);
                bool done = n.Tasks > 0 && n.DoneTasks == n.Tasks;
                var pill = days < 0 && !done ? Color.FromArgb(215, 50, 50) : days == 0 && !done ? Color.FromArgb(230, 130, 20) : Color.FromArgb(50, ink);
                var tc = days <= 0 && !done ? Color.White : ink;
                float tw = g.MeasureString(t, fSmall).Width, ph = smallH + 2 * u;
                var pr = new RectangleF(right - tw - 6 * u, card.Y + (cd.HeadH - ph) / 2, tw + 8 * u, ph);
                Draw.Fill(g, pr, ph / 2, pill);
                using (var b = new SolidBrush(tc)) g.DrawString(t, fSmall, b, pr.X + 4 * u, pr.Y + 1 * u);
                right = pr.X - 4 * u;
            }
            if (n.Pinned)
                using (var b = new SolidBrush(soft)) { g.FillEllipse(b, card.X + pad - 2 * u, card.Y + cd.HeadH / 2 - 2.5f * u, 5 * u, 5 * u); }
            float tx = card.X + pad + (n.Pinned ? 7 * u : 0);
            DrawText(g, n.Title, fTitle, ink, new RectangleF(tx, card.Y + (cd.HeadH - titleH) / 2, Math.Max(10, right - tx), cd.BandH), false); // wraps onto more lines when it's long
            if (cd.Collapsed) continue;

            var body = BodyRect(cd);
            if (cd.Scrolls) g.SetClip(body); // a long note shows its lines through the body, scrolled
            foreach (var kv in cd.Lines)
            {
                var l = kv.Key; var r = Shift(kv.Value, cd);
                if (r.Bottom < 0 || r.Top > ClientSize.Height || (cd.Scrolls && (r.Bottom < body.Top || r.Top > body.Bottom))) continue;
                var col = l.Kind == 3 && l.Done ? soft : ink;
                var f = l.Kind == 1 ? fHead : l.Kind == 3 && l.Done ? fDone : fText;
                float lineH = f.GetHeight(g);
                if (l.Kind == 2)
                    using (var b = new SolidBrush(soft)) g.FillEllipse(b, r.X - 13 * u, r.Y + lineH / 2 - 2.5f * u, 5 * u, 5 * u);
                else if (l.Kind == 3)
                {
                    float bs = 13 * u;
                    var box = new RectangleF(r.X - 19 * u, r.Y + (lineH - bs) / 2, bs, bs);
                    var boxCol = accent;
                    if (l.Done)
                    {
                        Draw.Fill(g, box, 3 * u, boxCol);
                        using (var p = new Pen(fill, 1.8f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(p, new[] { new PointF(box.X + bs * 0.24f, box.Y + bs * 0.52f), new PointF(box.X + bs * 0.43f, box.Y + bs * 0.72f), new PointF(box.X + bs * 0.77f, box.Y + bs * 0.3f) });
                    }
                    else Draw.Stroke(g, box, 3 * u, boxCol, 1.4f * u);
                }
                DrawText(g, l.Text, f, col, r, false);
            }
            if (cd.More != null) using (var b = new SolidBrush(soft)) g.DrawString(cd.More, fSmall, b, card.X + pad, card.Y + cd.MoreY - cd.Scroll);
            if (c.ShowTags && n.Tags.Count > 0)
                DrawText(g, TagLine(n), fSmall, soft, new RectangleF(card.X + pad, card.Y + cd.TagsY - cd.Scroll, card.Width - pad * 2 - (cd.Scrolls ? 12 * u : 0), card.Height), false);
            if (cd.Scrolls)
            {
                g.ResetClip();
                // a soft fade where the text runs under the edges, then the note's own scroll bar
                float fh = 10 * u;
                if (cd.Scroll > 0) using (var lg = new LinearGradientBrush(new RectangleF(body.X, body.Y - 1, body.Width, fh + 1), fill, Color.FromArgb(0, fill), 90f)) g.FillRectangle(lg, body.X, body.Y, body.Width - 12 * u, fh);
                if (cd.Scroll < cd.ScrollMax) using (var lg = new LinearGradientBrush(new RectangleF(body.X, body.Bottom - fh - 1, body.Width, fh + 1), Color.FromArgb(0, fill), fill, 90f)) g.FillRectangle(lg, body.X, body.Bottom - fh, body.Width - 12 * u, fh);
                var tr = NoteTrack(cd);
                Draw.Fill(g, tr, tr.Width / 2, Color.FromArgb(40, ink));
                var th = NoteThumb(cd);
                Draw.Fill(g, th, th.Width / 2, cd == cardHot || cd == cardDrag ? Color.FromArgb(200, ink) : Color.FromArgb(110, ink));
            }
        }
    }

    // ---- the scroll bar: a slim rounded track and thumb in the theme's faded text colour, wider while hovered
    int ScrollMax { get { return Math.Max(0, contentH - ClientSize.Height); } }
    RectangleF Track() { float tw = (sbHot || sbDrag ? 9 : 6) * u; return new RectangleF(cardsW + sbW - tw - 2 * u, 3 * u, tw, ClientSize.Height - 6 * u); }
    RectangleF Thumb()
    {
        var tr = Track();
        float th = Math.Max(28 * u, tr.Height * ClientSize.Height / Math.Max(1, contentH));
        float y = tr.Y + (ScrollMax == 0 ? 0 : (tr.Height - th) * scroll / ScrollMax);
        return new RectangleF(tr.X, y, tr.Width, th);
    }
    bool InScrollBar(Point p) { return sbW > 0 && p.X >= cardsW; }
    void SetScroll(int v) { v = Math.Max(0, Math.Min(ScrollMax, v)); if (v != scroll) { scroll = v; Invalidate(); } }

    void PaintScrollBar(Graphics g)
    {
        if (sbW == 0) return;
        var theme = c.ThemeName == "Auto" ? Theme.Get("Auto").Name : c.ThemeName;
        Color card, band, ink, soft, accent;
        Notes.Look(theme, 0, out card, out band, out ink, out soft, out accent);
        // the track is see-through-ish; the thumb is solid so it shows on any wallpaper
        bool paper = Notes.IsPaper(theme);
        var tr = Track();
        Draw.Fill(g, tr, tr.Width / 2, paper ? Color.FromArgb(120, 225, 225, 232) : Color.FromArgb(150, card));
        var th = Thumb();
        var thumb = paper ? (sbHot || sbDrag ? Color.FromArgb(95, 95, 105) : Color.FromArgb(135, 135, 145)) : (sbHot || sbDrag ? accent : soft);
        Draw.Fill(g, th, th.Width / 2, thumb);
    }

    // ---- a long note's own scroll bar, along the right edge of its body
    RectangleF NoteTrack(Card cd)
    {
        var b = BodyRect(cd);
        float tw = (cd == cardHot || cd == cardDrag ? 7 : 4) * u;
        return new RectangleF(b.Right - tw - 4 * u, b.Top + 4 * u, tw, b.Height - 8 * u);
    }
    RectangleF NoteThumb(Card cd)
    {
        var tr = NoteTrack(cd);
        float th = Math.Max(20 * u, tr.Height * cd.R.Height / Math.Max(1, cd.FullH));
        float y = tr.Y + (cd.ScrollMax == 0 ? 0 : (tr.Height - th) * cd.Scroll / cd.ScrollMax);
        return new RectangleF(tr.X, y, tr.Width, th);
    }
    // the strip a long note's scroll bar answers to: wider than the bar so it's easy to grab
    bool InNoteBar(Card cd, Point p) { if (cd == null || !cd.Scrolls || cd.Collapsed) return false; var b = BodyRect(cd); return p.X >= b.Right - 14 * u && p.Y >= b.Top && p.Y <= b.Bottom; }
    bool SetNoteScroll(Card cd, int v)
    {
        v = Math.Max(0, Math.Min(cd.ScrollMax, v));
        if (v == cd.Scroll) return false;
        cd.Scroll = v; noteScroll[cd.N.Key] = v;
        Invalidate();
        return true;
    }

    // ---- hit testing
    Card CardAt(Point p) { foreach (var cd in cards) if (cd.N != null && CardRect(cd).Contains(p)) return cd; return null; }

    KeyValuePair<NoteLine, RectangleF>? TaskAt(Card cd, Point p)
    {
        if (cd == null || cd.Collapsed) return null;
        foreach (var kv in cd.Lines)
        {
            if (kv.Key.Kind != 3) continue;
            var r = Shift(kv.Value, cd);
            if (cd.Scrolls && !BodyRect(cd).Contains(p)) return null;
            if (RectangleF.FromLTRB(r.X - 23 * u, r.Y - 1.5f * u, r.Right, r.Bottom + 1.5f * u).Contains(p)) return kv; // the whole line, not just the little box
        }
        return null;
    }

    // ---- dragging, ticking tasks, rolling up, opening notes and scrolling
    protected override void OnMouseDown(MouseEventArgs e)
    {
        var nc = CardAt(e.Location);
        if (e.Button == MouseButtons.Left && InNoteBar(nc, e.Location))
        {
            var th = NoteThumb(nc);
            if (e.Y >= th.Top && e.Y <= th.Bottom) { cardDrag = nc; cardGrab = (int)(e.Y - th.Top); Invalidate(); }
            else SetNoteScroll(nc, nc.Scroll + (e.Y < th.Top ? -1 : 1) * (int)(BodyRect(nc).Height * 0.9f));
            base.OnMouseDown(e);
            return;
        }
        if (e.Button == MouseButtons.Left && InScrollBar(e.Location))
        {
            var th = Thumb();
            if (e.Y >= th.Top && e.Y <= th.Bottom) { sbDrag = true; sbGrab = (int)(e.Y - th.Top); Invalidate(); }
            else SetScroll(scroll + (e.Y < th.Top ? -1 : 1) * (int)(ClientSize.Height * 0.9f)); // page up / down
            base.OnMouseDown(e);
            return;
        }
        if (e.Button == MouseButtons.Left) { down = true; dragging = false; downAt = Cursor.Position; grab = new Point(Cursor.Position.X - c.X, Cursor.Position.Y - c.Y); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (cardDrag != null)
        {
            var tr = NoteTrack(cardDrag); var th = NoteThumb(cardDrag);
            float room = tr.Height - th.Height;
            if (room > 0) SetNoteScroll(cardDrag, (int)Math.Round((e.Y - cardGrab - tr.Y) / room * cardDrag.ScrollMax));
            base.OnMouseMove(e);
            return;
        }
        var over = CardAt(e.Location);
        var hotCard = InNoteBar(over, e.Location) ? over : null;
        if (hotCard != cardHot) { cardHot = hotCard; Invalidate(); }
        if (hotCard != null) { Cursor = Cursors.Default; base.OnMouseMove(e); return; }
        if (sbDrag)
        {
            var tr = Track(); var th = Thumb();
            float room = tr.Height - th.Height;
            if (room > 0) SetScroll((int)Math.Round((e.Y - sbGrab - tr.Y) / room * ScrollMax));
            base.OnMouseMove(e);
            return;
        }
        bool hot = InScrollBar(e.Location);
        if (hot != sbHot) { sbHot = hot; Invalidate(); }
        if (hot) { Cursor = Cursors.Default; base.OnMouseMove(e); return; }
        var cd = CardAt(e.Location);
        bool clickable = cd != null && (TaskAt(cd, e.Location) != null || ChevronRect(cd).Contains(e.Location));
        Cursor = clickable ? Cursors.Hand : c.Locked ? Cursors.Default : Cursors.SizeAll;
        if (down && !c.Locked && !dragging && (Math.Abs(Cursor.Position.X - downAt.X) > 4 || Math.Abs(Cursor.Position.Y - downAt.Y) > 4)) dragging = true;
        if (dragging)
        {
            var p = Snap(new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y));
            c.X = p.X; c.Y = p.Y;
            SetWindowPos(Handle, IntPtr.Zero, c.X, c.Y, 0, 0, 0x1 | 0x4 | 0x10); // NOSIZE | NOZORDER | NOACTIVATE
        }
        base.OnMouseMove(e);
    }

    // Pulls the notes flush to a screen edge (with a small gap) when they're dragged close to one.
    Point Snap(Point p)
    {
        if (!c.Snap) return p;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int gap = (int)(12 * u), reach = (int)(18 * u);
        if (Math.Abs(p.X - (wa.Left + gap)) < reach) p.X = wa.Left + gap;
        else if (Math.Abs(p.X + Width - (wa.Right - gap)) < reach) p.X = wa.Right - gap - Width;
        if (Math.Abs(p.Y - (wa.Top + gap)) < reach) p.Y = wa.Top + gap;
        return p;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (sbHot && !sbDrag) { sbHot = false; Invalidate(); }
        if (cardHot != null && cardDrag == null) { cardHot = null; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (cardDrag != null && e.Button == MouseButtons.Left) { cardDrag = null; Invalidate(); base.OnMouseUp(e); return; }
        if (sbDrag && e.Button == MouseButtons.Left) { sbDrag = false; sbHot = InScrollBar(e.Location); Invalidate(); base.OnMouseUp(e); return; }
        if (e.Button == MouseButtons.Left && down)
        {
            down = false;
            if (dragging) { dragging = false; c.Save(); Relayout(); }
            else
            {
                var cd = CardAt(e.Location);
                var task = TaskAt(cd, e.Location);
                if (cd != null && ChevronRect(cd).Contains(e.Location))
                {
                    c.Collapsed = Settings.WithKey(c.Collapsed, cd.N.Key, !cd.Collapsed);
                    c.Save(); Relayout();
                }
                else if (task != null)
                {
                    var l = task.Value.Key;
                    Notes.Toggle(cd.N.Path, l.Src, cd.N.Revision);
                    Notes.Invalidate(cd.N.Path); LoadNotes();
                }
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            var cd = CardAt(e.Location);
            menuNote = cd == null ? null : cd.N;
            menu.Show(Cursor.Position);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        var cd = CardAt(e.Location);
        if (e.Button == MouseButtons.Left && cd != null && TaskAt(cd, e.Location) == null && !ChevronRect(cd).Contains(e.Location) && !InNoteBar(cd, e.Location))
            Notes.Open(cd.N.Path, c.DoubleClick);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int step = e.Delta * (int)(48 * u) / 120;
        var cd = CardAt(e.Location);
        if (cd != null && cd.Scrolls && !cd.Collapsed && BodyRect(cd).Contains(e.Location) && SetNoteScroll(cd, cd.Scroll - step)) { base.OnMouseWheel(e); return; }
        SetScroll(scroll - step);
        base.OnMouseWheel(e);
    }
}

// ======================================================================= Settings window
// Changes apply straight away; there's no OK button. Built in the same dark style as the menus.
class SettingsForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    static Color Bg { get { return KinetikSuite.Look.Current.Bg; } }

    static Color NavBg { get { return KinetikSuite.Look.Current.Nav; } }

    static Color CardBg { get { return KinetikSuite.Look.Current.Card; } }

    static Color Field { get { return KinetikSuite.Look.Current.Field; } }

    static Color Text1 { get { return KinetikSuite.Look.Current.Text; } }

    static Color Sub { get { return KinetikSuite.Look.Current.Sub; } }

    static Color Accent { get { return KinetikSuite.Look.Current.Accent; } }
    static readonly string[] Pages = { "General", "Appearance", "Notes", "Placement", "About" };
    static readonly string[] Subtitles = { "Where your notes live, and how new ones start.", "How the notes look on your desktop.", "Which notes show, and in what order.", "Where the notes sit, and how they behave.", "Version, updates and licence." };

    readonly NotesForm owner;
    readonly Settings c;
    readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
    readonly List<Label> navItems = new List<Label>();
    readonly System.Windows.Forms.Timer applyTimer = new System.Windows.Forms.Timer { Interval = 150 };
    FlowLayoutPanel list;
    CheckedListBox noteList;
    public string Page = "General";
    float k = 1;

    int S(float px) { return (int)Math.Round(px * k); }
    int RowW { get { return S(470); } }

    public SettingsForm(NotesForm owner, string page)
    {
        this.owner = owner; c = owner.Cfg;
        k = DeviceDpi / 96f;
        Text = "Kinetik Notes settings";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(680), S(600));
        MinimumSize = new Size(S(560), S(400));
        BackColor = Bg; ForeColor = Text1;
        Font = new Font("Segoe UI", 9.5f);
        applyTimer.Tick += (s, e) => { applyTimer.Stop(); owner.ApplySettings(); };
        FormClosing += (s, e) => { if (applyTimer.Enabled) { applyTimer.Stop(); owner.ApplySettings(); } };
        FormClosed += (s, e) => applyTimer.Dispose();

        var nav = new Panel { Dock = DockStyle.Left, Width = S(170), BackColor = NavBg };
        var brand = new Label { Text = "Kinetik Notes", Font = new Font("Segoe UI", 12.5f, FontStyle.Bold), ForeColor = Text1, Dock = DockStyle.Top, Height = S(60), Padding = new Padding(S(18), 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        var navList = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(S(8), 0, S(8), 0), BackColor = NavBg };
        foreach (var p in Pages) navList.Controls.Add(NavItem(p));
        nav.Controls.Add(navList); nav.Controls.Add(brand);
        nav.Padding = new Padding(0, 0, 1, 0);
        nav.Paint += (s, e) => { using (var pen = new Pen(KinetikSuite.Look.Current.Border, 1)) e.Graphics.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height); };
        Controls.Add(content); Controls.Add(nav);
        ShowPage(page ?? "General");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        KinetikSuite.Look.Current.Window(Handle); // title bar in the Kinetik apps' theme
    }

    Label NavItem(string name)
    {
        var l = new Label { Text = name, Tag = name, AutoSize = false, Size = new Size(S(154), S(38)), Margin = new Padding(0, 0, 0, S(3)), Cursor = Cursors.Hand };
        l.Paint += (s, e) =>
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool sel = (string)l.Tag == Page;
            g.Clear(NavBg);
            if (sel) { Draw.Fill(g, new RectangleF(0, 0, l.Width - 1, l.Height - 1), S(6), CardBg); Draw.Fill(g, new RectangleF(0, S(10), S(3), l.Height - S(20)), S(1.5f), Accent); }
            using (var b = new SolidBrush(sel ? Text1 : Sub)) g.DrawString(name, Font, b, S(16), (l.Height - Font.GetHeight(g)) / 2);
        };
        l.Click += (s, e) => ShowPage(name);
        navItems.Add(l);
        return l;
    }

    public void ShowPage(string name)
    {
        if (!Pages.Contains(name)) name = "General";
        Page = name;
        foreach (var l in navItems) l.Invalidate();
        content.SuspendLayout();
        foreach (Control ctl in content.Controls.Cast<Control>().ToList()) ctl.Dispose();
        noteList = null;
        list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(S(18), S(8), S(18), S(18)), BackColor = Bg };
        content.Controls.Add(list);
        KinetikSuite.Look.Current.ScrollBars(list);
        // The page's title and what it's for, as in Kinetik's settings.
        list.Controls.Add(new Label { Text = name, Font = new Font("Segoe UI", 17f, FontStyle.Bold), ForeColor = Text1, AutoSize = true, Margin = new Padding(S(2), S(10), 0, 0) });
        list.Controls.Add(new Label { Text = Subtitles[Array.IndexOf(Pages, name)], ForeColor = Sub, AutoSize = true, Margin = new Padding(S(3), S(2), 0, S(4)) });
        if (name == "General") BuildGeneral();
        else if (name == "Appearance") BuildAppearance();
        else if (name == "Notes") BuildNotes();
        else if (name == "Placement") BuildPlacement();
        else BuildAbout();
        content.ResumeLayout();
        ActiveControl = null; // no text box selected and highlighted until it's clicked
    }

    public void RefreshPage()
    {
        int scroll = list == null ? 0 : -list.AutoScrollPosition.Y;
        ShowPage(Page);
        list.AutoScrollPosition = new Point(0, scroll);
    }

    // The note list on the Notes page follows the folder; other pages don't depend on it.
    public void NotesChanged() { if (Page == "Notes" && !(ActiveControl is TextBox)) RefreshPage(); }

    void Apply() { applyTimer.Stop(); applyTimer.Start(); }

    // ---- page building helpers
    void Header(string text)
    {
        list.Controls.Add(new Label { Text = text.ToUpperInvariant(), Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Sub, AutoSize = false, Size = new Size(RowW, S(32)), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(S(4), S(10), 0, S(8)) });
    }

    void Hint(string text, int lines = 2)
    {
        list.Controls.Add(new Label { Text = text, ForeColor = Sub, AutoSize = false, Size = new Size(RowW, S(20 * lines + 6)), Margin = new Padding(S(2), S(2), 0, S(6)) });
    }

    void Row(string title, string desc, Control right)
    {
        var p = new KinetikSuite.CardRow(k) { Size = new Size(RowW, S(desc == null ? 50 : 60)) };
        var t = new Label { Text = title, ForeColor = Text1, AutoSize = true, Location = new Point(S(14), desc == null ? (p.Height - S(18)) / 2 : S(10)), BackColor = CardBg };
        p.Controls.Add(t);
        if (desc != null) p.Controls.Add(new Label { Text = desc, ForeColor = Sub, AutoSize = false, Size = new Size(RowW - S(30) - right.Width, S(20)), Location = new Point(S(14), S(31)), Font = new Font("Segoe UI", 8.5f), BackColor = CardBg, AutoEllipsis = true });
        right.Location = new Point(p.Width - right.Width - S(14), (p.Height - right.Height) / 2);
        p.Controls.Add(right);
        list.Controls.Add(p);
    }

    void Check(string title, string desc, bool value, Action<bool> set)
    {
        var sw = new KinetikSuite.Switch(value, k);
        sw.Changed += (s, e) => { set(sw.On); Apply(); };
        Row(title, desc, sw);
    }

    void Choice(string title, string desc, string[] items, int selected, Action<int> set, int width = 200)
    {
        var cb = new KinetikSuite.Dropdown { Width = S(width), ItemHeight = S(22) };
        cb.Items.AddRange(items);
        cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
        cb.SelectedIndexChanged += (s, e) => { set(cb.SelectedIndex); Apply(); };
        Row(title, desc, cb);
    }

    // A slider with a number box beside it: drag for rough changes, or type an exact value. Typed values can go
    // past the slider's end (up to typedMax) when the setting allows it.
    void Slider(string title, string desc, int min, int max, int value, string unit, Action<int> set, int typedMax = -1)
    {
        if (typedMax < max) typedMax = max;
        var box = new Panel { Size = new Size(S(262), S(30)), BackColor = CardBg };
        var tb = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickStyle = TickStyle.None, AutoSize = false, Size = new Size(S(160), S(30)), Location = new Point(0, 0), BackColor = CardBg };
        var num = new NumericUpDown { Minimum = min, Maximum = typedMax, Value = Math.Max(min, Math.Min(typedMax, value)), Width = S(64), Location = new Point(S(164), S(3)), BackColor = Field, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
        var unitLbl = new Label { Text = unit.Trim(), ForeColor = Sub, AutoSize = false, Size = new Size(S(32), S(30)), Location = new Point(S(230), 0), TextAlign = ContentAlignment.MiddleLeft, BackColor = CardBg };
        bool syncing = false;
        tb.ValueChanged += (s, e) =>
        {
            if (syncing) return;
            syncing = true; num.Value = tb.Value; syncing = false;
            set(tb.Value); Apply();
        };
        num.ValueChanged += (s, e) =>
        {
            if (syncing) return;
            int v = (int)num.Value;
            syncing = true; tb.Value = Math.Max(min, Math.Min(max, v)); syncing = false;
            set(v); Apply();
        };
        box.Controls.Add(tb); box.Controls.Add(num); box.Controls.Add(unitLbl);
        Row(title, desc, box);
    }

    TextBox Field1(string value, int width, Action<string> commit)
    {
        var tb = new TextBox { Text = value, Width = S(width), BackColor = Field, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle };
        Action done = () => commit(tb.Text);
        tb.Leave += (s, e) => done();
        tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !tb.Multiline) { done(); e.SuppressKeyPress = true; } };
        return tb;
    }

    KinetikSuite.FlatButton Btn(string text, EventHandler click, bool accent = false)
    {
        var b = new KinetikSuite.FlatButton(text, accent, k) { Font = Font };
        b.Click += click;
        return b;
    }

    // ---- pages
    void BuildGeneral()
    {
        Header("Notes folder");
        var folder = Field1(c.Folder, 300, v => { v = v.Trim().Trim('"'); if (v != c.Folder) { c.Folder = v; Apply(); } });
        Row(Directory.Exists(c.Folder) ? "Folder" : "Folder (not found)", null, folder);
        var browse = Btn("Browse…", (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder of Markdown (.md) notes", SelectedPath = Directory.Exists(c.Folder) ? c.Folder : "" })
                if (dlg.ShowDialog(this) == DialogResult.OK) { c.Folder = dlg.SelectedPath; owner.ApplySettings(); RefreshPage(); }
        }, true);
        Row("Choose a folder", "Each .md file in it becomes a note", browse);
        Check("Include subfolders", "Also show notes from folders inside it", c.Subfolders, v => c.Subfolders = v);

        Header("New notes");
        var tpl = new TextBox { Text = (c.Template ?? "").Replace("\n", "\r\n"), Multiline = true, ScrollBars = ScrollBars.Vertical, Size = new Size(RowW, S(90)), BackColor = Field, ForeColor = Text1, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9.5f), Margin = new Padding(0, 0, 0, S(4)) };
        tpl.Leave += (s, e) => { var v = tpl.Text.Replace("\r\n", "\n"); if (v != c.Template) { c.Template = v; Apply(); } };
        bool hasFolder = Directory.Exists(c.Folder);
        var tplTools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = CardBg, Margin = Padding.Empty };
        var openTpl = Btn("Open", (s, e) =>
        {
            try { Notes.EnsureTemplates(c.Folder, false); Process.Start("explorer.exe", "\"" + Notes.TemplatesDir(c.Folder) + "\""); } catch { }
        });
        var restoreTpl = Btn("Restore", (s, e) =>
        {
            if (MessageBox.Show(this, "Put the starter templates back as they shipped? Edited copies with the same names are overwritten; your own extra templates are kept.", "Kinetik Notes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try { Notes.EnsureTemplates(c.Folder, true); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kinetik Notes"); }
        });
        openTpl.Margin = new Padding(0, 0, S(6), 0); restoreTpl.Margin = Padding.Empty;
        openTpl.Enabled = restoreTpl.Enabled = hasFolder;
        tplTools.Controls.Add(openTpl); tplTools.Controls.Add(restoreTpl);
        tplTools.Size = tplTools.PreferredSize;
        Row("Templates folder", "Each .md file is a template", tplTools);
        Hint("Right-click → New note lists them. Placeholders: {title} {date} {time} {weekday} {tomorrow} {weekstart} (Monday) {friday} {nextweek}", 2);
        Check("Ask for a title", "Otherwise notes are named after the template and date", c.AskTitle, v => c.AskTitle = v);
        Check("Open new notes for editing", null, c.OpenNew, v => c.OpenNew = v);
        Hint("Blank note template:", 1);
        list.Controls.Add(tpl);

        Header("Behaviour");
        Choice("Double-clicking a note", null, new[] { "Opens it in Obsidian", "Opens it in the default editor", "Shows it in File Explorer" }, c.DoubleClick, i => c.DoubleClick = i, 230);
        Check("Show / hide with Ctrl+Alt+N", "Works from any app. Clicking the tray icon does the same.", c.Hotkey, v => c.Hotkey = v);
        Check("Run at startup", null, Startup.IsOn, v => Startup.Set(v));

        Header("Backup");
        Row("Back up settings", "Saved next to the app. Last backup: " + Backup.SavedText, Btn("Back up", (s, e) => { owner.ApplySettings(); Backup.SaveNow(); RefreshPage(); }, true));
        Row("Restore settings", "Loads that backup, e.g. after reinstalling Windows. Your notes are files, so they aren't part of it.", Btn("Restore", (s, e) => Backup.RestoreNow()));
    }

    void BuildAppearance()
    {
        Header("Theme");
        Hint("The paper themes colour the whole note. The others match Kinetik's widgets and tint each note's title with its colour.", 2);
        var tiles = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size(RowW + S(8), 0), WrapContents = true, BackColor = Bg, Margin = new Padding(0, 0, 0, S(4)) };
        foreach (var name in Notes.ThemeNames)
        {
            var tile = new ThemeTile(name, name == c.ThemeName, k);
            tile.Click += (s, e) =>
            {
                c.ThemeName = tile.ThemeName;
                foreach (ThemeTile o in tiles.Controls) o.SetSelected(o == tile);
                Apply();
            };
            tiles.Controls.Add(tile);
        }
        list.Controls.Add(tiles);

        Header("Colours");
        var colours = new[] { "A different colour each" }.Concat(Notes.ColourNames.Select(n => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(n))).ToArray();
        Choice("Default colour", "For notes without \"color:\" in their front matter", colours, c.DefaultColour + 1, i => c.DefaultColour = i - 1);
        Slider("Opacity", null, 20, 100, c.Opacity, "%", v => c.Opacity = v);

        Header("Text");
        var fonts = new[] { "Default (Segoe UI)" }.Concat(Draw.Families).ToArray();
        int fi = Array.IndexOf(Draw.Families, c.FontName);
        Choice("Font", null, fonts, fi < 0 ? 0 : fi + 1, i => c.FontName = i == 0 ? "" : Draw.Families[i - 1], 230);
        Slider("Size", null, 60, 200, c.Scale, "%", v => c.Scale = v, 250);

        Header("Layout");
        Slider("Note width", null, 160, 520, c.Width, " px", v => c.Width = v, 800);
        Slider("Note length", "Longer notes scroll inside. 0 = no limit.", 0, 1000, c.MaxHeight, "px", v => c.MaxHeight = v, 4000);
        Choice("Columns", "Notes fill the shortest column first", new[] { "1", "2", "3", "4", "5", "6" }, c.Columns - 1, i => c.Columns = i + 1, 80);
        Slider("Space between notes", null, 0, 40, c.Gap, " px", v => c.Gap = v);
        Slider("Corner roundness", null, 0, 30, c.Round, " px", v => c.Round = v);

        Header("On each note");
        Check("Task count", "e.g. 2/5 in the title band", c.ShowCount, v => c.ShowCount = v);
        Check("Progress bar", "A thin bar under the title that fills as tasks are done", c.ShowProgress, v => c.ShowProgress = v);
        Check("Due date", "From \"due:\" in the front matter. Red when overdue.", c.ShowDue, v => c.ShowDue = v);
        Check("Tags", "From \"tags:\" and #tags in the text", c.ShowTags, v => c.ShowTags = v);
    }

    void BuildNotes()
    {
        Header("Order");
        Choice("Sort by", null, new[] { "File name", "Title", "Last changed", "Due date", "Most open tasks" }, c.Sort, i => c.Sort = i);
        Check("Pinned notes first", "Pin a note from its right-click menu", c.PinnedFirst, v => c.PinnedFirst = v);

        Header("What to show");
        Check("Hide finished tasks", null, c.HideDone, v => c.HideDone = v);
        Check("Hide notes when every task is done", null, c.HideFinishedNotes, v => c.HideFinishedNotes = v);
        Slider("Lines per note", "Longer notes end with \"+3 more…\". 0 = all.", 0, 30, c.MaxLines, "", v => c.MaxLines = v, 99);
        var tags = Field1(c.TagFilter, 200, v => { v = v.Trim(); if (v != c.TagFilter) { c.TagFilter = v; Apply(); } });
        Row("Only notes tagged", "Comma-separated, e.g. web, marking. Empty = all.", tags);

        Header("Notes");
        Hint("Untick a note to hide it from the desktop. Click a note's arrow to roll it up to just its title.", 2);
        noteList = new CheckedListBox { Size = new Size(RowW, S(200)), BackColor = CardBg, ForeColor = Text1, BorderStyle = BorderStyle.None, CheckOnClick = true, IntegralHeight = false };
        var notes = owner.AllNotes.OrderBy(n => n.Key).ToList();
        foreach (var n in notes) noteList.Items.Add(n.Title + "   (" + n.Key + ")", !Settings.InList(c.Hidden, n.Key));
        noteList.ItemCheck += (s, e) => { c.Hidden = Settings.WithKey(c.Hidden, notes[e.Index].Key, e.NewValue != CheckState.Checked); Apply(); };
        list.Controls.Add(noteList);
        var tools = new FlowLayoutPanel { AutoSize = true, BackColor = Bg, Margin = new Padding(0, S(6), 0, 0) };
        tools.Controls.Add(Btn("Show all", (s, e) => { c.Hidden = ""; owner.ApplySettings(); RefreshPage(); }));
        tools.Controls.Add(Btn("Unroll all", (s, e) => { c.Collapsed = ""; owner.ApplySettings(); }));
        tools.Controls.Add(Btn("Roll up all", (s, e) => { c.Collapsed = string.Join("|", owner.AllNotes.Select(n => n.Key)); owner.ApplySettings(); }));
        list.Controls.Add(tools);
    }

    void BuildPlacement()
    {
        Header("Window");
        Choice("Placement", "Where the notes sit among your windows", new[] { "On the desktop (behind windows)", "Normal window", "Always on top" }, c.Layer, i => c.Layer = i, 230);
        Check("Lock position", "Stops the notes being dragged by accident", c.Locked, v => c.Locked = v);
        Check("Snap to screen edges", null, c.Snap, v => c.Snap = v);
        Row("Position", "Moves the notes back to the top-left corner", Btn("Reset position", (s, e) => { c.X = c.Y = int.MinValue; owner.ApplySettings(); }));
    }

    void BuildAbout()
    {
        Header("Kinetik Notes " + Program.Version);
        Hint("Sticky notes from a folder of Markdown files. Click a task to tick it off; it's saved straight to the file. Double-click a note to open it. Right-click a note for its colour, pin, due date, roll-up and more.", 4);
        Header("Front matter");
        Hint("Add these between --- lines at the top of a note (the right-click menu writes most of them for you):", 2);
        var tb = new TextBox
        {
            Text = "---\r\ncolor: pink        # yellow, orange, pink, purple, blue, green\r\ntitle: Shown title\r\npinned: true\r\ndue: 2026-10-20\r\ntags: [web, marking]\r\n---",
            Multiline = true, ReadOnly = true, Size = new Size(RowW, S(140)), BackColor = CardBg, ForeColor = Text1, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f)
        };
        list.Controls.Add(tb);
        Hint("Copyright © 2026 Akila Sella Hennedige. GPL-3.0-or-later.", 1);
    }
}

// ======================================================================= Settings backup
// Copies the app's settings (HKCU\Software\<KeyName>) to "<KeyName>-settings.reg" next to the exe, so they survive a
// Windows reinstall when the app folder is kept (on OneDrive, a USB stick…). It's a normal .reg export, so
// double-clicking the file restores the settings as well. A restore runs after the app has closed, so nothing the app
// saves on the way out can overwrite it, and then the app starts again with the restored settings.
// Same code in every Kinetik app.
static class Backup
{
    public static string KeyName, AppTitle;
    public static Action BeforeExit; // e.g. hide the tray icon, as the app's own Exit does
    static bool restoreAfterExit;

    public static string FilePath
    {
        get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), KeyName + "-settings.reg"); }
    }
    public static bool Exists { get { return System.IO.File.Exists(FilePath); } }
    public static string SavedText
    {
        get { return Exists ? System.IO.File.GetLastWriteTime(FilePath).ToString("d MMM yyyy, h:mm tt") : "never"; }
    }

    static bool HasSettings
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\" + KeyName)) return k != null; }
            catch { return true; }
        }
    }

    static bool Reg(string args) { return KinetikSuite.RegistryFiles.Run(args); }

    static bool FileIsOurs()
    {
        try
        {
            string own = "[HKEY_CURRENT_USER\\Software\\" + KeyName;
            bool any = false;
            foreach (var line in System.IO.File.ReadAllLines(FilePath))
            {
                var l = line.Trim();
                if (!l.StartsWith("[")) continue;
                if (!l.StartsWith(own + "]", StringComparison.OrdinalIgnoreCase) && !l.StartsWith(own + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                any = true;
            }
            return any;
        }
        catch { return false; }
    }

    static bool Import()
    {
        try { return FileIsOurs() && KinetikSuite.RegistryFiles.Replace(FilePath, new[] { KeyName }); }
        catch (Exception ex) { Say(ex.Message, MessageBoxIcon.Warning); return false; }
    }

    static void Say(string text, MessageBoxIcon icon) { MessageBox.Show(text, AppTitle, MessageBoxButtons.OK, icon); }

    // At launch: no settings in the registry (a fresh Windows) but a backup beside the exe, so offer it.
    public static void OfferRestore()
    {
        if (HasSettings || !Exists) return;
        if (MessageBox.Show("There are saved " + AppTitle + " settings in this folder, from " + SavedText + ".\n\nRestore them?",
                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
    }

    public static void SaveNow()
    {
        if (Reg("export \"HKCU\\Software\\" + KeyName + "\" \"" + FilePath + "\" /y"))
            Say("Settings backed up to\n" + FilePath + "\n\nKeep this file with the app. After reinstalling Windows, " + AppTitle + " offers to restore it the first time it starts.", MessageBoxIcon.Information);
        else
            Say("Couldn't save the backup to\n" + FilePath + "\n\nCheck that the folder isn't read-only.", MessageBoxIcon.Warning);
    }

    public static void RestoreNow()
    {
        if (!Exists) { Say("There's no backup yet. Use \"Back up settings\" first.\n\nIt would be saved as\n" + FilePath, MessageBoxIcon.Information); return; }
        if (!FileIsOurs()) { Say("The backup file isn't a " + AppTitle + " settings file, so it wasn't used.", MessageBoxIcon.Warning); return; }
        if (MessageBox.Show("Replace the current settings with the backup from " + SavedText + "?\n\n" + AppTitle + " will restart.",
                AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        restoreAfterExit = true;
        if (BeforeExit != null) try { BeforeExit(); } catch { }
        Application.Exit();
    }

    // Called from Main once the app has closed and released its single-instance lock.
    public static void AfterExit()
    {
        if (!restoreAfterExit) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
        try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
    }
}

// ======================================================================= The right-click menu every Kinetik widget shares
// The look, placement, startup and backup options, in the same order and the same words in every Kinetik app, so they
// feel like one set. Each app adds its own items above. Same code in every Kinetik app.
#pragma warning disable 0649 // the hooks an app has no use for stay null
class CommonMenu
{
    public static readonly int[] Scales = { 80, 90, 100, 115, 125, 150 };
    public static readonly int[] Opacities = { 100, 95, 80, 65, 50, 30, 0 };
    public const int DefaultOpacity = 95;
    public static readonly string[] Fonts = { "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Aptos", "Inter", "Cascadia Code", "Consolas", "Verdana", "Georgia" };

    // Settings saved before the shared default changed from 92% to 95% move with it; any other choice is kept.
    public static int Migrate(int opacity) { return opacity == 92 ? DefaultOpacity : opacity; }

    public string Title, Version;
    public Action OpenTheme;                                        // opens the theme picker (or the page with it)
    public Action LookChanged;                                      // optional: after the App theme changes
    public Func<int> Scale; public Action<int> SetScale;
    public int[] Widths; public Func<int> CardWidth; public Action<int> SetCardWidth; // optional: Narrow, Normal, Wide
    public Func<string> FontName; public Action<string> SetFontName;   // optional; "" = the default font
    public Func<int> Opacity; public Action<int> SetOpacity;
    public Func<int> Layer; public Action<int> SetLayer;
    public Func<bool> Locked; public Action<bool> SetLocked;
    public Func<bool> ClickThrough; public Action<bool> SetClickThrough; // optional
    public Func<bool> Snap; public Action<bool> SetSnap;
    public string LockText = "Lock position";
    public Action OpenSettings;                                     // optional: the app's Settings window
    public Func<bool> RunAtStartup; public Action<bool> SetRunAtStartup;   // optional
    public Action BackUp, Restore; public Func<string> LastBackup; // optional
    public Action Exit;                                         // optional

    static ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    static bool HasFont(string name)
    {
        try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; }
    }

    // Appearance and placement: Theme, Size, Font, Background opacity, Placement.
    public void AddLook(ToolStripItemCollection items)
    {
        if (OpenTheme != null) items.Add(Item("Widget theme…", false, OpenTheme));
        items.Add(KinetikSuite.Look.MenuItem(LookChanged)); // the theme of every Kinetik app's windows and menus

        var size = new ToolStripMenuItem("Size");
        foreach (var s in Scales) { int v = s; size.DropDownItems.Add(Item(v + "%", Scale() == v, () => SetScale(v))); }
        if (Widths != null)
        {
            size.DropDownItems.Add(new ToolStripSeparator());
            string[] names = { "Narrow", "Normal", "Wide" };
            for (int i = 0; i < Widths.Length && i < 3; i++) { int v = Widths[i]; size.DropDownItems.Add(Item(names[i], CardWidth() == v, () => SetCardWidth(v))); }
        }
        items.Add(size);

        if (FontName != null)
        {
            var font = new ToolStripMenuItem("Font");
            font.DropDownItems.Add(Item("Default", FontName() == "", () => SetFontName("")));
            foreach (var n in Fonts.Where(HasFont)) { string v = n; font.DropDownItems.Add(Item(v, FontName() == v, () => SetFontName(v))); }
            if (FontName() != "" && !Fonts.Contains(FontName())) font.DropDownItems.Add(Item(FontName(), true, () => { }));
            items.Add(font);
        }

        var opacity = new ToolStripMenuItem("Background opacity");
        foreach (var o in Opacities) { int v = o; opacity.DropDownItems.Add(Item(v + "%", Opacity() == v, () => SetOpacity(v))); }
        if (!Opacities.Contains(Opacity())) opacity.DropDownItems.Add(Item(Opacity() + "%", true, () => { }));
        items.Add(opacity);

        var place = new ToolStripMenuItem("Placement");
        string[] layers = { "On the desktop", "Normal window", "Always on top" };
        for (int i = 0; i < 3; i++) { int v = i; place.DropDownItems.Add(Item(layers[i], Layer() == v, () => SetLayer(v))); }
        place.DropDownItems.Add(new ToolStripSeparator());
        place.DropDownItems.Add(Item(LockText, Locked(), () => SetLocked(!Locked())));
        if (ClickThrough != null) place.DropDownItems.Add(Item("Click-through when locked", ClickThrough(), () => SetClickThrough(!ClickThrough())));
        place.DropDownItems.Add(Item("Snap to screen edges", Snap(), () => SetSnap(!Snap())));
        items.Add(place);
    }

    // The rest: Settings…, Run at startup, Backup, then the version and Exit.
    public void AddEnd(ToolStripItemCollection items)
    {
        items.Add(new ToolStripSeparator());
        if (OpenSettings != null) items.Add(Item("Settings…", false, OpenSettings));
        if (RunAtStartup != null) items.Add(Item("Run at startup", RunAtStartup(), () => SetRunAtStartup(!RunAtStartup())));
        if (BackUp != null)
        {
            var backup = new ToolStripMenuItem("Backup");
            backup.DropDownItems.Add(Item("Back up settings", false, BackUp));
            backup.DropDownItems.Add(Item("Restore settings…", false, Restore));
            backup.DropDownItems.Add(new ToolStripMenuItem("Last backup: " + LastBackup()) { Enabled = false });
            items.Add(backup);
        }
        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem(Title + " " + Version) { Enabled = false });
        if (Exit != null) items.Add(Item("Exit", false, Exit));
    }

    public void AddAll(ToolStripItemCollection items) { AddLook(items); AddEnd(items); }
}
#pragma warning restore 0649
