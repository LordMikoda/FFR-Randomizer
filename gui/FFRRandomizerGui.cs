// FFR Randomizer GUI: writes the Randomizer File (Randomizer.txt) that the
// UE4SS mod reads when the game starts. Built with the C# 5 compiler that
// ships with .NET Framework 4.8, so it needs nothing installed. See build.cmd.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("FFR Randomizer")]
[assembly: System.Reflection.AssemblyProduct("FFR Randomizer")]
[assembly: System.Reflection.AssemblyVersion("1.0.4.0")]

namespace FFRRandomizerGui
{
    // Window geometry shared by the panels and the form, in design pixels.
    static class MainFormSizes
    {
        // From a panel's edge to its window's edge.
        public const int WinInset = 20;
        // Below each window.
        public const int Gap = 14;
    }

    // Colors for the menu look: blue gradient windows with a light bevel on a
    // black ground, white text with a hard shadow and cyan labels.
    static class Theme
    {
        public static readonly Color Ground = ColorTranslator.FromHtml("#05060c");
        public static readonly Color Panel = ColorTranslator.FromHtml("#22379a");
        public static readonly Color Border = ColorTranslator.FromHtml("#3a4ea8");
        public static readonly Color Text = ColorTranslator.FromHtml("#ffffff");
        public static readonly Color Muted = ColorTranslator.FromHtml("#cdd5ee");
        public static readonly Color Dim = ColorTranslator.FromHtml("#7480ab");
        public static readonly Color Accent = ColorTranslator.FromHtml("#74d9ff");
        public static readonly Color AccentText = ColorTranslator.FromHtml("#0b1220");
        public static readonly Color Shadow = ColorTranslator.FromHtml("#0b0f2a");
        public static readonly Color ShadowSoft = ColorTranslator.FromHtml("#1a2154");
        public static readonly Color Good = ColorTranslator.FromHtml("#9cf2b6");
        public static readonly Color Bad = ColorTranslator.FromHtml("#ff9d9d");
        public static readonly Color Warn = ColorTranslator.FromHtml("#ffd27a");
        public static readonly Color Field = ColorTranslator.FromHtml("#07103f");
        public static readonly Color Bevel = ColorTranslator.FromHtml("#e3e6ef");
        public static readonly Color BevelInner = ColorTranslator.FromHtml("#5c6488");
        public static readonly Color[] Window = {
            ColorTranslator.FromHtml("#4a68cf"), ColorTranslator.FromHtml("#2a43a3"),
            ColorTranslator.FromHtml("#14227a"), ColorTranslator.FromHtml("#0a1356") };
        public static readonly Color[] WarningWindow = {
            ColorTranslator.FromHtml("#9a3a52"), ColorTranslator.FromHtml("#6e2238"),
            ColorTranslator.FromHtml("#481427"), ColorTranslator.FromHtml("#2c0a18") };
        public static readonly Color[] ButtonFace = {
            ColorTranslator.FromHtml("#5272d6"), ColorTranslator.FromHtml("#2b45ad"),
            ColorTranslator.FromHtml("#22379a"), ColorTranslator.FromHtml("#132075") };

