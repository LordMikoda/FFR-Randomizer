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
[assembly: System.Reflection.AssemblyVersion("0.5.0.0")]

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
        public static readonly Color Knob = ColorTranslator.FromHtml("#f4f6f9");

        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    // The game, mod and Randomizer File locations, and the file's format.
    static class Game
    {
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
            "# default, 9 or 50. Weapons, armor and accessories always stay at 1.\n" +
            "chest_quantity={14}\n";

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

        public static Dictionary<string, string> ReadSettings(string gameDir)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            string path = SettingsFile(gameDir);
            if (!File.Exists(path))
            {
                return values;
            }
            foreach (string line in File.ReadAllLines(path))
            {
                Match match = Regex.Match(line, @"^\s*([A-Za-z0-9_]+)\s*=(.*)$");
                if (match.Success)
                {
                    values[match.Groups[1].Value.ToLowerInvariant()] = match.Groups[2].Value.Trim();
                }
            }
            return values;
        }

        public static void WriteSettings(string gameDir, string seed, bool items, bool shops, bool visions, bool enemies, bool bosses,
            bool abilities, bool mixAbilities, bool finishers, bool supports,
            bool moves, bool characterFinishers, bool mixCharacters, bool stats, string quantity)
        {
            string text = string.Format(Template, seed, OnOff(items), OnOff(shops), OnOff(visions), OnOff(enemies), OnOff(bosses),
                OnOff(abilities), OnOff(mixAbilities), OnOff(finishers), OnOff(supports),
                OnOff(moves), OnOff(characterFinishers), OnOff(mixCharacters), OnOff(stats), quantity);
            File.WriteAllText(SettingsFile(gameDir), text, new UTF8Encoding(false));
        }

        static string OnOff(bool value)
        {
            return value ? "on" : "off";
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
        const int FooterHeight = 124;
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
        readonly RadioButton[] quantityButtons = new RadioButton[3];
        readonly string[] quantityValues = { "default", "9", "50" };
        readonly Label quantityHelp = new Label();
        readonly Card savedCard = new Card();
        readonly Label savedText = new Label();
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
            BuildTabs();
            int content = 0;
            foreach (OptionTab tab in tabs)
            {
                content = Math.Max(content, tab.ContentHeight(2));
            }
            fullHeight = HeaderHeight + 1 + TopGap + Math.Max(folderSection.Height, seedSection.Height) + StripHeight
                + ContentTop + content + ContentBottom + FooterHeight;

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

        // The three tabs. Options marked LATER are shown but can't be turned on yet.
        void BuildTabs()
        {
            OptionTab light = AddTab("We Are the Light");
            light.Wide.Add(BuildSection(y => AddNote("The randomizer as intended: everything shuffles, every character and " +
                "Vision stays playable, and the story can always be finished.", y)));
            light.Left.Add(BuildSection(BuildShuffles));
            light.Left.Add(BuildSection(BuildQuantity));
            light.Right.Add(BuildSection(y => AddToggles(light, "Visions", y,
                Row("Vision abilities", "Abilities and passives swap among Physical or Magic Visions.", abilitiesToggle),
                Row("Supports stay supports", "Fina, Leah, Ayaka and Y’shtola keep 5 or more healing abilities.", supportsToggle),
                Row("Vision finishers", "The 26 Vision finishers trade places.", finishersToggle))));
            light.Right.Add(BuildSection(y => AddToggles(light, "Characters", y,
                Row("Character moves", "Level-up moves swap among characters of the same attack type.", movesToggle, "NEW"),
                Row("Character finishers", "Finishers swap among characters of the same attack type.", characterFinishersToggle, "NEW"))));
            light.Toggles.AddRange(new[] { itemsToggle, shopsToggle, visionsToggle, enemiesToggle, bossesToggle });

            OptionTab become = AddTab("We must BECOME Chaos");
            become.Wide.Add(BuildSection(y => AddNote("Wilder, still finishable. Builds stop making sense, but nothing is " +
                "taken away for good.", y)));
            become.Left.Add(BuildSection(y => AddToggles(become, "Visions and characters", y,
                Row("Mix Physical and Magic", "Vision abilities come from all 26 Visions.", mixToggle),
                Row("Mix character moves", "Moves and finishers swap across Physical and Magic characters.", mixCharactersToggle, "NEW"),
                Row("Random character stats", "Five main stats trade growth curves per character. HP and MP stay.", statsToggle, "NEW"))));
            become.Left.Add(BuildSection(y => AddToggles(become, "Gear", y,
                Row("Gear stats", "Weapon and armor stats shuffle within each gear type.", null, "LATER"),
                Row("Gear passives", "Passives on gear come from other gear.", null, "LATER"))));
            become.Right.Add(BuildSection(y => AddToggles(become, "Enemies", y,
                Row("Enemy weaknesses", "Each enemy gets new elemental weaknesses.", null, "LATER"),
                Row("Status resistances", "Enemies resist different ailments.", null, "LATER"),
                Row("Enemy passives", "Enemy passives come from other enemies.", null, "LATER"),
                Row("Encounter rate", "Normal, half or double random battles.", null, "LATER"))));

            OptionTab chaos = AddTab("We are Chaos");
            chaos.Wide.Add(BuildSection(y => AddNote("No guard rails. All off by default.", y)));
            chaos.Wide.Add(BuildSection(BuildChaosWarning));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Characters and gear", y,
                Row("Fully random stats", "Any character can end up with any stats, no budget.", null, "LATER"),
                Row("Equip anything", "Every character can wear every weapon and armor type.", null, "LATER"))));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Enemies", y,
                Row("Enemies at full strength", "Swapped enemies and bosses keep their own stats.", null, "LATER"))));
            chaos.Right.Add(BuildSection(y => AddToggles(chaos, "Items and shops", y,
                Row("Shops sell anything", "Any item can turn up in any shop, at its game price.", null, "LATER"),
                Row("Story items in the shuffle", "Key and quest items move too. Can softlock the story.", null, "LATER"))));

            abilitiesToggle.Changed += delegate { UpdateOptions(); };
            movesToggle.Changed += delegate { UpdateOptions(); };
            characterFinishersToggle.Changed += delegate { UpdateOptions(); };
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
            int shortest = (int)Math.Round((HeaderHeight + TopGap + 104 + StripHeight + 160 + FooterHeight) * s) + frameHeight;
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
            Label version = AddLabel("v0.5.0", 440, 17, 100, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
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
            AddLabel("Share the seed and settings so others get the same game.", Pad, y + 72, ContentWidth,
                Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + 104;
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

        int BuildQuantity(int y)
        {
            AddHeading("Chest quantity", y);
            Card card = new Card();
            card.Radius = 10;
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, 48);
            target.Controls.Add(card);
            string[] labels = { "Default", "Up to 9", "Up to 50" };
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
                option.Location = new Point(4 + i * (width + 4), 4);
                option.Size = new Size(width, 40);
                option.Cursor = Cursors.Hand;
                option.CheckedChanged += delegate { UpdateQuantityLook(); ClearSaved(); };
                quantityButtons[i] = option;
                card.Controls.Add(option);
            }
            card.Resize += delegate { LayOutQuantity(card); };
            quantityHelp.Location = new Point(Pad, y + 76);
            quantityHelp.Size = new Size(ContentWidth, 20);
            quantityHelp.Font = Theme.Ui(9.5f, FontStyle.Regular);
            quantityHelp.ForeColor = Theme.Muted;
            quantityHelp.BackColor = Color.Transparent;
            target.Controls.Add(quantityHelp);
            return y + 108;
        }

        // The three buttons share the card's width, with the gap they start with.
        void LayOutQuantity(Card card)
        {
            if (quantityButtons[2] == null)
            {
                return;
            }
            int gap = quantityButtons[0].Top;
            int width = (card.ClientSize.Width - 4 * gap) / 3;
            for (int i = 0; i < 3; i++)
            {
                quantityButtons[i].SetBounds(gap + i * (width + gap), gap, width, quantityButtons[i].Height);
            }
        }

        void BuildFooter()
        {
            savedCard.Outline = Theme.GoodBorder;
            savedCard.BackColor = Theme.GoodBack;
            savedCard.Location = new Point(Pad, fullHeight - 112);
            savedCard.Size = new Size(ContentWidth, 44);
            savedText.Location = new Point(12, 4);
            savedText.Size = new Size(ContentWidth - 24, 36);
            savedText.TextAlign = ContentAlignment.MiddleLeft;
            savedText.Font = Theme.Ui(9.5f, FontStyle.Regular);
            savedText.BackColor = Theme.GoodBack;
            savedCard.Controls.Add(savedText);
            savedCard.Visible = false;
            Controls.Add(savedCard);

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

        void UpdateQuantityLook()
        {
            int picked = 0;
            for (int i = 0; i < 3; i++)
            {
                RadioButton option = quantityButtons[i];
                option.BackColor = option.Checked ? Theme.Accent : Theme.Panel;
                option.ForeColor = option.Checked ? Theme.AccentText : Theme.Text;
                if (option.Checked)
                {
                    picked = i;
                }
            }
            quantityHelp.Text = picked == 0
                ? "Each chest keeps its normal amount. Gear is always 1."
                : "Each chest gives 1 to " + quantityValues[picked] + " of its item. Gear is always 1.";
        }

        string NewSeed()
        {
            return (random.Next(10000, 100000).ToString() + random.Next(100000, 1000000).ToString());
        }

        void ClearSaved()
        {
            savedCard.Visible = false;
        }

        void ShowSaved(string message, bool good)
        {
            savedCard.Outline = good ? Theme.GoodBorder : Theme.Bad;
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
                folderStatus.Text = "\u2713  UE4SS and the randomizer mod are installed";
                folderStatus.ForeColor = Theme.Good;
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
            itemsToggle.On = Flag(values, "items", true);
            shopsToggle.On = Flag(values, "shops", true);
            visionsToggle.On = Flag(values, "visions", true);
            enemiesToggle.On = Flag(values, "enemies", false);
            bossesToggle.On = Flag(values, "bosses", false);
            abilitiesToggle.On = Flag(values, "abilities", false);
            mixToggle.On = Flag(values, "mix_physical_magic", false);
            supportsToggle.On = Flag(values, "supports", false);
            finishersToggle.On = Flag(values, "finishers", false);
            movesToggle.On = Flag(values, "character_moves", false);
            characterFinishersToggle.On = Flag(values, "character_finishers", false);
            mixCharactersToggle.On = Flag(values, "mix_character_moves", false);
            statsToggle.On = Flag(values, "character_stats", false);
            UpdateOptions();
            string quantity;
            int index = values.TryGetValue("chest_quantity", out quantity) ? Array.IndexOf(quantityValues, quantity.ToLowerInvariant()) : 0;
            quantityButtons[index < 0 ? 0 : index].Checked = true;
            UpdateQuantityLook();
            ClearSaved();
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

        string SelectedQuantity()
        {
            for (int i = 0; i < 3; i++)
            {
                if (quantityButtons[i].Checked)
                {
                    return quantityValues[i];
                }
            }
            return "default";
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
                Game.WriteSettings(gameDir, seed, itemsToggle.On, shopsToggle.On, visionsToggle.On, enemiesToggle.On, bossesToggle.On,
                    abilitiesToggle.On, mixToggle.On, finishersToggle.On, supportsToggle.On,
                    movesToggle.On, characterFinishersToggle.On, mixCharactersToggle.On, statsToggle.On, SelectedQuantity());
                GuiConfig.SaveGameDir(gameDir);
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
