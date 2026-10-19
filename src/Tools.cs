using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;

namespace WinTools
{
    /// <summary>Funzioni di servizio: avvio di programmi e cartelle, tasto rapido, avvio con Windows, formati.</summary>
    public static class Tools
    {
        public static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

        // ------------------------------------------------------------------ avvio
        /// <summary>Apre il bersaglio di un tasto: programma, file, cartella, indirizzo, «ms-settings:», «shell:…».</summary>
        public static void Launch(BarButton b)
        {
            if (b == null || string.IsNullOrWhiteSpace(b.Target)) return;
            Open(b.Target, b.Arguments, b.WorkingDir, b.Admin);
        }

        public static void Open(string target, string arguments = "", string workingDir = "", bool admin = false)
        {
            string t = Environment.ExpandEnvironmentVariables((target ?? "").Trim());
            if (t == "") return;
            try
            {
                if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    // Le cartelle speciali (shell:Downloads, shell:RecycleBinFolder, shell:AppsFolder\…) le apre Esplora file
                    Process.Start(new ProcessStartInfo("explorer.exe", t) { UseShellExecute = true });
                    return;
                }
                var psi = new ProcessStartInfo(t)
                {
                    UseShellExecute = true,
                    Arguments = Environment.ExpandEnvironmentVariables(arguments ?? ""),
                };
                string wd = Environment.ExpandEnvironmentVariables(workingDir ?? "");
                if (wd == "" && Path.IsPathRooted(t) && File.Exists(t)) wd = Path.GetDirectoryName(t);
                if (wd != "" && Directory.Exists(wd)) psi.WorkingDirectory = wd;
                if (admin) psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // L'utente ha annullato la richiesta UAC: niente da segnalare
            }
            catch (Exception ex)
            {
                Program.LogError("Avvio di " + t, ex);
                System.Windows.MessageBox.Show($"Impossibile aprire:\n{t}\n\n{ex.Message}", Program.Title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>Apre la cartella che contiene il file e lo seleziona.</summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Program.LogError("Apri cartella", ex); }
        }

        public static void CopyToClipboard(string text)
        {
            try { System.Windows.Clipboard.SetText(text ?? ""); } catch { }
        }

        // ------------------------------------------------------------------ appunti con file (come Esplora file)
        /// <summary>Mette il file o la cartella negli appunti come «Copia» o «Taglia» di Esplora file: si incolla ovunque.</summary>
        public static void CopyFileToClipboard(string path, bool cut)
        {
            try
            {
                var files = new System.Collections.Specialized.StringCollection { path };
                var data = new System.Windows.DataObject();
                data.SetFileDropList(files);
                // DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2: è così che Esplora file distingue Copia da Taglia
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
                System.Windows.Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex) { Program.LogError("Copia negli appunti", ex); }
        }

