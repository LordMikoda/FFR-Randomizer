// FFR Randomizer Setup: installs the app and the UE4SS mod, and uninstalls
// them again with /uninstall. The files it installs are embedded as resources
// named "payload/<path>" by build.cmd. Built with the C# 5 compiler that ships
// with .NET Framework 4.8, like the app.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("FFR Randomizer Setup")]
[assembly: AssemblyProduct("FFR Randomizer")]

namespace FFRRandomizerSetup
{
    static class Theme
    {
        public static readonly Color Ground = ColorTranslator.FromHtml("#14171f");
        public static readonly Color Panel = ColorTranslator.FromHtml("#1c212b");
        public static readonly Color Border = ColorTranslator.FromHtml("#2c3340");
        public static readonly Color Text = ColorTranslator.FromHtml("#e8ebf0");
        public static readonly Color Muted = ColorTranslator.FromHtml("#9aa3b2");
        public static readonly Color Accent = ColorTranslator.FromHtml("#5aa9e6");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#7bbcec");
        public static readonly Color AccentText = ColorTranslator.FromHtml("#0b1220");
        public static readonly Color Button = ColorTranslator.FromHtml("#262c38");
        public static readonly Color ButtonBorder = ColorTranslator.FromHtml("#3a4250");
        public static readonly Color Good = ColorTranslator.FromHtml("#8fd3a8");
        public static readonly Color Bad = ColorTranslator.FromHtml("#f2a3a3");

        public static Font Ui(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    // Finds the game the same way the app does. Keep in step with Game in
    // gui/FFRRandomizerGui.cs.
    static class Game
    {
        public const string SteamAppId = "3259780";
        public const string FolderName = "FINAL FANTASY RESONANCE";
        public const string ModsPath = @"FFRS\Binaries\Win64\ue4ss\Mods";
        public const string Ue4ssPath = @"FFRS\Binaries\Win64\ue4ss\UE4SS.dll";

        public static bool IsGame(string dir)
        {
            return dir != "" && Directory.Exists(Path.Combine(dir, @"FFRS\Binaries\Win64"));
        }

        public static bool HasUe4ss(string dir)
        {
            return File.Exists(Path.Combine(dir, Ue4ssPath));
        }

        public static string ModDir(string dir)
        {
            return Path.Combine(dir, ModsPath, "FFRRandomizer");
        }

        // Accepts the game folder or any folder inside it, such as FFRS.
        public static string Normalize(string folder)
        {
            string dir = folder;
            while (!string.IsNullOrEmpty(dir))
            {
                if (IsGame(dir))
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
    }

    // Where everything goes, and the embedded files.
    static class Install
    {
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FFRRandomizer";
        const string PayloadPrefix = "payload/";
        const string ModPrefix = "FFRRandomizer/";

        public static string AppDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\FFR Randomizer");
            }
        }

        public static string AppExe { get { return Path.Combine(AppDir, "FFRRandomizer.exe"); } }
        public static string Uninstaller { get { return Path.Combine(AppDir, "Uninstall.exe"); } }

        // The app remembers the game folder here (GuiConfig in the app).
        public static string AppConfigDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFRRandomizer");
            }
        }

        public static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FFR Randomizer.lnk"); }
        }

