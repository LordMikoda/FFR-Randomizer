// FFR Randomizer GUI: writes the Randomizer File (Randomizer.txt) that the
// UE4SS mod reads when the game starts. Built with the C# 5 compiler that
// ships with .NET Framework 4.8, so it needs nothing installed. See build.cmd.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("FFR Randomizer")]
[assembly: System.Reflection.AssemblyProduct("FFR Randomizer")]
[assembly: System.Reflection.AssemblyVersion("0.7.0.0")]

namespace FFRRandomizerGui
{
    static class Theme
    {
        public static readonly Color Ground = ColorTranslator.FromHtml("#14171f");
        public static readonly Color Panel = ColorTranslator.FromHtml("#1c212b");
        public static readonly Color Border = ColorTranslator.FromHtml("#2c3340");
        public static readonly Color Text = ColorTranslator.FromHtml("#e8ebf0");
        public static readonly Color Muted = ColorTranslator.FromHtml("#9aa3b2");
        public static readonly Color Accent = ColorTranslator.FromHtml("#5aa9e6");
        public static readonly Color AccentText = ColorTranslator.FromHtml("#0b1220");
        public static readonly Color Button = ColorTranslator.FromHtml("#262c38");
        public static readonly Color ButtonBorder = ColorTranslator.FromHtml("#3a4250");
        public static readonly Color Good = ColorTranslator.FromHtml("#8fd3a8");
        public static readonly Color GoodBack = ColorTranslator.FromHtml("#17291f");
        public static readonly Color GoodBorder = ColorTranslator.FromHtml("#2d5a3e");
        public static readonly Color Bad = ColorTranslator.FromHtml("#f2a3a3");
        public static readonly Color BadBack = ColorTranslator.FromHtml("#2a1a1c");
        public static readonly Color BadBorder = ColorTranslator.FromHtml("#6b3a3e");
        public static readonly Color Warn = ColorTranslator.FromHtml("#f2c97d");
        public static readonly Color WarnBack = ColorTranslator.FromHtml("#2a2416");
        public static readonly Color WarnBorder = ColorTranslator.FromHtml("#6b5a2e");
        public static readonly Color Knob = ColorTranslator.FromHtml("#f4f6f9");

        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    // The game, mod and Randomizer File locations, and the file's format.
    static class Game
    {
        // Kept in step with MOD_VERSION in the mod's main.lua.
        public const string AppVersion = "0.7.0";
        public const string SteamAppId = "3259780";
        public const string FolderName = "FINAL FANTASY RESONANCE";
        public const string ModPath = @"FFRS\Binaries\Win64\ue4ss\Mods\FFRRandomizer";
        public const string Ue4ssPath = @"FFRS\Binaries\Win64\ue4ss\UE4SS.dll";

        // Must stay in step with SETTINGS_TEMPLATE in the mod's main.lua.
        const string Template =
            "# FFR Randomizer settings, written by the FFR Randomizer GUI. You can also edit it by hand.\n" +
            "# Keep the same file for a whole playthrough. Change it, then restart the game.\n" +
            "seed={0}\n" +
            "# on or off\n" +
            "items={1}\n" +
            "shops={2}\n" +
            "visions={3}\n" +
            "enemies={4}\n" +
            "bosses={5}\n" +
            "# Vision abilities. mix_physical_magic and supports only matter with abilities on.\n" +
            "abilities={6}\n" +
            "mix_physical_magic={7}\n" +
            "finishers={8}\n" +
            "supports={9}\n" +
            "# Characters. mix_character_moves only matters with character_moves or character_finishers on.\n" +
            "character_moves={10}\n" +
            "character_finishers={11}\n" +
            "mix_character_moves={12}\n" +
            "character_stats={13}\n" +
            "# Gear and enemies.\n" +
            "gear_stats={14}\n" +
            "gear_passives={15}\n" +
            "enemy_weaknesses={16}\n" +
            "status_resistances={17}\n" +
            "enemy_passives={18}\n" +
            "# normal, half or double\n" +
            "encounter_rate={19}\n" +
            "# We are Chaos. These can make a run unwinnable.\n" +
            "random_stats={20}\n" +
            "swap_gear_types={21}\n" +
            "full_strength={22}\n" +
            "shops_sell_anything={23}\n" +
            "story_items={24}\n" +
            "# default, 9 or 50. Weapons, armor and accessories always stay at 1.\n" +
            "chest_quantity={25}\n";

        // The keys of the on/off lines, in the template's order after the seed.
        public static readonly string[] ToggleKeys = {
            "items", "shops", "visions", "enemies", "bosses",
            "abilities", "mix_physical_magic", "finishers", "supports",
            "character_moves", "character_finishers", "mix_character_moves", "character_stats",
            "gear_stats", "gear_passives", "enemy_weaknesses", "status_resistances", "enemy_passives" };
        public static readonly string[] ChaosKeys = {
            "random_stats", "swap_gear_types", "full_strength", "shops_sell_anything", "story_items" };

        public static string ModDir(string gameDir)
        {
            return Path.Combine(gameDir, ModPath);
        }

        public static string SettingsFile(string gameDir)
        {
            return Path.Combine(ModDir(gameDir), "Randomizer.txt");
        }

        // Matches the name the mod gives the spoiler log.
        public static string SpoilerFile(string gameDir, string seed)
        {
            return Path.Combine(ModDir(gameDir), "Spoiler_" + Regex.Replace(seed, "[^A-Za-z0-9_-]", "_") + ".txt");
        }

        // Accepts the game folder or any folder inside it, such as FFRS.
        public static string Normalize(string folder)
        {
            string dir = folder;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, @"FFRS\Binaries\Win64\FFRS-Win64-Shipping.exe")) ||
                    Directory.Exists(Path.Combine(dir, @"FFRS\Binaries\Win64")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return folder;
        }