        /// <summary>I file negli appunti (da Esplora file o da WinTools), se ce ne sono.</summary>
        public static List<string> ClipboardFiles()
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsFileDropList()) return new List<string>();
                return System.Windows.Clipboard.GetFileDropList().Cast<string>().Where(p => !string.IsNullOrEmpty(p)).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Incolla i file degli appunti nella cartella: copia, oppure sposta se erano stati «tagliati». Usa le finestre di
        /// Windows (avanzamento e conflitti di nome). Dopo uno spostamento gli appunti si svuotano, come in Esplora file.
        /// </summary>
        public static void PasteInto(string folder)
        {
            var files = ClipboardFiles();
            if (files.Count == 0 || !Directory.Exists(folder)) return;
            bool move = false;
            try
            {
                var eff = System.Windows.Clipboard.GetDataObject()?.GetData("Preferred DropEffect") as MemoryStream;
                if (eff != null) { var b = new byte[4]; eff.Position = 0; eff.Read(b, 0, 4); move = (BitConverter.ToInt32(b, 0) & 2) != 0; }
            }
            catch { }
            int done = 0;
            foreach (var src in files)
            {
                try
                {
                    string name = Path.GetFileName(src.TrimEnd('\\'));
                    string dest = Path.Combine(folder, name);
                    if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(src))
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(src, dest, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, dest, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    }
                    else if (File.Exists(src))
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(src, dest, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(src, dest, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    Program.LogError("Incolla " + src, ex);
                    System.Windows.MessageBox.Show($"Impossibile incollare:\n{src}\n\n{ex.Message}", Program.Title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            if (move && done > 0) { try { System.Windows.Clipboard.Clear(); } catch { } }
        }

        /// <summary>La finestra Proprietà di Windows per un file o una cartella.</summary>
        public static void ShowProperties(string path)
        {
            if (!Native.ShowProperties(path)) Program.LogError("Proprietà", new Exception("ShellExecuteEx non riuscito per " + path));
        }

        /// <summary>Terminale di Windows se c'è, altrimenti il prompt dei comandi.</summary>
        public static string TerminalCommand()
        {
            string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
            return File.Exists(wt) ? "wt.exe" : "cmd.exe";
        }

        /// <summary>Se il bersaglio è un collegamento (.lnk), l'eseguibile cui punta (per l'icona); altrimenti il bersaglio stesso.</summary>
        public static string ResolveShortcut(string path)
        {
            try
            {
                if (path == null || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return path;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = shell.CreateShortcut(path);
                string target = lnk.TargetPath as string;
                return string.IsNullOrEmpty(target) ? path : target;
            }
            catch { return path; }
        }

        // ------------------------------------------------------------------ tasto rapido
        /// <summary>«Ctrl+Alt+Space» → modificatori e tasto virtuale per RegisterHotKey. False se il testo non è valido.</summary>
        public static bool ParseHotkey(string text, out uint modifiers, out uint vk, out Key key)
        {
            modifiers = 0; vk = 0; key = Key.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            if (parts.Count == 0) return false;
            string last = parts[parts.Count - 1];
            foreach (var p in parts.Take(parts.Count - 1))
            {
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= Native.MOD_CONTROL; break;
                    case "alt": modifiers |= Native.MOD_ALT; break;
                    case "shift": case "maiusc": modifiers |= Native.MOD_SHIFT; break;
                    case "win": case "windows": modifiers |= Native.MOD_WIN; break;
                    default: return false;
                }
            }
            try
            {
                key = (Key)new KeyConverter().ConvertFromInvariantString(last);
            }
            catch { return false; }
            if (key == Key.None) return false;
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }

        public static string FormatHotkey(ModifierKeys mods, Key key)
        {
            var sb = new StringBuilder();
            if ((mods & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((mods & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            if ((mods & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((mods & ModifierKeys.Windows) != 0) sb.Append("Win+");
            sb.Append(new KeyConverter().ConvertToInvariantString(key));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ avvio con Windows
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Scrive (o toglie) la voce di avvio dell'utente corrente; aggiorna il percorso se l'exe è stato spostato.</summary>
        public static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string value = "\"" + Program.ExePath + "\" --autostart";
                        if (!string.Equals(k.GetValue(Program.Title) as string, value, StringComparison.OrdinalIgnoreCase)) k.SetValue(Program.Title, value);
                    }
                    else if (k.GetValue(Program.Title) != null) k.DeleteValue(Program.Title, false);
                }
            }
            catch (Exception ex) { Program.LogError("Avvio con Windows", ex); }
        }

        public static bool IsAutoStartSet()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k?.GetValue(Program.Title) != null;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ formati
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " byte";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0", Italian) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#", Italian) + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.##", Italian) + " GB";
        }

        public static string FormatDate(DateTime d)
        {
            if (d.Year < 1980) return "";
            var today = DateTime.Today;
            if (d.Date == today) return "oggi " + d.ToString("HH:mm", Italian);
            if (d.Date == today.AddDays(-1)) return "ieri " + d.ToString("HH:mm", Italian);
            return d.ToString(d.Year == today.Year ? "d MMM" : "d MMM yyyy", Italian);
        }

        public static string FormatCount(long n) => n.ToString("N0", Italian);

        /// <summary>Le unità fisse pronte (C:\, D:\…), per l'indice e le impostazioni.</summary>
        public static List<string> FixedDrives()
        {
            var list = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) list.Add(d.RootDirectory.FullName); } catch { }
                }
            }
            catch { }
            return list;
        }
    }
}