        public static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FFR Randomizer.lnk"); }
        }

        public static string Version
        {
            get
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("version.txt"))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd().Trim();
                }
            }
        }

        static IEnumerable<string> Payload()
        {
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (name.StartsWith(PayloadPrefix, StringComparison.Ordinal))
                {
                    yield return name.Substring(PayloadPrefix.Length);
                }
            }
        }

        static void Extract(string name, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadPrefix + name))
            using (FileStream file = File.Create(target))
            {
                stream.CopyTo(file);
            }
        }

        public static string SavedGameDir()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                return key == null ? "" : (key.GetValue("GameFolder") as string ?? "");
            }
        }

        // Mod files go into the game's ue4ss\Mods\FFRRandomizer. Randomizer.txt and
        // spoiler logs there aren't in the payload, so an update keeps them.
        public static void Run(string gameDir, bool desktop)
        {
            string modDir = Game.ModDir(gameDir);
            foreach (string name in Payload())
            {
                if (name.StartsWith(ModPrefix, StringComparison.Ordinal))
                {
                    Extract(name, Path.Combine(modDir, name.Substring(ModPrefix.Length).Replace('/', '\\')));
                }
                else
                {
                    Extract(name, Path.Combine(AppDir, name.Replace('/', '\\')));
                }
            }
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Uninstaller), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(self, Uninstaller, true);
            }

            Shortcut(StartMenuShortcut);
            if (desktop)
            {
                Shortcut(DesktopShortcut);
            }
            else if (File.Exists(DesktopShortcut))
            {
                File.Delete(DesktopShortcut);
            }

            Directory.CreateDirectory(AppConfigDir);
            File.WriteAllText(Path.Combine(AppConfigDir, "gui.txt"), gameDir);

            long size = 0;
            foreach (string file in Directory.GetFiles(AppDir))
            {
                size += new FileInfo(file).Length;
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "FFR Randomizer");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "LordMikoda");
                key.SetValue("DisplayIcon", AppExe);
                key.SetValue("InstallLocation", AppDir);
                key.SetValue("UninstallString", "\"" + Uninstaller + "\" /uninstall");
                key.SetValue("GameFolder", gameDir);
                key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        static void Shortcut(string path)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type linkType = link.GetType();
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { AppExe });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { AppDir });
            linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { AppExe + ",0" });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Make a Randomizer File for Final Fantasy Resonance" });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }

        // Removes the mod folder, the app, its shortcuts and settings, and the
        // Installed apps entry. The uninstaller can't delete itself while it
        // runs, so a short-lived cmd removes the app folder after it exits.
        public static void Remove()
        {
            string gameDir = SavedGameDir();
            if (gameDir != "")
            {
                string modDir = Game.ModDir(gameDir);
                if (modDir.EndsWith(@"ue4ss\Mods\FFRRandomizer", StringComparison.OrdinalIgnoreCase) && Directory.Exists(modDir))
                {
                    Directory.Delete(modDir, true);
                }
            }
            if (File.Exists(AppExe))
            {
                File.Delete(AppExe);
            }
            foreach (string path in new[] { StartMenuShortcut, DesktopShortcut, Path.Combine(AppConfigDir, "gui.txt") })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            if (Directory.Exists(AppConfigDir) && Directory.GetFileSystemEntries(AppConfigDir).Length == 0)
            {
                Directory.Delete(AppConfigDir);
            }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            ProcessStartInfo cleanup = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + AppDir + "\"");
            cleanup.CreateNoWindow = true;
            cleanup.UseShellExecute = false;
            cleanup.WorkingDirectory = Path.GetTempPath();
            Process.Start(cleanup);
        }
    }

    class SetupForm : Form
    {
        const int Pad = 20;
        const int ContentWidth = 480;
        readonly TextBox folderBox = new TextBox();
        readonly Label status = new Label();
        readonly CheckBox desktopBox = new CheckBox();
        readonly Button installButton = new Button();
        readonly Button closeButton = new Button();
        string gameDir = "";
        bool installed;

        public SetupForm()
        {
            Text = "FFR Randomizer Setup";
            Font = Theme.Ui(9.5f, FontStyle.Regular);
            BackColor = Theme.Ground;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(520, 330);
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
            {
                if (icon != null)
                {
                    Icon = new Icon(icon);
                }
            }

            AddLabel("FFR Randomizer " + Install.Version, Pad, 18, 380, Theme.Ui(13f, FontStyle.Bold), Theme.Text);
            AddLabel("Installs the FFR Randomizer app and its UE4SS mod for Final Fantasy Resonance.",
                Pad, 50, ContentWidth, Font, Theme.Muted);

            AddLabel("GAME FOLDER", Pad, 88, 200, Theme.Ui(8.5f, FontStyle.Bold), Theme.Muted);
            Panel box = new Panel();
            box.BackColor = Theme.Panel;
            box.Location = new Point(Pad, 110);
            box.Size = new Size(376, 36);
            folderBox.BorderStyle = BorderStyle.None;
            folderBox.BackColor = Theme.Panel;
            folderBox.ForeColor = Theme.Text;
            folderBox.ReadOnly = true;
            folderBox.TabStop = false;
            folderBox.AccessibleName = "Game folder";
            folderBox.Location = new Point(10, 9);
            folderBox.Width = 356;
            box.Controls.Add(folderBox);
            Controls.Add(box);
            Button browse = MakeButton("Browse", 404, 110, 96, 36, false);
            browse.Click += delegate { Browse(); };

            status.Location = new Point(Pad, 154);
            status.Size = new Size(ContentWidth, 40);
            Controls.Add(status);

            desktopBox.Text = "Add a desktop shortcut";
            desktopBox.Checked = true;
            desktopBox.ForeColor = Theme.Text;
            desktopBox.Location = new Point(Pad, 200);
            desktopBox.Size = new Size(300, 24);
            Controls.Add(desktopBox);

            installButton.Text = "Install";
            installButton.Location = new Point(Pad, 262);
            installButton.Size = new Size(352, 46);
            StyleButton(installButton, true);
            installButton.Click += delegate { OnInstall(); };
            Controls.Add(installButton);
            closeButton.Text = "Close";
            closeButton.Location = new Point(384, 262);
            closeButton.Size = new Size(116, 46);
            StyleButton(closeButton, false);
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);
            AcceptButton = installButton;

            string saved = Install.SavedGameDir();
            SetGameDir(saved != "" && Game.IsGame(saved) ? saved : Game.Detect());
        }

        Label AddLabel(string text, int x, int y, int width, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.Location = new Point(x, y);
            label.Size = new Size(width, font.Height + 6);
            Controls.Add(label);
            return label;
        }

        Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(w, h);
            StyleButton(button, primary);
            Controls.Add(button);
            return button;
        }

        static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Cursor = Cursors.Hand;
            if (primary)
            {
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
                button.BackColor = Theme.Accent;
                button.ForeColor = Theme.AccentText;
                button.Font = Theme.Ui(11f, FontStyle.Bold);
            }
            else
            {
                button.FlatAppearance.BorderColor = Theme.ButtonBorder;
                button.FlatAppearance.MouseOverBackColor = Theme.Border;
                button.BackColor = Theme.Button;
                button.ForeColor = Theme.Text;
            }
        }

        void SetGameDir(string dir)
        {
            gameDir = dir == "" ? "" : Game.Normalize(dir);
            folderBox.Text = gameDir == "" ? "Not found. Click Browse." : gameDir;
            if (!Game.IsGame(gameDir))
            {
                SetStatus("Final Fantasy Resonance wasn't found. Click Browse and pick its folder.", false);
                installButton.Enabled = false;
            }
            else if (!Game.HasUe4ss(gameDir))
            {
                SetStatus("UE4SS isn't installed in this game folder. Install UE4SS (a build for Unreal Engine 5.6) first, then run Setup again.", false);
                installButton.Enabled = false;
            }
            else
            {
                bool update = Directory.Exists(Game.ModDir(gameDir));
                SetStatus(update
                    ? "\u2713  Game and UE4SS found. The mod is already installed; Setup updates it and keeps your Randomizer File."
                    : "\u2713  Game and UE4SS found.", true);
                installButton.Enabled = true;
            }
        }

        void SetStatus(string text, bool good)
        {
            status.Text = text;
            status.ForeColor = good ? Theme.Good : Theme.Bad;
        }

        void Browse()
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
                }
            }
        }

        void OnInstall()
        {
            if (installed)
            {
                Process.Start(Install.AppExe);
                Close();
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                Install.Run(gameDir, desktopBox.Checked);
            }
            catch (UnauthorizedAccessException)
            {
                SetStatus("Windows blocked writing to the game folder. Close Setup, right-click it and choose Run as administrator.", false);
                return;
            }
            catch (IOException ex)
            {
                SetStatus("Install failed: " + ex.Message + " Close the FFR Randomizer app and the game, then try again.", false);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            installed = true;
            SetStatus("\u2713  Installed. Open FFR Randomizer from the Start menu to make a Randomizer File, then start a New Game.", true);
            installButton.Text = "Open FFR Randomizer";
            desktopBox.Enabled = false;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = Array.Exists(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool quiet = Array.Exists(args, a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            if (!uninstall)
            {
                Application.Run(new SetupForm());
                return 0;
            }

            if (!quiet && MessageBox.Show(
                    "Remove FFR Randomizer?\n\nThis removes the app and the FFRRandomizer mod folder, including your Randomizer File and spoiler logs. UE4SS and the game are not touched.",
                    "FFR Randomizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return 1;
            }
            try
            {
                Install.Remove();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                if (!quiet)
                {
                    MessageBox.Show("Couldn't remove everything: " + ex.Message + "\n\nClose the FFR Randomizer app and the game, then try again.",
                        "FFR Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return 2;
            }
            if (!quiet)
            {
                MessageBox.Show("FFR Randomizer was removed.", "FFR Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }
    }
}