        // Outfit, falling back to Segoe UI if the embedded font didn't load.
        public static Font Ui(float size, FontStyle style)
        {
            if (FontLib.Sans != null && FontLib.Sans.IsStyleAvailable(style))
            {
                return new Font(FontLib.Sans, size, style, GraphicsUnit.Point);
            }
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        // JetBrains Mono for the seed and codes, falling back to Consolas.
        public static Font Mono(float size)
        {
            if (FontLib.Mono != null)
            {
                return new Font(FontLib.Mono, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    // The two open-licensed fonts (SIL OFL 1.1, licenses in art/fonts) come
    // embedded in the exe. AddFontMemResourceEx makes them visible to GDI too,
    // which TextRenderer and text boxes use; the collection alone is GDI+ only.
    static class FontLib
    {
        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);

        static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        public static readonly FontFamily Sans;
        public static readonly FontFamily Mono;

        static FontLib()
        {
            try
            {
                Load("Outfit-Regular.ttf");
                Load("Outfit-Bold.ttf");
                Load("JetBrainsMono-Regular.ttf");
                foreach (FontFamily family in fonts.Families)
                {
                    if (family.Name == "Outfit")
                    {
                        Sans = family;
                    }
                    else if (family.Name == "JetBrains Mono")
                    {
                        Mono = family;
                    }
                }
            }
            catch (Exception)
            {
                Sans = null;
                Mono = null;
            }
        }

        // The memory stays allocated for the life of the app, as both font
        // APIs require.
        static void Load(string name)
        {
            using (Stream stream = typeof(FontLib).Assembly.GetManifestResourceStream("FFRRandomizerGui." + name))
            {
                if (stream == null)
                {
                    return;
                }
                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int got = stream.Read(data, read, data.Length - read);
                    if (got <= 0)
                    {
                        break;
                    }
                    read += got;
                }
                IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                fonts.AddMemoryFont(memory, data.Length);
                uint count = 0;
                AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
            }
        }
    }

    // The game, mod and Randomizer File locations, and the file's format.
    static class Game
    {
        // Kept in step with MOD_VERSION in the mod's main.lua.
        public const string AppVersion = "1.0.4";
        // The oldest mod this app's Randomizer File fully works with. The
        // portable app can update without Setup while the mod is at least
        // this; raise it when the file format or the mod's options change.
        public const string MinModVersion = "1.0.0";
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
            "chest_quantity={25}\n" +
            "# Rewards from every battle: 0.25 to 10 in steps of 0.25. 1 is the game's own.\n" +
            "exp_rate={26}\n" +
            "gil_rate={27}\n" +
            "mrp_rate={28}\n" +
            "# Music: off, shuffle (battle and field tracks each swap among themselves) or mixed (one pool)\n" +
            "music={29}\n";

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

    // Everything a settings code holds.
    class RunOptions
    {
        public string Seed = "";
        public bool[] Toggles = new bool[SettingsCode.ToggleCount];
        public int Encounter, Quantity;
        // Kept in the code for trapped chests, which aren't built; always 0.
        public int Traps;
        // EXP, Gil and MRP as quarter steps: 4 is 1x, 1 is 0.25x, 40 is 10x.
        public int[] Rates = { 4, 4, 4 };
        // 0 off, 1 shuffle, 2 mixed.
        public int Music;
    }

    // The seed and every option in one line, such as FFR2-0631G07ZQZ-49353958654:
    // FFR and the format number, the options as bits in 10 Crockford base32
    // characters, then the seed as typed. Must stay in step with settingsCode
    // in the mod's main.lua. Bits 0 to 22 are the switches in the settings
    // file's order (Game.ToggleKeys, then Game.ChaosKeys), 23 and 24 the
    // encounter rate, 25 and 26 the chest quantity, 27 and 28 kept for trapped
    // chests (always 0: the game's chest traps couldn't start a fight),
    // then six bits each for the EXP, Gil and MRP rates (quarter steps less 1),
    // 47 and 48 the music mode (1.0.0); 49 is free. FFR1 (0.7.0) had 7
    // characters and stopped at bit 26.
    static class SettingsCode
    {
        public const int Format = 2;
        public const int ToggleCount = 23;
        const string Digits = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const string Example = "Codes look like FFR2-0631G07ZQZ-49353958654.";
        public static readonly string[] Encounters = { "normal", "half", "double" };
        public static readonly string[] Quantities = { "default", "9", "50" };
        public static readonly string[] RateKeys = { "exp_rate", "gil_rate", "mrp_rate" };
        public static readonly string[] Musics = { "off", "shuffle", "mixed" };

        public static string Encode(RunOptions o)
        {
            long bits = 0;
            for (int i = 0; i < ToggleCount; i++)
            {
                if (o.Toggles[i])
                {
                    bits |= 1L << i;
                }
            }
            bits |= (long)o.Encounter << 23;
            bits |= (long)o.Quantity << 25;
            bits |= (long)o.Traps << 27;
            for (int i = 0; i < 3; i++)
            {
                bits |= (long)(o.Rates[i] - 1) << (29 + 6 * i);
            }
            bits |= (long)o.Music << 47;
            StringBuilder text = new StringBuilder("FFR" + Format + "-");
            for (int i = 9; i >= 0; i--)
            {
                text.Append(Digits[(int)((bits >> (5 * i)) & 31)]);
            }
            return text.Append('-').Append(o.Seed).ToString();
        }

        // A rate as the settings file writes it, the way the mod writes it: 1, 2.5, 0.25.
        public static string RateText(int steps)
        {
            return (steps / 4.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Read the way the mod reads it: pulled to the nearest quarter, 0.25 to 10.
        public static int RateSteps(string text)
        {
            double value;
            if (!double.TryParse(text ?? "", System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return 4;
            }
            return Math.Max(1, Math.Min(40, (int)Math.Floor(value * 4 + 0.5)));
        }

        // A settings file's values, read the way the mod reads them: items,
        // shops and Visions default to on, the rest to off.
        public static RunOptions FromValues(Dictionary<string, string> values)
        {
            RunOptions o = new RunOptions();
            List<string> keys = new List<string>(Game.ToggleKeys);
            keys.AddRange(Game.ChaosKeys);
            for (int i = 0; i < keys.Count; i++)
            {
                string value;
                o.Toggles[i] = values.TryGetValue(keys[i], out value)
                    ? !(value.ToLowerInvariant() == "off" || value.ToLowerInvariant() == "false" || value == "0")
                    : i < 3;
            }
            o.Seed = Lookup(values, "seed");
            o.Encounter = Math.Max(0, Array.IndexOf(Encounters, Lookup(values, "encounter_rate").ToLowerInvariant()));
            o.Quantity = Math.Max(0, Array.IndexOf(Quantities, Lookup(values, "chest_quantity").ToLowerInvariant()));
            for (int i = 0; i < 3; i++)
            {
                o.Rates[i] = values.ContainsKey(RateKeys[i]) ? RateSteps(values[RateKeys[i]]) : 4;
            }
            o.Music = Math.Max(0, Array.IndexOf(Musics, Lookup(values, "music").ToLowerInvariant()));
            return o;
        }

        static string Lookup(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }

        // Reads an FFR1 or FFR2 code. On failure, error says why.
        public static bool TryDecode(string code, out RunOptions o, out string error)
        {
            o = null;
            error = null;
            Match match = Regex.Match(code ?? "", @"^\s*FFR(\d+)-([0-9A-Za-z]+)-(\S{1,40})\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                error = "That isn’t an FFR settings code. " + Example;
                return false;
            }
            int format;
            int.TryParse(match.Groups[1].Value, out format);
            if (format > Format)
            {
                error = "This code is from a newer FFR Randomizer (FFR" + format + "). Update the app to load it.";
                return false;
            }
            int length = format == 1 ? 7 : format == 2 ? 10 : 0;
            if (length == 0 || match.Groups[2].Value.Length != length)
            {
                error = "That isn’t an FFR settings code. " + Example;
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
            o = new RunOptions();
            o.Encounter = (int)((bits >> 23) & 3);
            o.Quantity = (int)((bits >> 25) & 3);
            int used = 27;
            if (format == 2)
            {
                o.Traps = (int)((bits >> 27) & 3);
                for (int i = 0; i < 3; i++)
                {
                    o.Rates[i] = (int)((bits >> (29 + 6 * i)) & 63) + 1;
                }
                o.Music = (int)((bits >> 47) & 3);
                used = 49;
            }
            if ((bits >> used) != 0 || o.Encounter > 2 || o.Quantity > 2 || o.Traps > 2 || o.Music > 2
                || Math.Max(o.Rates[0], Math.Max(o.Rates[1], o.Rates[2])) > 40)
            {
                o = null;
                error = "This code has options this app doesn’t know. Update the app to load it.";
                return false;
            }
            for (int i = 0; i < ToggleCount; i++)
            {
                o.Toggles[i] = ((bits >> i) & 1) == 1;
            }
            o.Seed = match.Groups[3].Value;
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

    // Drawing shared by the menu controls: windows, fields, the pointing hand
    // and shadowed text. Sizes are in design pixels times the scale s.
    static class MenuArt
    {
        // A blue gradient window with a light beveled border and a thin black
        // outline. colors: four gradient stops, top left to bottom right.
        public static void DrawWindow(Graphics g, RectangleF r, float s, Color[] colors, Color edge, int radius)
        {
            if (r.Width < 4 || r.Height < 4)
            {
                return;
            }
            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = radius * s;
            using (GraphicsPath outer = Shapes.Rounded(r, rad))
            using (Pen black = new Pen(Color.Black, 1.5f * s))
            {
                g.DrawPath(black, outer);
            }
            RectangleF fill = Inset(r, 0.75f * s);
            using (GraphicsPath path = Shapes.Rounded(fill, rad))
            using (LinearGradientBrush brush = new LinearGradientBrush(Inset(fill, -1), colors[0], colors[3], 70f))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = colors;
                blend.Positions = new[] { 0f, 0.38f, 0.72f, 1f };
                brush.InterpolationColors = blend;
                g.FillPath(brush, path);
            }
            using (GraphicsPath bevel = Shapes.Rounded(Inset(r, 1.75f * s), rad - 1.5f * s))
            using (Pen pen = new Pen(edge, 2.25f * s))
            {
                g.DrawPath(pen, bevel);
            }
            using (GraphicsPath inner = Shapes.Rounded(Inset(r, 3.4f * s), rad - 3f * s))
            using (Pen pen = new Pen(Theme.BevelInner, 1f * s))
            {
                g.DrawPath(pen, inner);
            }
            g.SmoothingMode = mode;
        }

        // A sunken text field: dark fill, shadowed top and left, lit bottom and right.
        public static void DrawField(Graphics g, RectangleF r, float s)
        {
            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shapes.Rounded(r, 4 * s))
            using (Brush brush = new SolidBrush(Theme.Field))
            {
                g.FillPath(brush, path);
            }
            float w = 2 * s, h = w / 2;
            using (Pen dark = new Pen(ColorTranslator.FromHtml("#03061f"), w))
            using (Pen light = new Pen(ColorTranslator.FromHtml("#8c94b8"), w))
            {
                g.DrawLines(dark, new[] { new PointF(r.Left + h, r.Bottom - 3 * s), new PointF(r.Left + h, r.Top + h), new PointF(r.Right - 3 * s, r.Top + h) });
                g.DrawLines(light, new[] { new PointF(r.Left + 3 * s, r.Bottom - h), new PointF(r.Right - h, r.Bottom - h), new PointF(r.Right - h, r.Top + 3 * s) });
            }
            g.SmoothingMode = mode;
        }

        // The selected-item bar used by tabs and command items.
        public static void DrawHighlight(Graphics g, RectangleF r, float s)
        {
            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shapes.Rounded(r, 4 * s))
            using (LinearGradientBrush brush = new LinearGradientBrush(Inset(r, -1), Color.FromArgb(64, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90f))
            using (Pen pen = new Pen(Color.FromArgb(96, 255, 255, 255), 1f * s))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            g.SmoothingMode = mode;
        }

        // The original pointing glove (cuff, palm, finger), drawn from a 34 x 22
        // design. (x, y) is its top left; height is its drawn height.
        public static void DrawHand(Graphics g, float x, float y, float height)
        {
            float k = height / 22f;
            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(k, k);
            using (GraphicsPath palm = HandPalm())
            {
                // Hard shadow, offset like the text's.
                GraphicsState shadow = g.Save();
                g.TranslateTransform(1f, 2f);
                using (Brush dark = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                {
                    g.FillRectangle(dark, 1, 5, 7, 13);
                    g.FillPath(dark, palm);
                }
                g.Restore(shadow);
                using (Pen outline = new Pen(ColorTranslator.FromHtml("#141833"), 1.4f))
                using (Brush cuff = new SolidBrush(ColorTranslator.FromHtml("#c9cede")))
                using (Brush glove = new SolidBrush(ColorTranslator.FromHtml("#fbfbff")))
                using (Pen folds = new Pen(ColorTranslator.FromHtml("#8d94b3"), 1.1f))
                {
                    outline.LineJoin = LineJoin.Round;
                    folds.StartCap = folds.EndCap = LineCap.Round;
                    using (GraphicsPath band = Shapes.Rounded(new RectangleF(1, 5, 7, 13), 2))
                    {
                        g.FillPath(cuff, band);
                        g.DrawPath(outline, band);
                    }
                    g.FillPath(glove, palm);
                    g.DrawPath(outline, palm);
                    g.DrawLine(folds, 15, 10.2f, 21, 10.2f);
                    g.DrawLine(folds, 15, 13.4f, 22, 13.2f);
                    g.DrawLine(folds, 15, 16.4f, 22.4f, 16.2f);
                }
            }
            g.Restore(state);
            g.SmoothingMode = mode;
        }

        static GraphicsPath HandPalm()
        {
            GraphicsPath p = new GraphicsPath();
            PointF at = new PointF(8, 6);
            at = Quad(p, at, 10, 3.5f, 14, 4);
            p.AddLine(at, new PointF(30, 4.5f));
            at = new PointF(30, 4.5f);
            at = Quad(p, at, 33, 5, 33, 7.5f);
            at = Quad(p, at, 33, 10, 30, 10.2f);
            p.AddLine(at, new PointF(21, 10.2f));
            at = new PointF(21, 10.2f);
            at = Quad(p, at, 22.5f, 11.3f, 22, 13);
            at = Quad(p, at, 23.4f, 14.2f, 22.6f, 16);
            at = Quad(p, at, 23.2f, 17.8f, 21.4f, 19);
            at = Quad(p, at, 19, 20.5f, 14, 20);
            p.AddLine(at, new PointF(8, 18.5f));
            p.CloseFigure();
            return p;
        }

        // A quadratic curve as the equivalent cubic one.
        static PointF Quad(GraphicsPath p, PointF from, float cx, float cy, float x, float y)
        {
            PointF c1 = new PointF(from.X + 2f / 3f * (cx - from.X), from.Y + 2f / 3f * (cy - from.Y));
            PointF c2 = new PointF(x + 2f / 3f * (cx - x), y + 2f / 3f * (cy - y));
            PointF to = new PointF(x, y);
            p.AddBezier(from, c1, c2, to);
            return to;
        }

        // White (or colored) text with the hard two-step drop shadow.
        public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags, float s)
        {
            int one = Math.Max(1, (int)Math.Round(s));
            Rectangle far = bounds;
            far.Offset(2 * one, 2 * one);
            Rectangle near = bounds;
            near.Offset(one, one);
            TextRenderer.DrawText(g, text, font, far, Theme.ShadowSoft, flags);
            TextRenderer.DrawText(g, text, font, near, Theme.Shadow, flags);
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }

        // Paints what's behind a control (its parent's background and painting),
        // so custom controls can have see-through corners on the gradients.
        public static void PaintBehind(Control control, PaintEventArgs e)
        {
            Control parent = control.Parent;
            if (parent == null)
            {
                e.Graphics.Clear(Theme.Ground);
                return;
            }
            GraphicsState state = e.Graphics.Save();
            e.Graphics.TranslateTransform(-control.Left, -control.Top);
            Rectangle area = new Rectangle(control.Left, control.Top, control.Width, control.Height);
            using (PaintEventArgs behind = new PaintEventArgs(e.Graphics, area))
            {
                Bridge.Background(parent, behind);
                Bridge.Foreground(parent, behind);
            }
            e.Graphics.Restore(state);
        }

        public static RectangleF Inset(RectangleF r, float by)
        {
            return new RectangleF(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by);
        }

        // Control.InvokePaintBackground and InvokePaint are protected, so a
        // control subclass passes them through for PaintBehind.
        class Bridge : Control
        {
            static readonly Bridge one = new Bridge();
            public static void Background(Control c, PaintEventArgs e) { one.InvokePaintBackground(c, e); }
            public static void Foreground(Control c, PaintEventArgs e) { one.InvokePaint(c, e); }
        }
    }

    // One pointing hand for the whole window, like a game menu's cursor. It
    // moves to whatever was last focused or pointed at; host is the control
    // that draws it and key says which of the host's rows or items.
    static class HandCursor
    {
        static Control host;
        static int key;

        public static bool At(Control control, int which)
        {
            return host == control && key == which;
        }

        public static void Point(Control control, int which)
        {
            if (host == control && key == which)
            {
                return;
            }
            Control old = host;
            host = control;
            key = which;
            if (old != null && !old.IsDisposed)
            {
                old.Invalidate();
            }
            if (control != null)
            {
                control.Invalidate();
            }
        }

        // Moves the hand to host's row when trigger gets focus or the mouse.
        public static void Attach(Control trigger, Control drawer, int which)
        {
            trigger.GotFocus += delegate { Point(drawer, which); };
            trigger.MouseEnter += delegate { Point(drawer, which); };
        }
    }

    // A label whose text has the menu's drop shadow. Its background shows
    // whatever window it sits on.
    class MenuLabel : Label
    {
        public MenuLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            if (AutoEllipsis)
            {
                flags |= TextFormatFlags.EndEllipsis;
            }
            switch (TextAlign)
            {
                case ContentAlignment.TopCenter: flags |= TextFormatFlags.HorizontalCenter; break;
                case ContentAlignment.TopRight: flags |= TextFormatFlags.Right; break;
                case ContentAlignment.MiddleLeft: flags |= TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleCenter: flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter; break;
                case ContentAlignment.MiddleRight: flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right; break;
                case ContentAlignment.BottomLeft: flags |= TextFormatFlags.Bottom; break;
                case ContentAlignment.BottomCenter: flags |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter; break;
                case ContentAlignment.BottomRight: flags |= TextFormatFlags.Bottom | TextFormatFlags.Right; break;
            }
            Rectangle area = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal - 3, Height - Padding.Vertical - 2);
            MenuArt.DrawText(e.Graphics, Text, Font, area, ForeColor, flags, DeviceDpi / 96f);
        }
    }

    // A panel that paints the black ground and, when Window is set, a menu
    // window inside its margins. Labels and plain cards on it show its gradient.
    class WindowPanel : Panel
    {
        public bool Window = true;
        // Design pixels between the panel's edges and the window's.
        public int MarginSide = MainFormSizes.WinInset;
        public int MarginTop = 0;
        public int MarginBottom = MainFormSizes.Gap;
        public Color[] Colors = Theme.Window;

        public WindowPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Ground;
        }

        public RectangleF WindowBounds
        {
            get
            {
                float s = DeviceDpi / 96f;
                return new RectangleF(MarginSide * s, MarginTop * s, Width - 2 * MarginSide * s,
                    Height - (MarginTop + MarginBottom) * s);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Ground);
            if (Window)
            {
                MenuArt.DrawWindow(e.Graphics, WindowBounds, DeviceDpi / 96f, Colors, Theme.Bevel, 7);
            }
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate(true);
        }
    }

    // On/off as two words, like a game's config menu: the current one bright,
    // the other dim. The hand that points at its row is drawn by the row's card.
    class ToggleSwitch : Control
    {
        bool isOn = true;

        public event EventHandler Changed;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(86, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            BackColor = Color.Transparent;
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
            else if (e.KeyCode == Keys.Left && !On || e.KeyCode == Keys.Right && On)
            {
                On = !On;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            MenuArt.PaintBehind(this, e);
            float s = DeviceDpi / 96f;
            using (Font font = Theme.Ui(11f, FontStyle.Bold))
            {
                Size off = TextRenderer.MeasureText(g, "OFF", font, Size.Empty, TextFormatFlags.NoPadding);
                Size on = TextRenderer.MeasureText(g, "ON", font, Size.Empty, TextFormatFlags.NoPadding);
                int gap = (int)(12 * s);
                int right = Width - (int)(4 * s);
                int y = (Height - off.Height) / 2;
                Rectangle offBox = new Rectangle(right - off.Width, y, off.Width + 4, off.Height + 4);
                Rectangle onBox = new Rectangle(offBox.Left - gap - on.Width, y, on.Width + 4, on.Height + 4);
                Color quiet = Enabled ? Theme.Dim : ColorTranslator.FromHtml("#4f5a8a");
                TextFormatFlags flags = TextFormatFlags.NoPadding;
                DrawWord(g, "ON", font, onBox, Enabled && isOn, quiet, flags, s);
                DrawWord(g, "OFF", font, offBox, Enabled && !isOn, quiet, flags, s);
                if (Focused)
                {
                    using (Pen line = new Pen(Theme.Accent, 1.5f * s))
                    {
                        Rectangle lit = isOn ? onBox : offBox;
                        g.DrawLine(line, lit.Left, lit.Top + off.Height + 2 * s, lit.Left + (isOn ? on.Width : off.Width), lit.Top + off.Height + 2 * s);
                    }
                }
            }
        }

        static void DrawWord(Graphics g, string word, Font font, Rectangle box, bool lit, Color quiet, TextFormatFlags flags, float s)
        {
            if (lit)
            {
                MenuArt.DrawText(g, word, font, box, Theme.Text, flags, s);
            }
            else
            {
                Rectangle shade = box;
                shade.Offset(1, 1);
                TextRenderer.DrawText(g, word, font, shade, Theme.Shadow, flags);
                TextRenderer.DrawText(g, word, font, box, quiet, flags);
            }
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    // A slider in a sunken groove with a small light block for its handle.
    class MenuSlider : Control
    {
        int minimum, maximum = 10, value;
        public int SmallChange = 1;
        public int LargeChange = 5;

        public event EventHandler ValueChanged;

        public MenuSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
        }

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; if (maximum < value) maximum = value; Value = this.value; Invalidate(); }
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = Math.Max(value, minimum); Value = this.value; Invalidate(); }
        }

        public int Value
        {
            get { return value; }
            set
            {
                int clamped = Math.Max(minimum, Math.Min(maximum, value));
                if (clamped == this.value)
                {
                    return;
                }
                this.value = clamped;
                Invalidate();
                if (ValueChanged != null)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        float S { get { return DeviceDpi / 96f; } }

        RectangleF Groove
        {
            get
            {
                float s = S;
                float h = 12 * s;
                return new RectangleF(8 * s, (Height - h) / 2, Width - 16 * s, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            MenuArt.PaintBehind(this, e);
            float s = S;
            RectangleF groove = Groove;
            MenuArt.DrawField(g, groove, s);
            using (Pen tick = new Pen(Color.FromArgb(72, 190, 205, 255), 1f))
            {
                for (int i = 1; i < 10; i++)
                {
                    float x = groove.Left + groove.Width * i / 10f;
                    g.DrawLine(tick, x, groove.Top + 2 * s, x, groove.Bottom - 2 * s);
                }
            }
            float span = Math.Max(1, maximum - minimum);
            float at = groove.Left + 3 * s + (groove.Width - 6 * s) * (value - minimum) / span;
            RectangleF thumb = new RectangleF(at - 6 * s, groove.Top - 7 * s, 12 * s, groove.Height + 14 * s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shadow = Shapes.Rounded(new RectangleF(thumb.X + s, thumb.Y + s, thumb.Width, thumb.Height), 2 * s))
            using (Brush dark = new SolidBrush(Color.Black))
            {
                g.FillPath(dark, shadow);
            }
            using (GraphicsPath block = Shapes.Rounded(thumb, 2 * s))
            using (LinearGradientBrush face = new LinearGradientBrush(MenuArt.Inset(thumb, -1), Color.White, ColorTranslator.FromHtml("#b9c0d8"), 90f))
            using (Pen edge = new Pen(Focused ? Theme.Accent : ColorTranslator.FromHtml("#0a0f30"), (Focused ? 2f : 1f) * s))
            {
                g.FillPath(face, block);
                g.DrawPath(edge, block);
            }
        }

        void SetFromX(int x)
        {
            RectangleF groove = Groove;
            float usable = groove.Width - 6 * S;
            float t = usable <= 0 ? 0 : (x - groove.Left - 3 * S) / usable;
            Value = minimum + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                Capture = true;
                SetFromX(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && e.Button == MouseButtons.Left)
            {
                SetFromX(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value -= SmallChange; e.Handled = true; break;
                case Keys.Right: case Keys.Up: Value += SmallChange; e.Handled = true; break;
                case Keys.PageDown: Value -= LargeChange; e.Handled = true; break;
                case Keys.PageUp: Value += LargeChange; e.Handled = true; break;
                case Keys.Home: Value = minimum; e.Handled = true; break;
                case Keys.End: Value = maximum; e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // A small menu window as a button. Lit adds a cyan glow (presets that
    // match). Command buttons have no window: they show the highlight bar and
    // the hand, like the entries of a game's command menu.
    class MenuButton : Button
    {
        public bool Command;
        bool lit, hover, down;

        public MenuButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(11f, FontStyle.Bold);
        }

        public bool Lit
        {
            get { return lit; }
            set { if (lit != value) { lit = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            if (Command)
            {
                HandCursor.Point(this, 0);
            }
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnGotFocus(EventArgs e)
        {
            // Command entries take the hand; other buttons show focus with a
            // cyan edge, and the hand leaves so it never points somewhere stale.
            HandCursor.Point(Command ? this : null, 0);
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            MenuArt.PaintBehind(this, e);
            float s = DeviceDpi / 96f;
            Rectangle area = ClientRectangle;
            if (Command)
            {
                bool pointed = HandCursor.At(this, 0);
                if (pointed || down)
                {
                    MenuArt.DrawHighlight(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), s);
                }
                if (pointed)
                {
                    float hand = 22 * s;
                    MenuArt.DrawHand(g, 6 * s, (Height - hand) / 2, hand);
                }
                Rectangle text = new Rectangle((int)(48 * s), 0, Width - (int)(52 * s), Height);
                MenuArt.DrawText(g, Text, Font, text, Enabled ? ForeColor : Theme.Dim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix, s);
                return;
            }
            Color[] face = Theme.ButtonFace;
            if (down)
            {
                face = new[] { face[2], face[3], face[3], face[3] };
            }
            else if (hover)
            {
                face = new[] { ColorTranslator.FromHtml("#6a88e8"), face[0], face[1], face[2] };
            }
            Color edge = lit || Focused ? Theme.Accent : Theme.Bevel;
            MenuArt.DrawWindow(g, new RectangleF(1, 1, Width - 2, Height - 2), s, face, edge, 5);
            if (lit)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath glow = Shapes.Rounded(MenuArt.Inset(new RectangleF(1, 1, Width - 2, Height - 2), 4 * s), 3 * s))
                using (Pen pen = new Pen(Color.FromArgb(140, 116, 217, 255), 3f * s))
                {
                    g.DrawPath(pen, glow);
                }
            }
            MenuArt.DrawText(g, Text, Font, new Rectangle(0, 0, Width - 2, Height - 2), Enabled ? ForeColor : Theme.Dim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix, s);
        }
    }

    // One option of a three-way choice: the picked one bright, the others dim,
    // with the hand beside whichever has focus or the mouse.
    class MenuChoice : RadioButton
    {
        public MenuChoice()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            HandCursor.Attach(this, this, 0);
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            MenuArt.PaintBehind(this, e);
            float s = DeviceDpi / 96f;
            if (HandCursor.At(this, 0))
            {
                float hand = 22 * s;
                MenuArt.DrawHand(g, 0, (Height - hand) / 2, hand);
            }
            Rectangle text = new Rectangle((int)(38 * s), 0, Width - (int)(38 * s), Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (Checked)
            {
                MenuArt.DrawText(g, Text, Font, text, Theme.Text, flags, s);
            }
            else
            {
                Rectangle shade = text;
                shade.Offset(1, 1);
                TextRenderer.DrawText(g, Text, Font, shade, Theme.Shadow, flags);
                TextRenderer.DrawText(g, Text, Font, text, Theme.Dim, flags);
            }
            if (Focused)
            {
                Size size = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
                using (Pen line = new Pen(Theme.Accent, 1.5f * s))
                {
                    float y = (Height + size.Height) / 2f + 2 * s;
                    g.DrawLine(line, text.Left + 6 * s, y, text.Left + 6 * s + size.Width, y);
                }
            }
        }
    }

    // A see-through panel that groups rows on a window (the toggle rows, a
    // choice's options), or, as a Field, a sunken box around a text box.
    // It draws the hand for its rows and the lines between them.
    class Card : Panel
    {
        public int Radius = 8;
        public bool Field;
        public int Rows;
        public int RowHeight = 64;

        public Card()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            MenuArt.PaintBehind(this, e);
            if (Field)
            {
                MenuArt.DrawField(e.Graphics, new RectangleF(0, 0, Width, Height), DeviceDpi / 96f);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            float s = DeviceDpi / 96f;
            using (Pen pen = new Pen(Color.FromArgb(48, 190, 205, 255), 1f))
            {
                for (int row = 1; row < Rows; row++)
                {
                    float y = row * RowHeight * s;
                    g.DrawLine(pen, 0, y, Width - 1, y);
                }
            }
            for (int row = 0; row < Rows; row++)
            {
                if (HandCursor.At(this, row))
                {
                    float hand = 22 * s;
                    MenuArt.DrawHand(g, 0, row * RowHeight * s + (RowHeight * s - hand) / 2, hand);
                }
            }
        }
    }

    static class Shapes
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            return Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // A tab in the command window above the options: the selected one has the
    // highlight bar, the others are dim. The hand shows on focus or hover.
    class TabButton : Control
    {
        bool selected;
        string count = "";

        public TabButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTab;
            HandCursor.Attach(this, this, 0);
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
            MenuArt.PaintBehind(this, e);
            float s = DeviceDpi / 96f;
            if (selected)
            {
                MenuArt.DrawHighlight(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), s);
            }
            bool pointed = HandCursor.At(this, 0);
            using (Font name = Theme.Ui(12f, FontStyle.Bold))
            using (Font small = Theme.Ui(9f, FontStyle.Regular))
            {
                Size nameSize = TextRenderer.MeasureText(g, Text, name, Size.Empty, TextFormatFlags.NoPadding);
                Size countSize = TextRenderer.MeasureText(g, count, small, Size.Empty, TextFormatFlags.NoPadding);
                int gap = (int)(8 * s);
                int handRoom = pointed ? (int)(40 * s) : 0;
                bool showCount = nameSize.Width + gap + countSize.Width + handRoom + (int)(16 * s) <= Width;
                int textWidth = nameSize.Width + (showCount ? gap + countSize.Width : 0);
                int x = Math.Max(handRoom + (int)(4 * s), (Width - textWidth) / 2);
                int y = (Height - nameSize.Height) / 2;
                if (pointed)
                {
                    float hand = 20 * s;
                    MenuArt.DrawHand(g, x - 38 * s, (Height - hand) / 2, hand);
                }
                Rectangle nameBox = new Rectangle(x, y, Math.Min(nameSize.Width + 4, Width - x), nameSize.Height + 4);
                if (selected)
                {
                    MenuArt.DrawText(g, Text, name, nameBox, Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis, s);
                }
                else
                {
                    TextRenderer.DrawText(g, Text, name, new Rectangle(nameBox.X + 1, nameBox.Y + 1, nameBox.Width, nameBox.Height), Theme.Shadow, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, Text, name, nameBox, Theme.Dim, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
                if (showCount)
                {
                    TextRenderer.DrawText(g, count, small, new Point(x + nameSize.Width + gap, y + nameSize.Height - countSize.Height - (int)(2 * s)),
                        selected ? Theme.Accent : Theme.Dim, TextFormatFlags.NoPadding);
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
        // Content sits Pad from a section's edge: the window's inset plus the
        // window's own padding.
        const int WinInset = MainFormSizes.WinInset;
        const int WinPad = 14;
        const int Pad = WinInset + WinPad;
        const int ContentWidth = 520;
        const int HeaderHeight = 52;
        // A footer window (the commands or the status box) and the gap under it.
        const int FooterWindow = 112;
        const int FooterRow = FooterWindow + MainFormSizes.Gap;
        // Space above the game folder and seed, the tab strip under them, and
        // the space above and below a tab's options.
        const int TopGap = 4;
        const int StripHeight = 50 + MainFormSizes.Gap;
        const int ContentTop = 0;
        const int ContentBottom = 0;
        // Two columns of sections, whose windows share the gap between them.
        const int SectionWidth = ContentWidth + 2 * Pad;
        const int TwoColumnWidth = 2 * SectionWidth - WinInset;

        // In one column the two command entries share a line, so their window
        // is shorter.
        const int NarrowCommandRow = 40 + 24 + MainFormSizes.Gap;

        // The footer: the commands beside (or above) the status box.
        static int FooterHeight(int columns)
        {
            return columns == 2 ? FooterRow : NarrowCommandRow + FooterRow;
        }

        Panel folderSection;
        Panel seedSection;
        // The settings code, the whole width under the folder and seed.
        Panel codeSection;
        // The folder and seed, then code and presets, in design pixels.
        int topHeight;
        readonly WindowPanel strip = new WindowPanel();
        readonly WindowPanel commandWindow = new WindowPanel();
        readonly WindowPanel statusWindow = new WindowPanel();
        readonly MenuButton spoilerButton = new MenuButton();
        readonly Label checkKey = new MenuLabel();
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
        readonly Label folderStatus = new MenuLabel();
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
        readonly Label encounterHelp = new MenuLabel();
        readonly RadioButton[] quantityButtons = new RadioButton[3];
        readonly string[] quantityValues = { "default", "9", "50" };
        readonly Label quantityHelp = new MenuLabel();
        readonly RadioButton[] musicButtons = new RadioButton[3];
        readonly string[] musicValues = { "off", "shuffle", "mixed" };
        readonly Label musicHelp = new MenuLabel();
        // EXP, Gil and MRP in quarter steps, 1 (0.25x) to 40 (10x).
        readonly MenuSlider[] rateBars = new MenuSlider[3];
        readonly Label[] rateLabels = new Label[3];
        // WoL Mode, Dark Knight Mode, True Chaos Mode, beside the settings code.
        Panel presetSection;
        readonly MenuButton[] presetButtons = new MenuButton[3];
        readonly TextBox codeBox = new TextBox();
        readonly Card savedCard = new Card();
        readonly Label savedText = new MenuLabel();
        readonly Label statusTitle = new MenuLabel();
        readonly Label statusDetail = new MenuLabel();
        readonly Label statusCheck = new MenuLabel();
        bool loadingState;
        DateTime statusWritten;
        readonly MenuButton createButton = new MenuButton();
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
            presetSection = BuildSection(BuildPresets);
            foreach (MenuButton button in presetButtons)
            {
                button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            presetSection.Resize += delegate { LayOutPresets(); };
            BuildTabs();
            int content = 0;
            foreach (OptionTab tab in tabs)
            {
                content = Math.Max(content, tab.ContentHeight(2));
            }
            topHeight = Math.Max(folderSection.Height, seedSection.Height) + Math.Max(codeSection.Height, presetSection.Height);
            fullHeight = HeaderHeight + TopGap + topHeight + StripHeight + ContentTop + content + ContentBottom + FooterHeight(2);

            // Built at the one-column width so every anchor measures from the
            // design layout, then widened to two columns.
            target = this;
            ClientSize = new Size(SectionWidth, fullHeight);
            BuildHeader();
            foreach (Control control in Controls)
            {
                Stretch(control, AnchorStyles.Top);
            }
            BuildFooter();
            Controls.Add(folderSection);
            Controls.Add(seedSection);
            Controls.Add(codeSection);
            Controls.Add(presetSection);
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
            light.Wide.Add(BuildSection(false, y => AddNote("The randomizer as intended: everything shuffles, every character and " +
                "Vision stays playable, and the story can always be finished.", y)));
            light.Left.Add(BuildSection(BuildShuffles));
            light.Left.Add(BuildSection(y => BuildChoice("Chest quantity", y, new[] { "Default", "Up to 9", "Up to 50" },
                quantityButtons, quantityHelp)));
            light.Left.Add(BuildSection(y => BuildChoice("Music", y, new[] { "Off", "Shuffle", "Mixed" },
                musicButtons, musicHelp)));
            light.Right.Add(BuildSection(y => AddToggles(light, "Visions", y,
                Row("Vision abilities", "Abilities and passives swap among Physical or Magic Visions.", abilitiesToggle),
                Row("Supports stay supports", "Fina, Leah, Ayaka and Y\u2019shtola keep 5 or more healing abilities.", supportsToggle),
                Row("Vision finishers", "The 26 Vision finishers trade places.", finishersToggle))));
            light.Right.Add(BuildSection(y => AddToggles(light, "Characters", y,
                Row("Character moves", "Level-up moves swap among characters of the same attack type.", movesToggle),
                Row("Character finishers", "Finishers swap among characters of the same attack type.", characterFinishersToggle))));
            light.Right.Add(BuildSection(BuildRewards));
            light.Toggles.AddRange(new[] { itemsToggle, shopsToggle, visionsToggle, enemiesToggle, bossesToggle });

            OptionTab become = AddTab("We must BECOME Chaos");
            become.Wide.Add(BuildSection(false, y => AddNote("Wilder, still finishable. Builds stop making sense, but nothing is " +
                "taken away for good.", y)));
            become.Left.Add(BuildSection(y => AddToggles(become, "Visions and characters", y,
                Row("Mix Physical and Magic", "Vision abilities come from all 26 Visions.", mixToggle),
                Row("Mix character moves", "Moves and finishers swap across Physical and Magic characters.", mixCharactersToggle),
                Row("Random character stats", "Five main stats trade growth curves per character. HP and MP stay.", statsToggle))));
            become.Left.Add(BuildSection(y => AddToggles(become, "Gear", y,
                Row("Gear stats", "Gear of one kind trades stat sets. Prices stay.", gearStatsToggle),
                Row("Gear passives", "Passives swap among weapons, armor or accessories.", gearPassivesToggle))));
            become.Right.Add(BuildSection(y => AddToggles(become, "Enemies", y,
                Row("Enemy weaknesses", "Each enemy\u2019s element weaknesses and resistances trade places.", weaknessesToggle),
                Row("Status resistances", "Enemies resist different ailments. Bosses keep theirs.", statusToggle),
                Row("Enemy passives", "Passives swap among regular enemies. Bosses keep theirs.", enemyPassivesToggle))));
            become.Right.Add(BuildSection(y => BuildChoice("Encounter rate", y, new[] { "Normal", "Half", "Double" },
                encounterButtons, encounterHelp)));

            OptionTab chaos = AddTab("We are Chaos");
            chaos.Wide.Add(BuildSection(false, y => AddNote("No guard rails. All off by default.", y)));
            chaos.Wide.Add(BuildSection(BuildChaosWarning, Theme.WarningWindow));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Characters and gear", y,
                Row("Fully random stats", "Every stat curve, HP and MP too, comes from anyone. No budget.", randomStatsToggle),
                Row("Swap gear types", "Characters trade weapon and armor types. Rain might use a bow.", swapGearToggle))));
            chaos.Left.Add(BuildSection(y => AddToggles(chaos, "Enemies", y,
                Row("Enemies at full strength", "Swapped enemies and bosses keep their own stats.", fullStrengthToggle))));
            chaos.Right.Add(BuildSection(y => AddToggles(chaos, "Items and shops", y,
                Row("Shops sell anything", "Any item with a price can turn up in any shop.", shopsAnythingToggle),
                Row("Story items in the shuffle", "Key and quest items move too. Can softlock the story.", storyItemsToggle))));

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

        // The tab buttons sit in a window of their own, like a command menu.
        void BuildStrip()
        {
            strip.Size = new Size(SectionWidth, StripHeight);
            foreach (OptionTab tab in tabs)
            {
                strip.Controls.Add(tab.Button);
            }
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
        // A windowed section draws its menu window around its content, 12 px
        // of padding above and below it.
        Panel BuildSection(Func<int, int> build)
        {
            return BuildSection(true, build, Theme.Window);
        }

        Panel BuildSection(Func<int, int> build, Color[] colors)
        {
            return BuildSection(true, build, colors);
        }

        Panel BuildSection(bool window, Func<int, int> build)
        {
            return BuildSection(window, build, Theme.Window);
        }

        Panel BuildSection(bool window, Func<int, int> build, Color[] colors)
        {
            WindowPanel panel = new WindowPanel();
            panel.Window = window;
            panel.Colors = colors;
            panel.Width = SectionWidth;
            target = panel;
            int built = build(window ? 14 : 0);
            if (window)
            {
                int bottom = 0;
                foreach (Control control in panel.Controls)
                {
                    bottom = Math.Max(bottom, control.Bottom);
                }
                panel.Height = bottom + 12 + MainFormSizes.Gap;
            }
            else
            {
                panel.Height = built;
            }
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
        // current tab's options, which stretch to the column width, then the
        // commands and the status box. The window can't grow taller than the
        // tallest tab needs. Side by side, two sections overlap by the window
        // inset, so their windows share one gap.
        void LayOut()
        {
            if (layingOut || current == null || folderSection == null)
            {
                return;
            }
            layingOut = true;
            // The strip is built StripHeight tall and only the form's scaling
            // changes it, so it gives the scale the controls are at.
            float s = strip.Height / (float)StripHeight;
            int inset = (int)Math.Round(WinInset * s);
            int width = ClientSize.Width;
            int columns = width >= (int)(TwoColumnWidth * s) ? 2 : 1;
            int column = columns == 2 ? (width + inset) / 2 : width;
            int second = column - inset;
            int y = (int)Math.Round((HeaderHeight + TopGap) * s);
            if (columns == 2)
            {
                folderSection.SetBounds(0, y, column, folderSection.Height);
                seedSection.SetBounds(second, y, width - second, seedSection.Height);
                y += Math.Max(folderSection.Height, seedSection.Height);
                codeSection.SetBounds(0, y, column, codeSection.Height);
                presetSection.SetBounds(second, y, width - second, presetSection.Height);
                y += Math.Max(codeSection.Height, presetSection.Height);
            }
            else
            {
                foreach (Panel section in new[] { folderSection, seedSection, codeSection, presetSection })
                {
                    section.SetBounds(0, y, width, section.Height);
                    y += section.Height;
                }
            }
            strip.SetBounds(0, y, width, strip.Height);
            LayOutTabButtons(s, width);
            y += strip.Height;
            int footer = (int)Math.Round(FooterHeight(columns) * s);
            int footerTop = ClientSize.Height - footer;
            body.SetBounds(0, y, width, Math.Max(0, footerTop - y));
            int row = (int)Math.Round(FooterRow * s);
            int entryLeft = (int)Math.Round((WinInset + 8) * s);
            int entryHeight = (int)Math.Round(40 * s);
            if (columns == 2)
            {
                commandWindow.SetBounds(0, footerTop, column, row);
                statusWindow.SetBounds(second, footerTop, width - second, row);
                int entryWidth = column - 2 * entryLeft;
                createButton.SetBounds(entryLeft, (int)Math.Round(12 * s), entryWidth, entryHeight);
                spoilerButton.SetBounds(entryLeft, (int)Math.Round(56 * s), entryWidth, entryHeight);
            }
            else
            {
                int narrow = (int)Math.Round(NarrowCommandRow * s);
                commandWindow.SetBounds(0, footerTop, width, narrow);
                statusWindow.SetBounds(0, footerTop + narrow, width, row);
                int half = (width - 2 * entryLeft - (int)Math.Round(8 * s)) / 2;
                createButton.SetBounds(entryLeft, (int)Math.Round(12 * s), half + (int)Math.Round(30 * s), entryHeight);
                spoilerButton.SetBounds(createButton.Right + (int)Math.Round(8 * s), (int)Math.Round(12 * s),
                    width - entryLeft - createButton.Right - (int)Math.Round(8 * s), entryHeight);
            }

            int top = (int)Math.Round(ContentTop * s);
            int bottom = (int)Math.Round(ContentBottom * s);
            // Leave room for the scroll bar whenever the options don't fit, so
            // they never need a sideways one.
            int available = width;
            if (top + current.ContentHeight(columns) + bottom > body.Height)
            {
                available -= SystemInformation.VerticalScrollBarWidth;
            }
            int inner = columns == 2 ? (available + inset) / 2 : available;
            int right = columns == 2 ? inner - inset : 0;
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
                section.SetBounds(0, tops[0] + scroll, inner, section.Height);
                tops[0] += section.Height;
            }
            int side = columns == 2 ? 1 : 0;
            foreach (Panel section in current.Right)
            {
                section.SetBounds(right, tops[side] + scroll, available - right, section.Height);
                tops[side] += section.Height;
            }
            body.ResumeLayout(true);
            if (loaded)
            {
                int tallest = 0;
                foreach (OptionTab tab in tabs)
                {
                    tallest = Math.Max(tallest, tab.ContentHeight(columns));
                }
                int needed = body.Top + top + tallest + bottom + footer + Height - ClientSize.Height;
                MaximumSize = new Size(MaximumSize.Width, Math.Max(needed, MinimumSize.Height));
            }
            layingOut = false;
        }

        // The tabs share the strip window's width in these proportions.
        static readonly int[] TabWidths = { 220, 280, 190 };

        void LayOutTabButtons(float s, int width)
        {
            int gap = (int)Math.Round(6 * s);
            int edge = (int)Math.Round((WinInset + 8) * s);
            int top = (int)Math.Round(7 * s);
            int height = (int)Math.Round(36 * s);
            float total = 0;
            foreach (int w in TabWidths) total += w;
            float room = width - 2 * edge - gap * (tabs.Count - 1);
            int x = edge;
            for (int i = 0; i < tabs.Count; i++)
            {
                int w = i == tabs.Count - 1 ? width - edge - x : (int)(room * TabWidths[i] / total);
                tabs[i].Button.SetBounds(x, top, w, height);
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
            int narrow = (int)Math.Round(SectionWidth * s) + frameWidth;
            if (Width > area.Width)
            {
                Width = narrow;
            }
            int shortest = (int)Math.Round((HeaderHeight + TopGap + topHeight + StripHeight + 160 + FooterHeight(2)) * s)
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

        // The app's crystal icon beside the title.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Icon == null)
            {
                return;
            }
            float s = DeviceDpi / 96f;
            int size = (int)Math.Round(32 * s);
            using (Icon mark = new Icon(Icon, new Size(size, size)))
            {
                e.Graphics.DrawIcon(mark, new Rectangle((int)Math.Round(WinInset * s), (int)Math.Round(10 * s), size, size));
            }
        }

        Label AddLabel(string text, int x, int y, int width, Font font, Color color)
        {
            Label label = new MenuLabel();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, font.Height + 4);
            label.Font = font;
            label.ForeColor = color;
            target.Controls.Add(label);
            return label;
        }

        Label AddHeading(string text, int y)
        {
            return AddLabel(text.ToUpperInvariant(), Pad, y, ContentWidth, Theme.Ui(9.5f, FontStyle.Bold), Theme.Accent);
        }

        MenuButton MakeButton(string text, int x, int y, int width, int height)
        {
            MenuButton button = new MenuButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            target.Controls.Add(button);
            return button;
        }

        // A text box inside a sunken field, 44 px tall.
        Card AddInput(TextBox box, int x, int y, int width, Font font)
        {
            Card card = new Card();
            card.Field = true;
            card.Location = new Point(x, y);
            card.Size = new Size(width, 44);
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Field;
            box.GotFocus += delegate { HandCursor.Point(null, 0); };
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
            AddLabel("FFR Randomizer", WinInset + 42, 13, 300, Theme.Ui(15f, FontStyle.Bold), Theme.Text);
            Label version = AddLabel("v" + Game.AppVersion, Pad + ContentWidth - 86, 18, 100, Theme.Ui(10f, FontStyle.Regular), Theme.Accent);
            version.TextAlign = ContentAlignment.TopRight;
        }

        int BuildFolder(int y)
        {
            AddHeading("Game folder", y);
            AddInput(folderBox, Pad, y + 22, 424, Theme.Ui(9.5f, FontStyle.Regular));
            folderBox.ReadOnly = true;
            folderBox.TabStop = false;
            folderBox.AccessibleName = "Game folder";
            MenuButton browse = MakeButton("Browse", Pad + 432, y + 22, 88, 44);
            browse.Click += delegate { BrowseForGame(); };
            folderStatus.Location = new Point(Pad, y + 72);
            folderStatus.Size = new Size(ContentWidth, 20);
            folderStatus.Font = Theme.Ui(9.5f, FontStyle.Regular);
            target.Controls.Add(folderStatus);
            return y + 104;
        }

        int BuildSeed(int y)
        {
            AddHeading("Seed", y);
            AddInput(seedBox, Pad, y + 22, 412, Theme.Mono(12f));
            seedBox.AccessibleName = "Seed";
            seedBox.MaxLength = 40;
            seedBox.TextChanged += delegate { ClearSaved(); };
            MenuButton randomButton = MakeButton("Random", Pad + 420, y + 22, 100, 44);
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
            AddInput(codeBox, Pad, y + 22, ContentWidth - 88 - 120 - 16, Theme.Mono(12f));
            codeBox.ReadOnly = true;
            codeBox.AccessibleName = "Settings code";
            MenuButton copy = MakeButton("Copy", ContentWidth + Pad - 120 - 8 - 88, y + 22, 88, 44);
            copy.Click += delegate { CopyCode(); };
            MenuButton paste = MakeButton("Paste code", ContentWidth + Pad - 120, y + 22, 120, 44);
            paste.Click += delegate { PasteCode(); };
            AddLabel("The seed and every option, sliders too. Paste a friend’s code to load it.", Pad, y + 72,
                ContentWidth, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + CodeHeight;
        }

        static readonly string[] PresetNames = { "WoL Mode", "Dark Knight Mode", "True Chaos Mode" };

        // Three buttons that set the switches; LayOutPresets shares the width.
        int BuildPresets(int y)
        {
            AddHeading("Presets", y);
            int width = (ContentWidth - 16) / 3;
            for (int i = 0; i < 3; i++)
            {
                int which = i;
                MenuButton button = MakeButton(PresetNames[i], Pad + i * (width + 8), y + 22, width, 44);
                button.Font = Theme.Ui(10.5f, FontStyle.Bold);
                button.Click += delegate { ApplyPreset(which); };
                presetButtons[i] = button;
            }
            AddLabel("One click sets every switch. Lit while yours match; reward sliders stay put.", Pad, y + 72,
                ContentWidth, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + CodeHeight;
        }

        void LayOutPresets()
        {
            if (presetButtons[0] == null)
            {
                return;
            }
            float s = presetButtons[0].Height / 44f;
            int pad = (int)Math.Round(Pad * s), gap = (int)Math.Round(8 * s);
            int width = (presetSection.Width - 2 * pad - 2 * gap) / 3;
            for (int i = 0; i < 3; i++)
            {
                presetButtons[i].SetBounds(pad + i * (width + gap), presetButtons[i].Top, width, presetButtons[i].Height);
            }
        }

        // What each preset turns on, by index in SettingToggles(). The reward
        // sliders and the seed are never part of a preset.
        static RunOptions Preset(int which)
        {
            RunOptions o = new RunOptions();
            int[] on;
            if (which == 0)
            {
                // WoL Mode: items, Visions and enemies; no shops or bosses.
                on = new[] { 0, 2, 3 };
            }
            else if (which == 1)
            {
                // Dark Knight Mode: every shuffle that keeps the game's rules:
                // no mixing, no random stats, no gear stats, nothing from Chaos.
                on = new[] { 0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 14, 15, 16, 17 };
            }
            else
            {
                // True Chaos Mode: everything except the guard rail (supports)
                // and Random character stats, which Fully random stats replaces.
                List<int> all = new List<int>();
                for (int i = 0; i < SettingsCode.ToggleCount; i++)
                {
                    if (i != 8 && i != 12)
                    {
                        all.Add(i);
                    }
                }
                on = all.ToArray();
            }
            foreach (int i in on)
            {
                o.Toggles[i] = true;
            }
            o.Quantity = which;
            o.Encounter = which == 2 ? 2 : 0;
            // WoL keeps the game's music, Dark Knight shuffles, True Chaos mixes.
            o.Music = which;
            return o;
        }

        void ApplyPreset(int which)
        {
            RunOptions o = Preset(which);
            o.Seed = seedBox.Text.Trim();
            o.Rates = CurrentOptions().Rates;
            ApplyOptions(o);
            ShowSaved(PresetNames[which] + " set. Press Create Randomizer File to save it." +
                (which == 2 ? " This run may be unwinnable." : ""), which != 2);
        }

        void UpdatePresetButtons()
        {
            RunOptions current = CurrentOptions();
            for (int i = 0; i < 3; i++)
            {
                RunOptions preset = Preset(i);
                bool match = current.Encounter == preset.Encounter && current.Quantity == preset.Quantity
                    && current.Music == preset.Music;
                for (int t = 0; t < SettingsCode.ToggleCount && match; t++)
                {
                    match = current.Toggles[t] == preset.Toggles[t];
                }
                MenuButton button = presetButtons[i];
                button.Lit = match;
                button.ForeColor = i == 2 ? Theme.Bad : Theme.Text;
            }
        }

        static readonly string[] RateNames = { "EXP", "Gil", "MRP" };

        // Three slider rows: 0.25x to 10x in quarter steps, with a 1x reset.
        int BuildRewards(int y)
        {
            Card card = AddToggleCard("Rewards", y, 3);
            for (int i = 0; i < 3; i++)
            {
                int which = i, top = i * 64;
                Label name = new MenuLabel();
                name.Text = RateNames[i];
                name.Font = Theme.Ui(12f, FontStyle.Bold);
                name.ForeColor = Theme.Text;
                name.SetBounds(38, top + 19, 54, 26);
                MenuSlider bar = new MenuSlider();
                bar.Minimum = 1;
                bar.Maximum = 40;
                bar.Value = 4;
                bar.SmallChange = 1;
                bar.LargeChange = 4;
                bar.AccessibleName = RateNames[i] + " multiplier";
                bar.SetBounds(92, top + 16, ContentWidth - 92 - 44 - 8 - 64 - 8, 32);
                HandCursor.Attach(bar, card, i);
                bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                Label value = new MenuLabel();
                value.Font = Theme.Mono(11f);
                value.ForeColor = Theme.Text;
                value.TextAlign = ContentAlignment.MiddleRight;
                value.SetBounds(ContentWidth - 44 - 8 - 64, top + 18, 64, 28);
                value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                MenuButton reset = new MenuButton();
                reset.Text = "1x";
                reset.AccessibleName = "Reset " + RateNames[i] + " to 1x";
                reset.Font = Theme.Ui(9.5f, FontStyle.Bold);
                reset.SetBounds(ContentWidth - 44, top + 16, 44, 32);
                reset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                reset.Click += delegate { rateBars[which].Value = 4; };
                bar.ValueChanged += delegate
                {
                    rateLabels[which].Text = (rateBars[which].Value / 4.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    ClearSaved();
                };
                rateBars[i] = bar;
                rateLabels[i] = value;
                value.Text = "1.00x";
                card.Controls.Add(name);
                card.Controls.Add(bar);
                card.Controls.Add(value);
                card.Controls.Add(reset);
            }
            AddLabel("From every battle, on every difficulty. Expert’s own 1.5x MRP stacks with it.", Pad,
                y + 22 + 3 * 64 + 6, ContentWidth, Theme.Ui(9.5f, FontStyle.Regular), Theme.Muted);
            return y + 22 + 3 * 64 + 34;
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

        // Drawn in a red-tinted window (see BuildTabs).
        int BuildChaosWarning(int y)
        {
            Label text = AddLabel("These can make a run unwinnable or break the story. Create Randomizer File asks you to confirm " +
                "when any is on.", Pad, y, ContentWidth, Theme.Ui(10f, FontStyle.Bold), ColorTranslator.FromHtml("#ffd6d6"));
            text.Height = 40;
            text.TextAlign = ContentAlignment.MiddleLeft;
            return y + 40;
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

        // Rows of 64 px with faint lines between them, under a heading. The
        // card draws the hand beside whichever row has focus or the mouse.
        Card AddToggleCard(string heading, int y, int rows)
        {
            AddHeading(heading, y);
            Card card = new Card();
            card.Rows = rows;
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, rows * 64);
            target.Controls.Add(card);
            return card;
        }

        // Badges sit after the title: NEW in the accent color, LATER greyed with
        // the title and switch. The title is measured at the screen's scale and
        // brought back to design units, since the form scales everything after.
        // The left 38 px of a row are the hand's.
        void AddShuffleRow(Card card, int index, string title, string detail, ToggleSwitch toggle, string badge)
        {
            bool later = badge == "LATER";
            int top = index * 64;
            int text = 38;
            int width = ContentWidth - text - toggle.Width - 8;
            Label name = new MenuLabel();
            name.Text = title;
            name.Font = Theme.Ui(12f, FontStyle.Bold);
            name.ForeColor = later ? Theme.Dim : Theme.Text;
            name.Location = new Point(text, top + 8);
            name.Size = new Size(width, 24);
            Label info = new MenuLabel();
            info.Text = detail;
            info.Font = Theme.Ui(9.5f, FontStyle.Regular);
            info.ForeColor = Theme.Muted;
            info.Location = new Point(text, top + 31);
            info.Size = new Size(width, 33);
            toggle.Location = new Point(ContentWidth - toggle.Width, top + 17);
            toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            name.Anchor = info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            toggle.AccessibleName = title;
            HandCursor.Attach(toggle, card, index);
            HandCursor.Attach(name, card, index);
            HandCursor.Attach(info, card, index);
            if (!later)
            {
                toggle.Changed += delegate { ClearSaved(); UpdateCounts(); };
            }
            if (badge != null)
            {
                float scale = DeviceDpi / 96f;
                Label tag = new MenuLabel();
                tag.Text = badge;
                tag.Font = Theme.Ui(7.5f, FontStyle.Bold);
                tag.ForeColor = later ? Theme.Dim : Theme.Accent;
                tag.TextAlign = ContentAlignment.MiddleCenter;
                int titleWidth = (int)(TextRenderer.MeasureText(title, name.Font).Width / scale);
                int tagWidth = (int)(TextRenderer.MeasureText(badge, tag.Font).Width / scale) + 6;
                tag.SetBounds(text + titleWidth + 4, top + 11, tagWidth, 18);
                tag.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                card.Controls.Add(tag);
                name.Width = titleWidth + 2;
                name.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            card.Controls.Add(name);
            card.Controls.Add(info);
            card.Controls.Add(toggle);
        }

        // Three words in a row, the picked one bright; the hand sits beside
        // whichever has focus or the mouse.
        int BuildChoice(string heading, int y, string[] labels, RadioButton[] buttons, Label help)
        {
            AddHeading(heading, y);
            Card card = new Card();
            card.Location = new Point(Pad, y + 22);
            card.Size = new Size(ContentWidth, 44);
            target.Controls.Add(card);
            int width = ChoiceWidth;
            for (int i = 0; i < 3; i++)
            {
                RadioButton option = new MenuChoice();
                option.Font = Theme.Ui(12f, FontStyle.Bold);
                option.Text = labels[i];
                option.AccessibleName = heading + ": " + labels[i];
                option.Location = new Point(i * width, 2);
                option.Size = new Size(width, 40);
                option.CheckedChanged += delegate { UpdateChoices(); ClearSaved(); };
                buttons[i] = option;
                card.Controls.Add(option);
            }
            card.Resize += delegate { LayOutChoice(card, buttons); };
            help.Location = new Point(Pad + 38, y + 70);
            help.Size = new Size(ContentWidth - 38, 20);
            help.Font = Theme.Ui(9.5f, FontStyle.Regular);
            help.ForeColor = Theme.Muted;
            target.Controls.Add(help);
            return y + 92;
        }

        // Each option is ChoiceWidth wide (less if the card is narrower), so
        // the three sit together like a menu's choices.
        const int ChoiceWidth = 160;

        void LayOutChoice(Card card, RadioButton[] buttons)
        {
            if (buttons[2] == null)
            {
                return;
            }
            int width = Math.Min((int)Math.Round(ChoiceWidth * DeviceDpi / 96f), card.ClientSize.Width / 3);
            for (int i = 0; i < 3; i++)
            {
                buttons[i].SetBounds(i * width, buttons[i].Top, width, buttons[i].Height);
            }
        }

        // Bottom left, a command window with the two main actions; bottom
        // right, the status box: the apply status, its seed check, and the
        // saved message laid over them while one is showing. LayOut places them.
        void BuildFooter()
        {
            foreach (WindowPanel window in new[] { commandWindow, statusWindow })
            {
                window.Size = new Size(SectionWidth, FooterRow);
                Controls.Add(window);
            }
            int inner = SectionWidth - 2 * Pad;

            createButton.Command = true;
            createButton.Text = "Create Randomizer File";
            createButton.Font = Theme.Ui(13.5f, FontStyle.Bold);
            createButton.SetBounds(WinInset + 8, 12, SectionWidth - 2 * (WinInset + 8), 40);
            createButton.Anchor = AnchorStyles.None;
            createButton.Click += delegate { CreateFile(); };
            commandWindow.Controls.Add(createButton);
            AcceptButton = createButton;

            spoilerButton.Command = true;
            spoilerButton.Text = "Open spoiler log";
            spoilerButton.Font = Theme.Ui(12f, FontStyle.Bold);
            spoilerButton.ForeColor = Theme.Muted;
            spoilerButton.SetBounds(WinInset + 8, 56, SectionWidth - 2 * (WinInset + 8), 40);
            spoilerButton.Anchor = AnchorStyles.None;
            spoilerButton.Click += delegate { OpenSpoiler(); };
            commandWindow.Controls.Add(spoilerButton);

            statusTitle.SetBounds(Pad, 10, inner, 24);
            statusTitle.Font = Theme.Ui(12f, FontStyle.Bold);
            statusTitle.AutoEllipsis = true;
            statusDetail.SetBounds(Pad, 34, inner, 40);
            statusDetail.Font = Theme.Ui(9.5f, FontStyle.Regular);
            statusDetail.AutoEllipsis = true;
            checkKey.Text = "SEED CHECK";
            checkKey.Font = Theme.Ui(9.5f, FontStyle.Bold);
            checkKey.ForeColor = Theme.Accent;
            checkKey.SetBounds(Pad, 77, 110, 20);
            checkKey.TextAlign = ContentAlignment.MiddleLeft;
            statusCheck.Font = Theme.Mono(12f);
            statusCheck.ForeColor = Theme.Text;
            statusCheck.SetBounds(Pad + 112, 74, inner - 112, 24);
            statusCheck.TextAlign = ContentAlignment.MiddleLeft;
            foreach (Label line in new[] { statusTitle, statusDetail, checkKey, statusCheck })
            {
                line.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                statusWindow.Controls.Add(line);
            }
            checkKey.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            savedCard.SetBounds(WinInset + 4, 4, SectionWidth - 2 * (WinInset + 4), FooterWindow - 8);
            savedCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            savedText.SetBounds(WinPad - 4, 4, savedCard.Width - 2 * (WinPad - 4), savedCard.Height - 8);
            savedText.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            savedText.TextAlign = ContentAlignment.MiddleLeft;
            savedText.Font = Theme.Ui(11f, FontStyle.Bold);
            savedCard.Controls.Add(savedText);
            savedCard.Visible = false;
            statusWindow.Controls.Add(savedCard);
            savedCard.BringToFront();
        }

        void UpdateChoices()
        {
            int quantity = Picked(quantityButtons);
            int encounter = Picked(encounterButtons);
            int music = Picked(musicButtons);
            foreach (RadioButton[] buttons in new[] { quantityButtons, encounterButtons, musicButtons })
            {
                foreach (RadioButton option in buttons)
                {
                    if (option != null)
                    {
                        option.Invalidate();
                    }
                }
            }
            quantityHelp.Text = quantity == 0
                ? "Each chest keeps its normal amount. Gear is always 1."
                : "Each chest gives 1 to " + quantityValues[quantity] + " of its item. Gear is always 1.";
            encounterHelp.Text = encounter == 0 ? "Random battles as often as usual."
                : encounter == 1 ? "Half as many random battles: twice the walk between them."
                : "Twice as many random battles: half the walk between them.";
            musicHelp.Text = music == 0 ? "The game’s own music. Airship and chocobo music never change."
                : music == 1 ? "Battle tracks swap among battles, field and town tracks among theirs."
                : "Battle, field and town tracks all swap in one pool.";
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

        RunOptions CurrentOptions()
        {
            RunOptions o = new RunOptions();
            ToggleSwitch[] toggles = SettingToggles();
            for (int i = 0; i < toggles.Length; i++)
            {
                o.Toggles[i] = toggles[i].On;
            }
            o.Seed = seedBox.Text.Trim();
            o.Encounter = Picked(encounterButtons);
            o.Quantity = Picked(quantityButtons);
            o.Music = Picked(musicButtons);
            for (int i = 0; i < 3; i++)
            {
                o.Rates[i] = rateBars[i] == null ? 4 : rateBars[i].Value;
            }
            return o;
        }

        // Sets every switch, choice and slider at once, then refreshes once.
        void ApplyOptions(RunOptions o)
        {
            loadingState = true;
            ToggleSwitch[] toggles = SettingToggles();
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].On = o.Toggles[i];
            }
            encounterButtons[o.Encounter].Checked = true;
            quantityButtons[o.Quantity].Checked = true;
            musicButtons[o.Music].Checked = true;
            for (int i = 0; i < 3; i++)
            {
                rateBars[i].Value = o.Rates[i];
            }
            seedBox.Text = o.Seed;
            loadingState = false;
            UpdateOptions();
            UpdateChoices();
            UpdateCounts();
            UpdateCode();
        }

        void UpdateCode()
        {
            if (!loadingState)
            {
                codeBox.Text = SettingsCode.Encode(CurrentOptions());
                UpdatePresetButtons();
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
            RunOptions o;
            string error;
            if (!SettingsCode.TryDecode(text, out o, out error))
            {
                ShowSaved(text.Trim() == "" ? "Copy a settings code first, then press Paste code." : error, false);
                return;
            }
            int count = 0;
            foreach (bool on in o.Toggles)
            {
                count += on ? 1 : 0;
            }
            ApplyOptions(o);
            ShowSaved("Loaded " + codeBox.Text + ": seed and " + count + " options set. Press Create Randomizer File to save it.", true);
        }

        // A message over the status box until the next change.
        void ShowSaved(string message, bool good)
        {
            savedText.ForeColor = good ? Theme.Good : Theme.Bad;
            savedText.Text = message;
            savedCard.Visible = true;
            savedCard.Invalidate(true);
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
                else if (Game.CompareVersions(version, Game.MinModVersion) < 0)
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
            string fileCode = File.Exists(settingsPath) ? SettingsCode.Encode(SettingsCode.FromValues(Game.ReadSettings(gameDir))) : null;
            // An older mod writes an older code format; compare it as this one.
            RunOptions statusOptions;
            string ignored;
            if (SettingsCode.TryDecode(code, out statusOptions, out ignored))
            {
                code = SettingsCode.Encode(statusOptions);
            }
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
            string check = Value(status, "check") == "" ? null : Value(status, "check");
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
            Color[] text = { Theme.Text, Theme.Warn, Theme.Bad, Theme.Good };
            statusTitle.ForeColor = text[kind];
            statusDetail.ForeColor = kind == 1 || kind == 2 ? text[kind] : Theme.Muted;
            statusTitle.Text = title;
            statusDetail.Text = detail;
            statusCheck.Text = check ?? "";
            checkKey.Visible = check != null;
            statusCheck.Visible = check != null;
            statusWindow.Invalidate(true);
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
            PickChoice(values, "music", musicValues, musicButtons);
            for (int i = 0; i < 3; i++)
            {
                string rate;
                rateBars[i].Value = values.TryGetValue(SettingsCode.RateKeys[i], out rate) ? SettingsCode.RateSteps(rate) : 4;
            }
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
                for (int i = 0; i < 3; i++)
                {
                    values.Add(SettingsCode.RateText(rateBars[i].Value));
                }
                values.Add(musicValues[Picked(musicButtons)]);
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
