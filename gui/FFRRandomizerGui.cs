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
[assembly: System.Reflection.AssemblyVersion("0.1.1.0")]

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
            "# default, 9 or 50. Weapons, armor and accessories always stay at 1.\n" +
            "chest_quantity={4}\n";

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

        public static void WriteSettings(string gameDir, string seed, bool items, bool shops, bool visions, string quantity)
        {
            string text = string.Format(Template, seed, OnOff(items), OnOff(shops), OnOff(visions), quantity);
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
            using (Brush brush = new SolidBrush(isOn ? Theme.Accent : Theme.ButtonBorder))
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
            using (Brush brush = new SolidBrush(Theme.Knob))
            {
                g.FillEllipse(brush, x, track.Top + 3, knob, knob);
            }
        }
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

    class MainForm : Form
    {
        const int Pad = 20;
        const int ContentWidth = 520;

        readonly TextBox folderBox = new TextBox();
        readonly Label folderStatus = new Label();
        readonly TextBox seedBox = new TextBox();
        readonly ToggleSwitch itemsToggle = new ToggleSwitch();
        readonly ToggleSwitch shopsToggle = new ToggleSwitch();
        readonly ToggleSwitch visionsToggle = new ToggleSwitch();
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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Ground;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f, FontStyle.Regular);
            ClientSize = new Size(560, 808);
            using (Stream icon = typeof(MainForm).Assembly.GetManifestResourceStream("FFRRandomizerGui.icon.ico"))
            {
                if (icon != null)
                {
                    Icon = new Icon(icon);
                }
            }

            BuildHeader();
            int y = 72;
            y = BuildFolder(y);
            y = BuildSeed(y);
            y = BuildShuffles(y);
            y = BuildQuantity(y);
            BuildComingLater(y);
            BuildFooter();

            LoadState();
            ActiveControl = createButton;
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
                g.DrawLine(line, 0, 684 * s, ClientSize.Width, 684 * s);
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
            Controls.Add(label);
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
            Controls.Add(button);
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
            card.Controls.Add(box);
            Controls.Add(card);
            return card;
        }

        void BuildHeader()
        {
            AddLabel("FFR Randomizer", 48, 14, 300, Theme.Ui(12.5f, FontStyle.Bold), Theme.Text);
            Label version = AddLabel("v0.1.1", 440, 17, 100, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
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
            Controls.Add(folderStatus);
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
            AddHeading("Shuffles", y);
            Card card = new Card();
            card.Radius = 10;
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, 3 * 64);
            Controls.Add(card);
            AddShuffleRow(card, 0, "Items", "Chests, enemy drops and steals. Key items stay put.", itemsToggle);
            AddShuffleRow(card, 1, "Shops", "Same categories and game prices, new stock.", shopsToggle);
            AddShuffleRow(card, 2, "Visions", "All 26 Vision rewards trade places.", visionsToggle);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    float s = DeviceDpi / 96f;
                    e.Graphics.DrawLine(pen, 0, 64 * s, card.Width - 1, 64 * s);
                    e.Graphics.DrawLine(pen, 0, 128 * s, card.Width - 1, 128 * s);
                }
            };
            return y + 22 + 3 * 64 + 20;
        }

        void AddShuffleRow(Card card, int index, string title, string detail, ToggleSwitch toggle)
        {
            int top = index * 64;
            Label name = new Label();
            name.Text = title;
            name.Font = Theme.Ui(11f, FontStyle.Bold);
            name.ForeColor = Theme.Text;
            name.Location = new Point(14, top + 10);
            name.Size = new Size(400, 22);
            Label info = new Label();
            info.Text = detail;
            info.Font = Theme.Ui(9.5f, FontStyle.Regular);
            info.ForeColor = Theme.Muted;
            info.Location = new Point(14, top + 33);
            info.Size = new Size(420, 20);
            toggle.Location = new Point(ContentWidth - 52 - 14, top + 17);
            toggle.AccessibleName = title;
            toggle.Changed += delegate { ClearSaved(); };
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
            Controls.Add(card);
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
            quantityHelp.Location = new Point(Pad, y + 76);
            quantityHelp.Size = new Size(ContentWidth, 20);
            quantityHelp.Font = Theme.Ui(9.5f, FontStyle.Regular);
            quantityHelp.ForeColor = Theme.Muted;
            quantityHelp.BackColor = Color.Transparent;
            Controls.Add(quantityHelp);
            return y + 108;
        }

        void BuildComingLater(int y)
        {
            AddHeading("Coming later", y);
            AddLabel("Enemies, bosses, EXP and Gil balance, gear options", Pad, y + 22, ContentWidth,
                Theme.Ui(10f, FontStyle.Regular), Theme.Muted);
        }

        void BuildFooter()
        {
            savedCard.Outline = Theme.GoodBorder;
            savedCard.BackColor = Theme.GoodBack;
            savedCard.Location = new Point(Pad, 696);
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
            createButton.Location = new Point(Pad, 748);
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

            Button spoiler = MakeButton("Open spoiler log", 388, 748, 152, 48);
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
            itemsToggle.On = Flag(values, "items");
            shopsToggle.On = Flag(values, "shops");
            visionsToggle.On = Flag(values, "visions");
            string quantity;
            int index = values.TryGetValue("chest_quantity", out quantity) ? Array.IndexOf(quantityValues, quantity.ToLowerInvariant()) : 0;
            quantityButtons[index < 0 ? 0 : index].Checked = true;
            UpdateQuantityLook();
            ClearSaved();
        }

        static bool Flag(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return true;
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
            string seed = seedBox.Text.Trim();
            if (seed == "")
            {
                seed = NewSeed();
                seedBox.Text = seed;
            }
            try
            {
                Game.WriteSettings(gameDir, seed, itemsToggle.On, shopsToggle.On, visionsToggle.On, SelectedQuantity());
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
