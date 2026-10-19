using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace WinTools
{
    /// <summary>
    /// L'archivio di WinTools (%LOCALAPPDATA%\WinTools\Archivio\{id del tasto}\…): copie dei file trascinati sulla barra e
    /// file .rdp generati. Un tasto che punta qui non dipende più dall'originale, che può essere spostato o eliminato.
    /// Quando il tasto viene rimosso, la sua cartella va nel Cestino.
    /// </summary>
    public static class ArchiveStore
    {
        public static string Root => AppSettings.ArchiveDir;
        public static string DirFor(string id) => Path.Combine(Root, id);

        public static string Expand(string target) => Environment.ExpandEnvironmentVariables((target ?? "").Trim().Trim('"'));

        public static bool IsArchived(string target)
        {
            string t = Expand(target);
            return t.StartsWith(Root + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Vero se il tasto apre un file (non una cartella, un comando o un elemento della shell) che esiste.</summary>
        public static bool IsFileTarget(BarButton b)
        {
            if (b == null || b.Separator || b.Group) return false;
            string t = Expand(b.Target);
            return t.Length > 2 && Path.IsPathRooted(t) && File.Exists(t);
        }

        public static bool SizeOk(string target, int maxMb)
        {
            try { return new FileInfo(Expand(target)).Length <= (long)maxMb * 1024 * 1024; } catch { return false; }
        }

        /// <summary>Copia il file del tasto nell'archivio e punta il tasto alla copia (ricordando l'originale).</summary>
        public static bool Archive(BarButton b)
        {
            string src = Expand(b.Target);
            if (!Path.IsPathRooted(src) || !File.Exists(src) || IsArchived(src)) return false;
            string dir = DirFor(b.Id);
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(src));
            File.Copy(src, dest, true);
            b.Origin = src;
            b.Target = dest;
            return true;
        }

        public static int ArchiveAll(IEnumerable<BarButton> buttons)
        {
            int n = 0;
            foreach (var b in buttons)
            {
                try { if (Archive(b)) n++; }
                catch (Exception ex) { Program.LogError("Archivio", ex); }
            }
            return n;
        }

        /// <summary>L'originale esiste ancora: si può aggiornare la copia.</summary>
        public static bool CanRefresh(BarButton b)
        {
            return b != null && IsArchived(b.Target) && !string.IsNullOrEmpty(b.Origin) && File.Exists(Expand(b.Origin));
        }

        /// <summary>Ricopia l'originale sopra la copia nell'archivio.</summary>
        public static bool Refresh(BarButton b)
        {
            if (!CanRefresh(b)) return false;
            File.Copy(Expand(b.Origin), Expand(b.Target), true);
            return true;
        }

        /// <summary>La cartella del tasto nell'archivio (e quelle dei tasti dentro un contenitore) va nel Cestino.</summary>
        public static void DeleteFor(BarButton b)
        {
            if (b == null) return;
            try
            {
                if (b.Children != null) foreach (var c in b.Children) DeleteFor(c);
                string dir = DirFor(b.Id);
                if (Directory.Exists(dir)) FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex) { Program.LogError("Archivio", ex); }
        }
    }
}