        public static string Detect()
        {
            foreach (string view in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId,
                                            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId })
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(view))
                {
                    string location = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                    {
                        return location;
                    }
                }
            }
            foreach (string library in SteamLibraries())
            {
                string candidate = Path.Combine(library, @"steamapps\common\" + FolderName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return "";
        }

        static IEnumerable<string> SteamLibraries()
        {
            List<string> libraries = new List<string>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string steam = key == null ? null : key.GetValue("SteamPath") as string;
                if (string.IsNullOrEmpty(steam))
                {
                    return libraries;
                }
                libraries.Add(steam.Replace('/', '\\'));
                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }
            }
            return libraries;
        }

        public static string StatusFile(string gameDir)
        {
            return Path.Combine(ModDir(gameDir), "Status.txt");
        }

        // MOD_VERSION from the installed main.lua, or null when it can't be read.
        public static string ModVersion(string gameDir)
        {
            try
            {
                Match match = Regex.Match(File.ReadAllText(Path.Combine(ModDir(gameDir), @"Scripts\main.lua")),
                    "MOD_VERSION\\s*=\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        // -1, 0 or 1 as a is older, the same as or newer than b. Text that
        // isn't a version counts as the same.
        public static int CompareVersions(string a, string b)
        {
            Version va, vb;
            if (!Version.TryParse(a, out va) || !Version.TryParse(b, out vb))
            {
                return 0;
            }
            return va.CompareTo(vb) < 0 ? -1 : va.CompareTo(vb) > 0 ? 1 : 0;
        }

        public static Dictionary<string, string> ReadSettings(string gameDir)
        {
            return ReadValues(SettingsFile(gameDir));
        }

        // key=value lines, keys in lower case. Missing or unreadable files give none.
        public static Dictionary<string, string> ReadValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            if (!File.Exists(path))
            {
                return values;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return values;
            }
            catch (UnauthorizedAccessException)
            {
                return values;
            }
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, @"^\s*([A-Za-z0-9_]+)\s*=(.*)$");
                if (match.Success)
                {
                    values[match.Groups[1].Value.ToLowerInvariant()] = match.Groups[2].Value.Trim();
                }
            }
            return values;
        }

        // values: the seed, then the template's values in order, already as text.
        public static void WriteSettings(string gameDir, string[] values)
        {
            File.WriteAllText(SettingsFile(gameDir), string.Format(Template, (object[])values), new UTF8Encoding(false));
        }

        public static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }
    }

    // The seed and every option in one line, such as FFR1-4KQ7M2D-49353958654:
    // FFR and the format number, the options as bits in 7 Crockford base32
    // characters, then the seed as typed. Must stay in step with settingsCode
    // in the mod's main.lua. Bits 0 to 22 are the switches in the settings
    // file's order (Game.ToggleKeys, then Game.ChaosKeys), 23 and 24 the
    // encounter rate, 25 and 26 the chest quantity; 27 to 34 are free.
    static class SettingsCode
    {
        public const int Format = 1;
        const string Digits = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const int ToggleCount = 23;

        public static string Encode(string seed, bool[] toggles, int encounter, int quantity)
        {
            long bits = 0;
            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i])
                {
                    bits |= 1L << i;
                }
            }
            bits |= (long)encounter << ToggleCount;
            bits |= (long)quantity << (ToggleCount + 2);
            StringBuilder text = new StringBuilder("FFR" + Format + "-");
            for (int i = 6; i >= 0; i--)
            {
                text.Append(Digits[(int)((bits >> (5 * i)) & 31)]);
            }
            return text.Append('-').Append(seed).ToString();
        }

        // The code a settings file's values give, read the way the mod reads
        // them: items, shops and Visions default to on, the rest to off.
        public static string FromValues(Dictionary<string, string> values)
        {
            List<string> keys = new List<string>(Game.ToggleKeys);
            keys.AddRange(Game.ChaosKeys);
            bool[] toggles = new bool[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                string value;
                toggles[i] = values.TryGetValue(keys[i], out value)
                    ? !(value.ToLowerInvariant() == "off" || value.ToLowerInvariant() == "false" || value == "0")
                    : i < 3;
            }
            string seed, encounter, quantity;
            values.TryGetValue("seed", out seed);
            values.TryGetValue("encounter_rate", out encounter);
            values.TryGetValue("chest_quantity", out quantity);
            return Encode(seed ?? "", toggles,
                Math.Max(0, Array.IndexOf(new[] { "normal", "half", "double" }, (encounter ?? "").ToLowerInvariant())),
                Math.Max(0, Array.IndexOf(new[] { "default", "9", "50" }, (quantity ?? "").ToLowerInvariant())));
        }

        // Reads a code. On failure, error says why and the outputs are unset.
        public static bool TryDecode(string code, out string seed, out bool[] toggles, out int encounter,
            out int quantity, out string error)
        {
            seed = null;
            toggles = null;
            encounter = quantity = 0;
            error = null;
            Match match = Regex.Match(code ?? "", @"^\s*FFR(\d+)-([0-9A-Za-z]{7})-(\S{1,40})\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                error = "That isn’t an FFR settings code. Codes look like FFR1-4KQ7M2D-49353958654.";
                return false;
            }
            int format;
            if (!int.TryParse(match.Groups[1].Value, out format) || format != Format)
            {
                error = format > Format
                    ? "This code is from a newer FFR Randomizer (FFR" + format + "). Update the app to load it."
                    : "That isn’t an FFR settings code. Codes look like FFR1-4KQ7M2D-49353958654.";
                return false;
            }
            long bits = 0;
            foreach (char c in match.Groups[2].Value.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1'))
            {
                int digit = Digits.IndexOf(c);
                if (digit < 0)
                {
                    error = "That settings code has a letter codes never use (U). Check it was copied whole.";
                    return false;
                }
                bits = (bits << 5) + digit;
            }
            encounter = (int)((bits >> ToggleCount) & 3);
            quantity = (int)((bits >> (ToggleCount + 2)) & 3);
            if ((bits >> (ToggleCount + 4)) != 0 || encounter > 2 || quantity > 2)
            {
                error = "This code has options this app doesn’t know. Update the app to load it.";
                return false;
            }
            toggles = new bool[ToggleCount];
            for (int i = 0; i < ToggleCount; i++)
            {
                toggles[i] = ((bits >> i) & 1) == 1;
            }
            seed = match.Groups[3].Value;
            return true;
        }
    }

    // The GUI's own memory of the chosen game folder.
    static class GuiConfig
    {
        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"FFRRandomizer\gui.txt");
            }
        }

        public static string LoadGameDir()
        {
            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : "";
            }
            catch (IOException)
            {
                return "";
            }
        }

        public static void SaveGameDir(string gameDir)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, gameDir);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    class ToggleSwitch : Control
    {
        bool isOn = true;

        public event EventHandler Changed;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(52, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public bool On
        {
            get { return isOn; }
            set
            {
                if (isOn == value)
                {
                    return;
                }
                isOn = value;
                AccessibleDescription = isOn ? "On" : "Off";
                Invalidate();
                if (Changed != null)
                {
                    Changed(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            On = !On;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                On = !On;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            Rectangle track = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Shapes.Rounded(track, track.Height / 2))
            using (Brush brush = new SolidBrush(!Enabled ? Theme.Border : isOn ? Theme.Accent : Theme.ButtonBorder))
            {
                g.FillPath(brush, path);
                if (Focused)
                {
                    using (Pen pen = new Pen(Theme.Text, 1.5f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
            int knob = track.Height - 6;
            int x = isOn ? track.Right - knob - 3 : track.Left + 3;
            using (Brush brush = new SolidBrush(Enabled ? Theme.Knob : Theme.Muted))
            {
                g.FillEllipse(brush, x, track.Top + 3, knob, knob);
            }
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    // A bordered, rounded panel, used for the input boxes and the shuffle list.
    class Card : Panel
    {
        public int Radius = 8;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public Color Outline = Theme.Border;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Ground);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shapes.Rounded(box, Radius))
            using (Brush brush = new SolidBrush(BackColor))
            using (Pen pen = new Pen(Outline))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    static class Shapes
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // A tab in the strip above the options. The selected one is a raised panel
    // with an accent line along its bottom; the others are plain text.
    class TabButton : Control
    {
        bool selected;
        string count = "";

        public TabButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTab;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; AccessibleDescription = value ? "Selected" : ""; Invalidate(); }
        }

        // How many of the tab's options are on, shown after its name.
        public string Count
        {
            get { return count; }
            set { count = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Ground);
            float s = DeviceDpi / 96f;
            if (selected)
            {
                // Rounded at the top only: the shape runs past the bottom edge.
                Rectangle shape = new Rectangle(0, 0, Width - 1, Height + (int)(10 * s));
                using (GraphicsPath path = Shapes.Rounded(shape, (int)(8 * s)))
                using (Brush fill = new SolidBrush(Theme.Panel))
                using (Pen edge = new Pen(Focused ? Theme.Text : Theme.Border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                }
                using (Brush accent = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(accent, 0, Height - 3 * s, Width, 3 * s);
                }
            }
            else
            {
                using (Pen line = new Pen(Focused ? Theme.Muted : Theme.Border))
                {
                    g.DrawLine(line, 0, Height - 1, Width, Height - 1);
                    if (Focused)
                    {
                        g.DrawRectangle(line, 0, 0, Width - 1, Height - 2);
                    }
                }
            }
            using (Font name = Theme.Ui(10.5f, selected ? FontStyle.Bold : FontStyle.Regular))
            using (Font small = Theme.Ui(9f, FontStyle.Regular))
            {
                Size nameSize = TextRenderer.MeasureText(g, Text, name, Size.Empty, TextFormatFlags.NoPadding);
                Size countSize = TextRenderer.MeasureText(g, count, small, Size.Empty, TextFormatFlags.NoPadding);
                int gap = (int)(8 * s);
                bool showCount = nameSize.Width + gap + countSize.Width + (int)(16 * s) <= Width;
                int x = (Width - nameSize.Width - (showCount ? gap + countSize.Width : 0)) / 2;
                int y = (Height - nameSize.Height) / 2;
                TextRenderer.DrawText(g, Text, name, new Point(x, y), selected ? Theme.Text : Theme.Muted, TextFormatFlags.NoPadding);
                if (showCount)
                {
                    TextRenderer.DrawText(g, count, small, new Point(x + nameSize.Width + gap, y + nameSize.Height - countSize.Height),
                        Theme.Muted, TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // One tab of options: sections across the full width, then a left and a
    // right column. Toggles are the ones its count covers.
    class OptionTab
    {
        public TabButton Button = new TabButton();
        public readonly List<Panel> Wide = new List<Panel>();
        public readonly List<Panel> Left = new List<Panel>();
        public readonly List<Panel> Right = new List<Panel>();
        public readonly List<ToggleSwitch> Toggles = new List<ToggleSwitch>();

        public IEnumerable<Panel> Sections
        {
            get
            {
                foreach (Panel panel in Wide) yield return panel;
                foreach (Panel panel in Left) yield return panel;
                foreach (Panel panel in Right) yield return panel;
            }
        }

        // The tab's content height in the given number of columns.
        public int ContentHeight(int columns)
        {
            int wide = 0, left = 0, right = 0;
            foreach (Panel panel in Wide) wide += panel.Height;
            foreach (Panel panel in Left) left += panel.Height;
            foreach (Panel panel in Right) right += panel.Height;
            return wide + (columns == 2 ? Math.Max(left, right) : left + right);
        }
    }

    class MainForm : Form
    {
        const int Pad = 20;
        const int ContentWidth = 520;
        const int HeaderHeight = 52;
        // The apply status (or a saved message over it) and the buttons.
        const int FooterHeight = 160;
        // Space above the game folder and seed, the tab strip under them, and
        // the space above and below a tab's options.
        const int TopGap = 14;
        const int StripHeight = 54;
        const int ContentTop = 10;
        const int ContentBottom = 10;
        // Two columns need this much width.
        const int TwoColumnWidth = 2 * ContentWidth + 3 * Pad;

        Panel folderSection;
        Panel seedSection;
        // The settings code, the whole width under the folder and seed.
        Panel codeSection;
        readonly Panel strip = new Panel();
        // The current tab's options. It scrolls when the window is too short.
        readonly Panel body = new Panel();
        readonly List<OptionTab> tabs = new List<OptionTab>();
        readonly HashSet<ToggleSwitch> laterToggles = new HashSet<ToggleSwitch>();
        OptionTab current;
        int fullHeight;
        bool layingOut;
        bool layoutQueued;
        bool loaded;
        // Where AddLabel and the other helpers put new controls.
        Control target;

        readonly TextBox folderBox = new TextBox();
        readonly Label folderStatus = new Label();
        readonly TextBox seedBox = new TextBox();
        readonly ToggleSwitch itemsToggle = new ToggleSwitch();
        readonly ToggleSwitch shopsToggle = new ToggleSwitch();
        readonly ToggleSwitch visionsToggle = new ToggleSwitch();
        readonly ToggleSwitch enemiesToggle = new ToggleSwitch();
        readonly ToggleSwitch bossesToggle = new ToggleSwitch();
        readonly ToggleSwitch abilitiesToggle = new ToggleSwitch();
        readonly ToggleSwitch mixToggle = new ToggleSwitch();
        readonly ToggleSwitch supportsToggle = new ToggleSwitch();
        readonly ToggleSwitch finishersToggle = new ToggleSwitch();
        readonly ToggleSwitch movesToggle = new ToggleSwitch();
        readonly ToggleSwitch characterFinishersToggle = new ToggleSwitch();
        readonly ToggleSwitch mixCharactersToggle = new ToggleSwitch();
        readonly ToggleSwitch statsToggle = new ToggleSwitch();
        readonly ToggleSwitch gearStatsToggle = new ToggleSwitch();
        readonly ToggleSwitch gearPassivesToggle = new ToggleSwitch();
        readonly ToggleSwitch weaknessesToggle = new ToggleSwitch();
        readonly ToggleSwitch statusToggle = new ToggleSwitch();
        readonly ToggleSwitch enemyPassivesToggle = new ToggleSwitch();
        readonly ToggleSwitch randomStatsToggle = new ToggleSwitch();
        readonly ToggleSwitch swapGearToggle = new ToggleSwitch();
        readonly ToggleSwitch fullStrengthToggle = new ToggleSwitch();
        readonly ToggleSwitch shopsAnythingToggle = new ToggleSwitch();
        readonly ToggleSwitch storyItemsToggle = new ToggleSwitch();
        readonly RadioButton[] encounterButtons = new RadioButton[3];
        readonly string[] encounterValues = { "normal", "half", "double" };
        readonly Label encounterHelp = new Label();
        readonly RadioButton[] quantityButtons = new RadioButton[3];
        readonly string[] quantityValues = { "default", "9", "50" };
        readonly Label quantityHelp = new Label();
        readonly TextBox codeBox = new TextBox();
        readonly Card savedCard = new Card();
        readonly Label savedText = new Label();
        readonly Card statusCard = new Card();
        readonly Label statusTitle = new Label();
        readonly Label statusDetail = new Label();
        readonly Label statusCheck = new Label();
        bool loadingState;
        DateTime statusWritten;
        readonly Button createButton = new Button();
        readonly Random random = new Random();
        string gameDir = "";

        public MainForm()
        {
            Text = "FFR Randomizer";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ResizeRedraw = true;
            BackColor = Theme.Ground;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f, FontStyle.Regular);
            using (Stream icon = typeof(MainForm).Assembly.GetManifestResourceStream("FFRRandomizerGui.icon.ico"))
            {
                if (icon != null)
                {
                    Icon = new Icon(icon);
                }
            }

            folderSection = BuildSection(BuildFolder);
            seedSection = BuildSection(BuildSeed);
            codeSection = BuildSection(BuildCode);
            BuildTabs();
            int content = 0;
            foreach (OptionTab tab in tabs)
            {
                content = Math.Max(content, tab.ContentHeight(2));
            }
            fullHeight = HeaderHeight + 1 + TopGap + Math.Max(folderSection.Height, seedSection.Height) + codeSection.Height
                + StripHeight + ContentTop + content + ContentBottom + FooterHeight;

            // Built at the one-column width so every anchor measures from the
            // design layout, then widened to two columns.
            target = this;
            ClientSize = new Size(ContentWidth + 2 * Pad, fullHeight);
            BuildHeader();
            BuildFooter();
            foreach (Control control in Controls)
            {
                Stretch(control, control.Top >= fullHeight - FooterHeight ? AnchorStyles.Bottom : AnchorStyles.Top);
            }
            Controls.Add(folderSection);
            Controls.Add(seedSection);
            Controls.Add(codeSection);
            BuildStrip();
            body.AutoScroll = true;
            body.BackColor = Theme.Ground;
            foreach (OptionTab tab in tabs)
            {
                foreach (Panel section in tab.Sections)
                {
                    section.Visible = false;
                    body.Controls.Add(section);
                }
            }
            Controls.Add(body);
            ClientSize = new Size(TwoColumnWidth, fullHeight);
            SelectTab(tabs[0]);

            LoadState();
            ActiveControl = createButton;
        }

        // The three tabs. NEW marks what this version added.
        void BuildTabs()
        {
            OptionTab light = AddTab("We Are the Light");
            light.Wide.Add(BuildSection(y => AddNote("The randomizer as intended: everything shuffles, every character and " +
                "Vision stays playable, and the story can always be finished.", y)));
            light.Left.Add(BuildSection(BuildShuffles));
            light.Left.Add(BuildSection(y => BuildChoice("Chest quantity", y, new[] { "Default", "Up to 9", "Up to 50" },
                quantityButtons, quantityHelp)));
            light.Right.Add(BuildSection(y => AddToggles(light, "Visions", y,
                Row("Vision abilities", "Abilities and passives swap among Physical or Magic Visions.", abilitiesToggle),
                Row("Supports stay supports", "Fina, Leah, Ayaka and Y\u2019shtola keep 5 or more healing abilities.", supportsToggle),
                Row("Vision finishers", "The 26 Vision finishers trade places.", finishersToggle))));
            light.Right.Add(BuildSection(y => AddToggles(light, "Characters", y,
                Row("Character moves", "Level-up moves swap among characters of the same attack type.", movesToggle),
                Row("Character finishers", "Finishers swap among characters of the same attack type.", characterFinishersToggle))));
            light.Toggles.AddRange(new[] { itemsToggle, shopsToggle, visionsToggle, enemiesToggle, bossesToggle });

            OptionTab become = AddTab("We must BECOME Chaos");
            become.Wide.Add(BuildSection(y => AddNote("Wilder, still finishable. Builds stop making sense, but nothing is " +
                "taken away for good.", y)));
            become.Left.Add(BuildSection(y => AddToggles(become, "Visions and characters", y,
                Row("Mix Physical and Magic", "Vision abilities come from all 26 Visions.", mixToggle),
                Row("Mix character moves", "Moves and finishers swap across Physical and Magic characters.", mixCharactersToggle),
                Row("Random character stats", "Five main stats trade growth curves per character. HP and MP stay.", statsToggle))));
            become.Left.Add(BuildSection(y => AddToggles(become, "Gear", y,
                Row("Gear stats", "Gear of one kind trades stat sets. Prices stay.", gearStatsToggle, "NEW"),
                Row("Gear passives", "Passives swap among weapons, armor or accessories.", gearPassivesToggle, "NEW"))));
            become.Right.Add(BuildSection(y => AddToggles(become, "Enemies", y,
                Row("Enemy weaknesses", "Each enemy\u2019s element weaknesses and resistances trade places.", weaknessesToggle, "NEW"),
                Row("Status resistances", "Enemies resist different ailments. Bosses keep theirs.", statusToggle, "NEW"),
                Row("Enemy passives", "Passives swap among regular enemies. Bosses keep theirs.", enemyPassivesToggle, "NEW"))));
            become.Right.Add(BuildSection(y => BuildChoice("Encounter rate", y, new[] { "Normal", "Half", "Double" },
                encounterButtons, encounterHelp)));

            OptionTab chaos = AddTab("We are Chaos");
            chaos.Wide.Add(BuildSection(y => AddNote("No guard rails. All off by default.", y)));
            chaos.Wide.Add(BuildSection(BuildChaosWarning));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Characters and gear", y,
                Row("Fully random stats", "Every stat curve, HP and MP too, comes from anyone. No budget.", randomStatsToggle, "NEW"),
                Row("Swap gear types", "Characters trade weapon and armor types. Rain might use a bow.", swapGearToggle, "NEW"))));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Enemies", y,
                Row("Enemies at full strength", "Swapped enemies and bosses keep their own stats.", fullStrengthToggle, "NEW"))));
            chaos.Right.Add(BuildSection(y => AddToggles(chaos, "Items and shops", y,
                Row("Shops sell anything", "Any item with a price can turn up in any shop.", shopsAnythingToggle, "NEW"),
                Row("Story items in the shuffle", "Key and quest items move too. Can softlock the story.", storyItemsToggle, "NEW"))));

            abilitiesToggle.Changed += delegate { UpdateOptions(); };
            movesToggle.Changed += delegate { UpdateOptions(); };
            characterFinishersToggle.Changed += delegate { UpdateOptions(); };
            statsToggle.Changed += delegate { UpdateOptions(); };
            randomStatsToggle.Changed += delegate { UpdateOptions(); };
        }

        // The on/off switches in the settings file's order (Game.ToggleKeys,
        // then Game.ChaosKeys).
        ToggleSwitch[] SettingToggles()
        {
            return new[] {
                itemsToggle, shopsToggle, visionsToggle, enemiesToggle, bossesToggle,
                abilitiesToggle, mixToggle, finishersToggle, supportsToggle,
                movesToggle, characterFinishersToggle, mixCharactersToggle, statsToggle,
                gearStatsToggle, gearPassivesToggle, weaknessesToggle, statusToggle, enemyPassivesToggle,
                randomStatsToggle, swapGearToggle, fullStrengthToggle, shopsAnythingToggle, storyItemsToggle };
        }

        OptionTab AddTab(string name)
        {
            OptionTab tab = new OptionTab();
            tab.Button.Text = name;
            tab.Button.AccessibleName = name;
            tab.Button.Click += delegate { SelectTab(tab); };
            tabs.Add(tab);
            return tab;
        }

        // The tab buttons sit along the bottom of the strip, over a dividing line.
        void BuildStrip()
        {
            strip.BackColor = Theme.Ground;
            strip.Size = new Size(ContentWidth + 2 * Pad, StripHeight);
            foreach (OptionTab tab in tabs)
            {
                strip.Controls.Add(tab.Button);
            }
            strip.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen line = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(line, 0, strip.Height - 1, strip.Width, strip.Height - 1);
                }
            };
            Controls.Add(strip);
        }

        void SelectTab(OptionTab tab)
        {
            if (current != null)
            {
                foreach (Panel section in current.Sections)
                {
                    section.Visible = false;
                }
            }
            current = tab;
            foreach (OptionTab other in tabs)
            {
                other.Button.Selected = other == tab;
            }
            body.AutoScrollPosition = Point.Empty;
            foreach (Panel section in tab.Sections)
            {
                section.Visible = true;
            }
            LayOut();
        }

        // One section of settings in its own panel, laid out at the design
        // width with the usual side margins, so its controls keep their places.
        Panel BuildSection(Func<int, int> build)
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Ground;
            panel.Width = ContentWidth + 2 * Pad;
            target = panel;
            panel.Height = build(0);
            foreach (Control control in panel.Controls)
            {
                Stretch(control, AnchorStyles.Top);
            }
            return panel;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            QueueLayOut();
        }

        // Laid out after the resize finishes: moving the sections while the
        // window is mid-resize leaves the body's scroll area at its old size.
        void QueueLayOut()
        {
            if (!IsHandleCreated)
            {
                LayOut();
            }
            else if (!layoutQueued)
            {
                layoutQueued = true;
                BeginInvoke(new MethodInvoker(delegate { layoutQueued = false; LayOut(); }));
            }
        }

        // Two columns when the window is wide enough, otherwise one: the game
        // folder and seed side by side or stacked, then the tab strip, then the
        // current tab's options, which stretch to the column width. The window
        // can't grow taller than the tallest tab needs.
        void LayOut()
        {
            if (layingOut || current == null || folderSection == null)
            {
                return;
            }
            layingOut = true;
            // The Create button is built 48 px tall, so it gives the scale.
            float s = createButton.Height / 48f;
            int pad = (int)Math.Round(Pad * s);
            int width = ClientSize.Width;
            int columns = width >= (int)(TwoColumnWidth * s) ? 2 : 1;
            int column = (width - pad * (columns + 1)) / columns;
            int y = (int)Math.Round((HeaderHeight + 1 + TopGap) * s);
            if (columns == 2)
            {
                folderSection.SetBounds(0, y, column + 2 * pad, folderSection.Height);
                seedSection.SetBounds(column + pad, y, column + 2 * pad, seedSection.Height);
                y += Math.Max(folderSection.Height, seedSection.Height);
            }
            else
            {
                folderSection.SetBounds(0, y, width, folderSection.Height);
                y += folderSection.Height;
                seedSection.SetBounds(0, y, width, seedSection.Height);
                y += seedSection.Height;
            }
            codeSection.SetBounds(0, y, width, codeSection.Height);
            y += codeSection.Height;
            strip.SetBounds(0, y, width, strip.Height);
            LayOutTabButtons(s, pad, width);
            y += strip.Height;
            int footerTop = ClientSize.Height - (int)Math.Round(FooterHeight * s);
            body.SetBounds(0, y, width, Math.Max(0, footerTop - y));

            int top = (int)Math.Round(ContentTop * s);
            int bottom = (int)Math.Round(ContentBottom * s);
            // Leave room for the scroll bar whenever the options don't fit, so
            // they never need a sideways one.
            int available = width;
            if (top + current.ContentHeight(columns) + bottom > body.Height)
            {
                available -= SystemInformation.VerticalScrollBarWidth;
            }
            int inner = (available - pad * (columns + 1)) / columns;
            // Resuming layout afterwards makes the body measure its scroll area again.
            body.SuspendLayout();
            body.AutoScrollMargin = new Size(0, bottom);
            int scroll = body.AutoScrollPosition.Y;
            int at = top;
            foreach (Panel section in current.Wide)
            {
                section.SetBounds(0, at + scroll, available, section.Height);
                at += section.Height;
            }
            int[] tops = { at, at };
            foreach (Panel section in current.Left)
            {
                section.SetBounds(0, tops[0] + scroll, inner + 2 * pad, section.Height);
                tops[0] += section.Height;
            }
            int right = columns == 2 ? 1 : 0;
            foreach (Panel section in current.Right)
            {
                section.SetBounds(right * (inner + pad), tops[right] + scroll, inner + 2 * pad, section.Height);
                tops[right] += section.Height;
            }
            body.ResumeLayout(true);
            if (loaded)
            {
                int tallest = 0;
                foreach (OptionTab tab in tabs)
                {
                    tallest = Math.Max(tallest, tab.ContentHeight(columns));
                }
                int needed = body.Top + top + tallest + bottom + (int)Math.Round(FooterHeight * s) + Height - ClientSize.Height;
                MaximumSize = new Size(MaximumSize.Width, Math.Max(needed, MinimumSize.Height));
            }
            layingOut = false;
        }

        static readonly int[] TabWidths = { 220, 280, 190 };

        void LayOutTabButtons(float s, int pad, int width)
        {
            int gap = (int)Math.Round(4 * s);
            int height = (int)Math.Round(44 * s);
            float total = 0;
            foreach (int w in TabWidths) total += w * s;
            float fit = Math.Min(1f, (width - 2 * pad - gap * (tabs.Count - 1)) / total);
            int x = pad;
            for (int i = 0; i < tabs.Count; i++)
            {
                int w = (int)(TabWidths[i] * s * fit);
                tabs[i].Button.SetBounds(x, strip.Height - height, w, height);
                x += w + gap;
            }
        }

        // Controls that start at the left margin widen with the window, and
        // ones further right, such as Browse and the version, keep to the right
        // edge. The title, inset for the crystal mark, stays put.
        static void Stretch(Control control, AnchorStyles edge)
        {
            if (control.Left == Pad)
            {
                control.Anchor = edge | AnchorStyles.Left | AnchorStyles.Right;
            }
            else if (control.Left > ContentWidth / 2)
            {
                control.Anchor = edge | AnchorStyles.Right;
            }
            else
            {
                control.Anchor = edge | AnchorStyles.Left;
            }
        }

        // Opens with two columns, sized to show every option, or one column on
        // a screen too narrow for two. On a screen too short, it opens at 90% of
        // the height and the options scroll. The window can be dragged wider,
        // narrower down to one column, or shorter down to the header, the
        // folder and seed, the tabs, a few rows and the footer.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int frameWidth = Width - ClientSize.Width;
            int frameHeight = Height - ClientSize.Height;
            float s = DeviceDpi / 96f;
            int narrow = (int)Math.Round((ContentWidth + 2 * Pad) * s) + frameWidth;
            if (Width > area.Width)
            {
                Width = narrow;
            }
            int shortest = (int)Math.Round((HeaderHeight + TopGap + 104 + CodeHeight + StripHeight + 160 + FooterHeight) * s)
                + frameHeight;
            MinimumSize = new Size(narrow, Math.Min(shortest, area.Height));
            MaximumSize = new Size((int)Math.Round(TwoColumnWidth * s * 1.4f) + frameWidth, SystemInformation.MaxWindowTrackSize.Height);
            loaded = true;
            LayOut();
            int fit = area.Height * 9 / 10;
            if (Height > fit)
            {
                Height = Math.Max(fit, MinimumSize.Height);
            }
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            using (Pen line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, 52 * s, ClientSize.Width, 52 * s);
                float footerTop = ClientSize.Height - FooterHeight * s;
                g.DrawLine(line, 0, footerTop, ClientSize.Width, footerTop);
            }
            // Crystal mark beside the title.
            using (Pen pen = new Pen(Theme.Accent, 1.8f * s))
            {
                pen.LineJoin = LineJoin.Round;
                PointF[] gem = { P(31, 16, s), P(38, 23, s), P(31, 36, s), P(24, 23, s) };
                g.DrawPolygon(pen, gem);
                g.DrawLine(pen, P(24, 23, s), P(38, 23, s));
                g.DrawLine(pen, P(31, 16, s), P(31, 36, s));
            }
        }

        static PointF P(float x, float y, float s)
        {
            return new PointF(x * s, y * s);
        }

        Label AddLabel(string text, int x, int y, int width, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, font.Height + 4);
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            target.Controls.Add(label);
            return label;
        }

        Label AddHeading(string text, int y)
        {
            return AddLabel(text.ToUpperInvariant(), Pad, y, ContentWidth, Theme.Ui(9f, FontStyle.Bold), Theme.Muted);
        }

        Button MakeButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Theme.ButtonBorder;
            button.FlatAppearance.MouseOverBackColor = Theme.ButtonBorder;
            button.BackColor = Theme.Button;
            button.ForeColor = Theme.Text;
            button.Cursor = Cursors.Hand;
            target.Controls.Add(button);
            return button;
        }

        // A text box inside a rounded card, so it matches the mockup's 44 px fields.
        Card AddInput(TextBox box, int x, int y, int width, Font font)
        {
            Card card = new Card();
            card.Location = new Point(x, y);
            card.Size = new Size(width, 44);
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Panel;
            box.ForeColor = Theme.Text;
            box.Font = font;
            box.Location = new Point(12, (44 - font.Height) / 2);
            box.Width = width - 24;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(box);
            target.Controls.Add(card);
            return card;
        }

        void BuildHeader()
        {
            AddLabel("FFR Randomizer", 48, 14, 300, Theme.Ui(12.5f, FontStyle.Bold), Theme.Text);
            Label version = AddLabel("v" + Game.AppVersion, 440, 17, 100, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            version.TextAlign = ContentAlignment.TopRight;
        }

        int BuildFolder(int y)
        {
            AddHeading("Game folder", y);
            AddInput(folderBox, Pad, y + 22, 424, Theme.Ui(9.5f, FontStyle.Regular));
            folderBox.ReadOnly = true;
            folderBox.TabStop = false;
            folderBox.AccessibleName = "Game folder";
            Button browse = MakeButton("Browse", 452, y + 22, 88, 44);
            browse.Click += delegate { BrowseForGame(); };
            folderStatus.Location = new Point(Pad, y + 72);
            folderStatus.Size = new Size(ContentWidth, 20);
            folderStatus.Font = Theme.Ui(9.5f, FontStyle.Regular);
            folderStatus.BackColor = Color.Transparent;
            target.Controls.Add(folderStatus);
            return y + 104;
        }

        int BuildSeed(int y)
        {
            AddHeading("Seed", y);
            AddInput(seedBox, Pad, y + 22, 412, new Font("Consolas", 12f, FontStyle.Regular));
            seedBox.AccessibleName = "Seed";
            seedBox.MaxLength = 40;
            seedBox.TextChanged += delegate { ClearSaved(); };
            Button randomButton = MakeButton("Random", 440, y + 22, 100, 44);
            randomButton.Click += delegate { seedBox.Text = NewSeed(); };
            AddLabel("Share the settings code below so others get the same game.", Pad, y + 72, ContentWidth,
                Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + 104;
        }

        const int CodeHeight = 104;

        // The settings code follows every change; Paste code loads one.
        int BuildCode(int y)
        {
            AddHeading("Settings code", y);
            AddInput(codeBox, Pad, y + 22, ContentWidth - 88 - 120 - 16, new Font("Consolas", 12f, FontStyle.Regular));
            codeBox.ReadOnly = true;
            codeBox.AccessibleName = "Settings code";
            Button copy = MakeButton("Copy", ContentWidth + Pad - 120 - 8 - 88, y + 22, 88, 44);
            copy.Click += delegate { CopyCode(); };
            Button paste = MakeButton("Paste code", ContentWidth + Pad - 120, y + 22, 120, 44);
            paste.Click += delegate { PasteCode(); };
            AddLabel("The seed and every option in one line. Paste a friend’s code to load their settings.", Pad, y + 72,
                ContentWidth, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + CodeHeight;
        }

        int BuildShuffles(int y)
        {
            Card card = AddToggleCard("Shuffles", y, 5);
            AddShuffleRow(card, 0, "Items", "Chests, enemy drops and steals. Key items stay put.", itemsToggle, null);
            AddShuffleRow(card, 1, "Shops", "Same categories and game prices, new stock.", shopsToggle, null);
            AddShuffleRow(card, 2, "Visions", "All 26 Vision rewards trade places.", visionsToggle, null);
            AddShuffleRow(card, 3, "Enemies", "Random encounters swap. Each spot keeps its stats and rewards.", enemiesToggle, null);
            AddShuffleRow(card, 4, "Bosses", "Boss fights swap. Each fight keeps its stats and rewards.", bossesToggle, null);
            return y + 22 + 5 * 64 + 20;
        }

        // One switch row: no toggle and a LATER badge means a placeholder for an
        // option that isn't built yet.
        class ToggleRow
        {
            public string Title, Detail, Badge;
            public ToggleSwitch Toggle;
        }

        static ToggleRow Row(string title, string detail, ToggleSwitch toggle, string badge = null)
        {
            return new ToggleRow { Title = title, Detail = detail, Toggle = toggle, Badge = badge };
        }

        int AddToggles(OptionTab tab, string heading, int y, params ToggleRow[] rows)
        {
            Card card = AddToggleCard(heading, y, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                ToggleSwitch toggle = rows[i].Toggle ?? new ToggleSwitch();
                if (rows[i].Toggle == null)
                {
                    toggle.On = false;
                    toggle.Enabled = false;
                    laterToggles.Add(toggle);
                }
                else
                {
                    tab.Toggles.Add(toggle);
                }
                AddShuffleRow(card, i, rows[i].Title, rows[i].Detail, toggle, rows[i].Badge);
            }
            return y + 22 + rows.Length * 64 + 20;
        }

        // A line about the tab, room for two lines when the window is narrow.
        int AddNote(string text, int y)
        {
            Label note = AddLabel(text, Pad, y, ContentWidth, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            note.Height = 38;
            return y + 44;
        }

        int BuildChaosWarning(int y)
        {
            Card card = new Card();
            card.Outline = ColorTranslator.FromHtml("#6b3a40");
            card.BackColor = ColorTranslator.FromHtml("#2a1a1d");
            card.Location = new Point(Pad, y);
            card.Size = new Size(ContentWidth, 44);
            Label text = new Label();
            text.Text = "These can make a run unwinnable or break the story. Create Randomizer File asks you to confirm when any is on.";
            text.Font = Theme.Ui(9.5f, FontStyle.Regular);
            text.ForeColor = ColorTranslator.FromHtml("#f2c4c4");
            text.BackColor = card.BackColor;
            text.Location = new Point(12, 4);
            text.Size = new Size(ContentWidth - 24, 36);
            text.TextAlign = ContentAlignment.MiddleLeft;
            text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(text);
            target.Controls.Add(card);
            return y + 56;
        }

        // Vision mixing needs the ability shuffle, character mixing needs moves
        // or finishers, and supports needs either ability or move shuffle.
        void UpdateOptions()
        {
            mixToggle.Enabled = abilitiesToggle.On;
            supportsToggle.Enabled = abilitiesToggle.On || movesToggle.On;
            mixCharactersToggle.Enabled = movesToggle.On || characterFinishersToggle.On;
            // Fully random stats replaces the budget shuffle.
            statsToggle.Enabled = !randomStatsToggle.On;
            UpdateCounts();
        }

        void UpdateCounts()
        {
            foreach (OptionTab tab in tabs)
            {
                int on = 0;
                foreach (ToggleSwitch toggle in tab.Toggles)
                {
                    if (toggle.On && toggle.Enabled)
                    {
                        on++;
                    }
                }
                tab.Button.Count = on > 0 ? on + " on" : "all off";
            }
        }

        // A rounded card of 64 px rows with lines between them, under a heading.
        Card AddToggleCard(string heading, int y, int rows)
        {
            AddHeading(heading, y);
            Card card = new Card();
            card.Radius = 10;
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, rows * 64);
            target.Controls.Add(card);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    float s = DeviceDpi / 96f;
                    for (int row = 1; row < rows; row++)
                    {
                        e.Graphics.DrawLine(pen, 0, row * 64 * s, card.Width - 1, row * 64 * s);
                    }
                }
            };
            return card;
        }

        // Badges sit after the title: NEW in the accent color, LATER greyed with
        // the title and switch. The title is measured at the screen's scale and
        // brought back to design units, since the form scales everything after.
        void AddShuffleRow(Card card, int index, string title, string detail, ToggleSwitch toggle, string badge)
        {
            bool later = badge == "LATER";
            int top = index * 64;
            Label name = new Label();
            name.Text = title;
            name.Font = Theme.Ui(11f, FontStyle.Bold);
            name.ForeColor = later ? Theme.Muted : Theme.Text;
            name.Location = new Point(14, top + 10);
            name.Size = new Size(400, 22);
            Label info = new Label();
            info.Text = detail;
            info.Font = Theme.Ui(9.5f, FontStyle.Regular);
            info.ForeColor = Theme.Muted;
            info.Location = new Point(14, top + 33);
            info.Size = new Size(420, 20);
            toggle.Location = new Point(ContentWidth - 52 - 14, top + 17);
            toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            name.Anchor = info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            toggle.AccessibleName = title;
            if (!later)
            {
                toggle.Changed += delegate { ClearSaved(); UpdateCounts(); };
            }
            if (badge != null)
            {
                float scale = DeviceDpi / 96f;
                Label tag = new Label();
                tag.Text = badge;
                tag.Font = Theme.Ui(7.5f, FontStyle.Bold);
                tag.ForeColor = later ? ColorTranslator.FromHtml("#c9cfda") : Theme.AccentText;
                tag.BackColor = later ? Theme.Border : Theme.Accent;
                tag.TextAlign = ContentAlignment.MiddleCenter;
                int titleWidth = (int)(TextRenderer.MeasureText(title, name.Font).Width / scale);
                int tagWidth = (int)(TextRenderer.MeasureText(badge, tag.Font).Width / scale) + 6;
                tag.SetBounds(14 + titleWidth + 4, top + 12, tagWidth, 18);
                tag.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                card.Controls.Add(tag);
                name.Width = titleWidth + 2;
                name.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            card.Controls.Add(name);
            card.Controls.Add(info);
            card.Controls.Add(toggle);
        }

        int BuildChoice(string heading, int y, string[] labels, RadioButton[] buttons, Label help)
        {
            AddHeading(heading, y);
            Card card = new Card();
            card.Radius = 10;
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, 48);
            target.Controls.Add(card);
            int width = (ContentWidth - 8 - 8) / 3;
            for (int i = 0; i < 3; i++)
            {
                RadioButton option = new RadioButton();
                option.Appearance = Appearance.Button;
                option.FlatStyle = FlatStyle.Flat;
                option.FlatAppearance.BorderSize = 0;
                option.FlatAppearance.CheckedBackColor = Theme.Accent;
                option.FlatAppearance.MouseOverBackColor = Theme.Button;
                option.TextAlign = ContentAlignment.MiddleCenter;
                option.Font = Theme.Ui(10.5f, FontStyle.Bold);
                option.Text = labels[i];
                option.AccessibleName = heading + ": " + labels[i];
                option.Location = new Point(4 + i * (width + 4), 4);
                option.Size = new Size(width, 40);
                option.Cursor = Cursors.Hand;
                option.CheckedChanged += delegate { UpdateChoices(); ClearSaved(); };
                buttons[i] = option;
                card.Controls.Add(option);
            }
            card.Resize += delegate { LayOutChoice(card, buttons); };
            help.Location = new Point(Pad, y + 76);
            help.Size = new Size(ContentWidth, 20);
            help.Font = Theme.Ui(9.5f, FontStyle.Regular);
            help.ForeColor = Theme.Muted;
            help.BackColor = Color.Transparent;
            target.Controls.Add(help);
            return y + 108;
        }

        // The three buttons share the card's width, with the gap they start with.
        static void LayOutChoice(Card card, RadioButton[] buttons)
        {
            if (buttons[2] == null)
            {
                return;
            }
            int gap = buttons[0].Top;
            int width = (card.ClientSize.Width - 4 * gap) / 3;
            for (int i = 0; i < 3; i++)
            {
                buttons[i].SetBounds(gap + i * (width + gap), gap, width, buttons[i].Height);
            }
        }

        // The apply status card, with a saved message laid over it while one is
        // showing, then the buttons.
        void BuildFooter()
        {
            statusCard.Location = new Point(Pad, fullHeight - 144);
            statusCard.Size = new Size(ContentWidth, 76);
            Label[] lines = { statusTitle, statusDetail, statusCheck };
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i].Location = new Point(12, 7 + i * 21);
                lines[i].Size = new Size(ContentWidth - 24, 20);
                lines[i].Font = Theme.Ui(i == 0 ? 10f : 9.5f, i == 0 ? FontStyle.Bold : FontStyle.Regular);
                lines[i].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                lines[i].AutoEllipsis = true;
                statusCard.Controls.Add(lines[i]);
            }
            Controls.Add(statusCard);

            savedCard.Outline = Theme.GoodBorder;
            savedCard.BackColor = Theme.GoodBack;
            savedCard.Location = new Point(Pad, fullHeight - 144);
            savedCard.Size = new Size(ContentWidth, 76);
            savedText.Location = new Point(12, 4);
            savedText.Size = new Size(ContentWidth - 24, 68);
            savedText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            savedText.TextAlign = ContentAlignment.MiddleLeft;
            savedText.Font = Theme.Ui(9.5f, FontStyle.Regular);
            savedText.BackColor = Theme.GoodBack;
            savedCard.Controls.Add(savedText);
            savedCard.Visible = false;
            Controls.Add(savedCard);
            savedCard.BringToFront();

            createButton.Text = "Create Randomizer File";
            createButton.Location = new Point(Pad, fullHeight - 60);
            createButton.Size = new Size(358, 48);
            createButton.FlatStyle = FlatStyle.Flat;
            createButton.FlatAppearance.BorderSize = 0;
            createButton.FlatAppearance.MouseOverBackColor = ColorTranslator.FromHtml("#7bbcec");
            createButton.BackColor = Theme.Accent;
            createButton.ForeColor = Theme.AccentText;
            createButton.Font = Theme.Ui(11.5f, FontStyle.Bold);
            createButton.Cursor = Cursors.Hand;
            createButton.Click += delegate { CreateFile(); };
            Controls.Add(createButton);
            AcceptButton = createButton;

            Button spoiler = MakeButton("Open spoiler log", 388, fullHeight - 60, 152, 48);
            spoiler.BackColor = Theme.Ground;
            spoiler.Click += delegate { OpenSpoiler(); };
        }

        void UpdateChoices()
        {
            int quantity = Picked(quantityButtons);
            int encounter = Picked(encounterButtons);
            foreach (RadioButton[] buttons in new[] { quantityButtons, encounterButtons })
            {
                foreach (RadioButton option in buttons)
                {
                    if (option != null)
                    {
                        option.BackColor = option.Checked ? Theme.Accent : Theme.Panel;
                        option.ForeColor = option.Checked ? Theme.AccentText : Theme.Text;
                    }
                }
            }
            quantityHelp.Text = quantity == 0
                ? "Each chest keeps its normal amount. Gear is always 1."
                : "Each chest gives 1 to " + quantityValues[quantity] + " of its item. Gear is always 1.";
            encounterHelp.Text = encounter == 0 ? "Random battles as often as usual."
                : encounter == 1 ? "Half as many random battles: twice the walk between them."
                : "Twice as many random battles: half the walk between them.";
        }

        static int Picked(RadioButton[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].Checked)
                {
                    return i;
                }
            }
            return 0;
        }

        string NewSeed()
        {
            return (random.Next(10000, 100000).ToString() + random.Next(100000, 1000000).ToString());
        }

        // Every change to the seed or an option comes through here, so the
        // settings code follows it.
        void ClearSaved()
        {
            savedCard.Visible = false;
            UpdateCode();
        }

        string CurrentCode()
        {
            ToggleSwitch[] toggles = SettingToggles();
            bool[] on = new bool[toggles.Length];
            for (int i = 0; i < toggles.Length; i++)
            {
                on[i] = toggles[i].On;
            }
            return SettingsCode.Encode(seedBox.Text.Trim(), on, Picked(encounterButtons), Picked(quantityButtons));
        }

        void UpdateCode()
        {
            if (!loadingState)
            {
                codeBox.Text = CurrentCode();
            }
        }

        void CopyCode()
        {
            try
            {
                Clipboard.SetText(codeBox.Text);
                ShowSaved("Settings code copied. Anyone who pastes it gets this seed and these options.", true);
            }
            catch (ExternalException)
            {
                ShowSaved("Windows wouldn’t let the app use the clipboard. Try Copy again.", false);
            }
        }

        void PasteCode()
        {
            string text = "";
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (ExternalException)
            {
            }
            string seed, error;
            bool[] on;
            int encounter, quantity;
            if (!SettingsCode.TryDecode(text, out seed, out on, out encounter, out quantity, out error))
            {
                ShowSaved(text.Trim() == "" ? "Copy a settings code first, then press Paste code." : error, false);
                return;
            }
            loadingState = true;
            ToggleSwitch[] toggles = SettingToggles();
            int count = 0;
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].On = on[i];
                count += on[i] ? 1 : 0;
            }
            encounterButtons[encounter].Checked = true;
            quantityButtons[quantity].Checked = true;
            seedBox.Text = seed;
            loadingState = false;
            UpdateOptions();
            UpdateChoices();
            UpdateCounts();
            UpdateCode();
            ShowSaved("Loaded " + codeBox.Text + ": seed and " + count + " options set. Press Create Randomizer File to save it.", true);
        }

        void ShowSaved(string message, bool good)
        {
            savedCard.Outline = good ? Theme.GoodBorder : Theme.BadBorder;
            savedCard.BackColor = savedText.BackColor = good ? Theme.GoodBack : Theme.BadBack;
            savedText.ForeColor = good ? ColorTranslator.FromHtml("#bfe9cf") : Theme.Bad;
            savedText.Text = message;
            savedCard.Visible = true;
            savedCard.Invalidate();
        }

        bool ModInstalled
        {
            get
            {
                return gameDir != "" && File.Exists(Path.Combine(gameDir, Game.Ue4ssPath)) &&
                    File.Exists(Path.Combine(Game.ModDir(gameDir), @"Scripts\main.lua"));
            }
        }

        void SetGameDir(string folder)
        {
            gameDir = folder == "" ? "" : Game.Normalize(folder);
            folderBox.Text = gameDir == "" ? "Not found. Use Browse to pick the game folder." : gameDir;
            if (gameDir == "")
            {
                folderStatus.Text = "Pick the FINAL FANTASY RESONANCE folder.";
                folderStatus.ForeColor = Theme.Bad;
            }
            else if (!File.Exists(Path.Combine(gameDir, Game.Ue4ssPath)))
            {
                folderStatus.Text = "UE4SS is not installed in this folder.";
                folderStatus.ForeColor = Theme.Bad;
            }
            else if (!ModInstalled)
            {
                folderStatus.Text = "UE4SS found, but the randomizer mod is not installed.";
                folderStatus.ForeColor = Theme.Bad;
            }
            else
            {
                string version = Game.ModVersion(gameDir);
                int compared = Game.CompareVersions(version, Game.AppVersion);
                if (version == null)
                {
                    folderStatus.Text = "\u2713  UE4SS and the randomizer mod are installed";
                    folderStatus.ForeColor = Theme.Good;
                }
                else if (compared < 0)
                {
                    folderStatus.Text = "!  Mod " + version + " installed, this app is " + Game.AppVersion + ". Run the " +
                        Game.AppVersion + " Setup, or some options won\u2019t apply.";
                    folderStatus.ForeColor = Theme.Warn;
                }
                else if (compared > 0)
                {
                    folderStatus.Text = "!  Mod " + version + " installed, this app is " + Game.AppVersion +
                        ". Use the app that came with it.";
                    folderStatus.ForeColor = Theme.Warn;
                }
                else
                {
                    folderStatus.Text = "\u2713  UE4SS and randomizer mod " + version + " installed";
                    folderStatus.ForeColor = Theme.Good;
                }
            }
            folderStatus.AutoEllipsis = true;
            RefreshStatus();
        }

        // What the mod's Status.txt says about the last game start, against
        // the Randomizer File on disk.
        void RefreshStatus()
        {
            if (!ModInstalled)
            {
                SetStatus(0, "No status from the game", "Install the mod and pick the game folder to see what the game applied.", null);
                return;
            }
            Dictionary<string, string> status = Game.ReadValues(Game.StatusFile(gameDir));
            string settingsPath = Game.SettingsFile(gameDir);
            // A saved message lies over the status card. Once the game writes a
            // new status, the message has done its job, so the status shows.
            DateTime written = File.Exists(Game.StatusFile(gameDir)) ? File.GetLastWriteTime(Game.StatusFile(gameDir)) : DateTime.MinValue;
            if (written != statusWritten)
            {
                statusWritten = written;
                savedCard.Visible = false;
            }
            if (status.Count == 0)
            {
                if (Game.CompareVersions(Game.ModVersion(gameDir) ?? "0.0.0", "0.7.0") < 0)
                {
                    SetStatus(0, "No status from the game", "Mod 0.7.0 or newer reports here after each game start.", null);
                }
                else
                {
                    SetStatus(0, "Not applied yet", "Start the game to apply the Randomizer File. The seed check appears here once it has.", null);
                }
                return;
            }
            string state = Value(status, "state"), code = Value(status, "code"), seed = Value(status, "seed");
            string when = FriendlyTime(Value(status, "applied_at") != "" ? Value(status, "applied_at") : Value(status, "started"));
            string fileCode = File.Exists(settingsPath) ? SettingsCode.FromValues(Game.ReadSettings(gameDir)) : null;
            if (fileCode != code)
            {
                bool fileNewer = fileCode != null &&
                    File.GetLastWriteTime(settingsPath) > File.GetLastWriteTime(Game.StatusFile(gameDir));
                if (fileNewer && !GameRunning())
                {
                    SetStatus(0, "Not applied yet", "Start the game to apply this file. Last game start: seed " + seed + ", " + when + ".", null);
                }
                else if (fileNewer)
                {
                    SetStatus(1, "!  The game is still on the old file",
                        "It last applied seed " + seed + " " + when + ". Restart the game to use this file.", null);
                }
                else
                {
                    SetStatus(1, "!  The game read this file differently",
                        "It applied " + code + " " + when + ". Check the file\u2019s values, or create it again.", null);
                }
                return;
            }
            int errors, changes;
            int.TryParse(Value(status, "errors"), out errors);
            int.TryParse(Value(status, "changes"), out changes);
            string check = Value(status, "check") == "" ? null : "Seed check   " + Value(status, "check");
            if (state == "error")
            {
                SetStatus(2, "!  Not applied: the mod hit an error", Value(status, "message").TrimEnd('.') + ". Please report it with the UE4SS log.", null);
            }
            else if (state != "applied")
            {
                SetStatus(0, "Waiting for the game", "The game started " + when + " and hasn\u2019t loaded its tables yet.", null);
            }
            else if (errors > 0)
            {
                SetStatus(2, "!  Applied with " + errors + (errors == 1 ? " error: " : " errors: ") + when,
                    changes.ToString("N0") + " changes applied. Some options may not work. The UE4SS log has the Failed on lines.", check);
            }
            else
            {
                SetStatus(3, "\u2713  Applied in game: " + when + ", mod " + Value(status, "version"),
                    "This file\u2019s settings. " + changes.ToString("N0") + " changes applied, no errors.", check);
            }
        }

        static string Value(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }

        static bool GameRunning()
        {
            Process[] found = Process.GetProcessesByName("FFRS-Win64-Shipping");
            foreach (Process process in found)
            {
                process.Dispose();
            }
            return found.Length > 0;
        }

        // "today 3:40 PM", "yesterday 3:40 PM" or "Nov 2, 3:40 PM" from the
        // mod's local "2026-11-03 15:40".
        static string FriendlyTime(string text)
        {
            DateTime time;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out time))
            {
                return text == "" ? "at an unknown time" : text;
            }
            string clock = time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            if (time.Date == DateTime.Today)
            {
                return "today " + clock;
            }
            if (time.Date == DateTime.Today.AddDays(-1))
            {
                return "yesterday " + clock;
            }
            return time.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture) + ", " + clock;
        }

        // kind: 0 neutral, 1 warning, 2 problem, 3 good.
        void SetStatus(int kind, string title, string detail, string check)
        {
            Color[] back = { Theme.Panel, Theme.WarnBack, Theme.BadBack, Theme.GoodBack };
            Color[] border = { Theme.Border, Theme.WarnBorder, Theme.BadBorder, Theme.GoodBorder };
            Color[] text = { Theme.Text, Theme.Warn, Theme.Bad, ColorTranslator.FromHtml("#bfe9cf") };
            statusCard.BackColor = back[kind];
            statusCard.Outline = border[kind];
            foreach (Label label in new[] { statusTitle, statusDetail, statusCheck })
            {
                label.BackColor = back[kind];
            }
            statusTitle.ForeColor = text[kind];
            statusDetail.ForeColor = kind == 0 ? Theme.Muted : text[kind];
            statusCheck.ForeColor = Theme.Text;
            statusTitle.Text = title;
            statusDetail.Text = detail;
            statusCheck.Text = check ?? "";
            statusCard.Invalidate();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (loaded && gameDir != "")
            {
                SetGameDir(gameDir);
            }
        }

        // Starts from the current Randomizer File, so reopening the GUI shows the
        // playthrough's real seed and options.
        void LoadState()
        {
            string saved = GuiConfig.LoadGameDir();
            SetGameDir(saved != "" && Directory.Exists(saved) ? saved : Game.Detect());

            Dictionary<string, string> values = ModInstalled ? Game.ReadSettings(gameDir) : new Dictionary<string, string>();
            string seed;
            seedBox.Text = values.TryGetValue("seed", out seed) && seed != "" ? seed : NewSeed();
            // Items, shops and Visions came first and default to on; the rest to off.
            ToggleSwitch[] toggles = SettingToggles();
            string[] keys = AllToggleKeys();
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].On = Flag(values, keys[i], i < 3);
            }
            UpdateOptions();
            PickChoice(values, "chest_quantity", quantityValues, quantityButtons);
            PickChoice(values, "encounter_rate", encounterValues, encounterButtons);
            UpdateChoices();
            ClearSaved();
        }

        static string[] AllToggleKeys()
        {
            List<string> keys = new List<string>(Game.ToggleKeys);
            keys.AddRange(Game.ChaosKeys);
            return keys.ToArray();
        }

        static void PickChoice(Dictionary<string, string> values, string key, string[] choices, RadioButton[] buttons)
        {
            string value;
            int index = values.TryGetValue(key, out value) ? Array.IndexOf(choices, value.ToLowerInvariant()) : 0;
            buttons[index < 0 ? 0 : index].Checked = true;
        }

        static bool Flag(Dictionary<string, string> values, string key, bool fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return fallback;
            }
            value = value.ToLowerInvariant();
            return value != "off" && value != "false" && value != "0";
        }

        void BrowseForGame()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Pick the FINAL FANTASY RESONANCE folder";
                if (gameDir != "")
                {
                    dialog.SelectedPath = gameDir;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetGameDir(dialog.SelectedPath);
                    GuiConfig.SaveGameDir(gameDir);
                    LoadState();
                }
            }
        }

        void CreateFile()
        {
            if (!ModInstalled)
            {
                ShowSaved("Install UE4SS and the randomizer mod first, then pick the game folder.", false);
                return;
            }
            List<string> chaos = new List<string>();
            foreach (ToggleSwitch toggle in tabs[2].Toggles)
            {
                if (toggle.On && toggle.Enabled)
                {
                    chaos.Add(toggle.AccessibleName);
                }
            }
            if (chaos.Count > 0 && MessageBox.Show(this,
                    "These options can make the game unwinnable or stop the story. There's no fixing that mid-run, " +
                    "only a new Randomizer File and a New Game.\n\nOn in this file: " + string.Join(", ", chaos.ToArray()),
                    "Create a We are Chaos run?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            string seed = seedBox.Text.Trim();
            if (seed == "")
            {
                seed = NewSeed();
                seedBox.Text = seed;
            }
            try
            {
                // The seed, the gear and enemy toggles, encounter rate, the Chaos
                // toggles and chest quantity, in the template's order.
                ToggleSwitch[] toggles = SettingToggles();
                List<string> values = new List<string> { seed };
                for (int i = 0; i < Game.ToggleKeys.Length; i++)
                {
                    values.Add(Game.OnOff(toggles[i].On));
                }
                values.Add(encounterValues[Picked(encounterButtons)]);
                for (int i = Game.ToggleKeys.Length; i < toggles.Length; i++)
                {
                    values.Add(Game.OnOff(toggles[i].On));
                }
                values.Add(quantityValues[Picked(quantityButtons)]);
                Game.WriteSettings(gameDir, values.ToArray());
                GuiConfig.SaveGameDir(gameDir);
                RefreshStatus();
                ShowSaved("Randomizer File saved. Start the game and begin a New Game. Keep this file for the whole playthrough.", true);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                ShowSaved("Could not save the Randomizer File: " + ex.Message, false);
            }
        }

        void OpenSpoiler()
        {
            string seed = seedBox.Text.Trim();
            string path = gameDir == "" || seed == "" ? "" : Game.SpoilerFile(gameDir, seed);
            if (path == "" || !File.Exists(path))
            {
                ShowSaved("No spoiler log for this seed yet. The game writes it the first time it starts with this seed.", false);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
