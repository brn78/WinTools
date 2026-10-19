using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinTools
{
    /// <summary>
    /// Icone di file, cartelle e app, chieste alla shell su un thread a parte (le prime volte possono richiedere decine
    /// di millisecondi) e consegnate all'interfaccia appena pronte. Al massimo qualche centinaio in memoria.
    /// </summary>
    public static class IconCache
    {
        sealed class Request { public string Key; public int Size; public Action<ImageSource> Done; public Dispatcher Dispatcher; }

        static readonly ConcurrentDictionary<string, ImageSource> Cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly BlockingCollection<Request> Queue = new BlockingCollection<Request>();
        static Thread _worker;

        static void EnsureWorker()
        {
            if (_worker != null) return;
            _worker = new Thread(() =>
            {
                foreach (var r in Queue.GetConsumingEnumerable())
                {
                    ImageSource img = null;
                    try { img = Load(r.Key, r.Size); } catch { }
                    if (img != null)
                    {
                        if (Cache.Count > 600) Cache.Clear();
                        Cache[r.Key + "|" + r.Size] = img;
                    }
                    var done = r.Done;
                    if (done != null) r.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => done(img)));
                }
            }) { IsBackground = true, Name = "WinTools.Icons" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        /// <summary>
        /// L'icona dalla cache (subito) oppure null, con la consegna asincrona di quella caricata. Chiavi: un percorso,
        /// «shell:AppsFolder\…», oppure un file immagine .png/.ico/.jpg.
        /// </summary>
        public static ImageSource Get(string key, int size, Action<ImageSource> onLoaded)
        {
            if (string.IsNullOrEmpty(key)) return null;
            ImageSource cached;
            if (Cache.TryGetValue(key + "|" + size, out cached)) return cached;
            EnsureWorker();
            Queue.Add(new Request { Key = key, Size = size, Done = onLoaded, Dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher });
            return null;
        }

        static ImageSource Load(string key, int size)
        {
            // In pixel reali: a 150% una tessera da 32 punti sono 48 pixel
            int px = (int)Math.Round(size * Dpi());
            string ext = "";
            try { ext = Path.GetExtension(key).ToLowerInvariant(); } catch { }
            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp") && File.Exists(key))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = px;
                bi.UriSource = new Uri(key);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            string path = key;
            if (key.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase)) path = key.Substring("shell:".Length);   // «::{CLSID}» è il nome di analisi
            else if (!key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                path = Environment.ExpandEnvironmentVariables(key);
                // Un comando senza percorso (notepad.exe, calc.exe): lo si cerca nel PATH per avere la sua icona
                if (!Path.IsPathRooted(path) && !File.Exists(path) && !Directory.Exists(path))
                {
                    string found = FindOnPath(path);
                    if (found != null) path = found;
                    else if (path.IndexOf(':') > 0) return null;   // ms-settings:, http:// … niente icona dalla shell
                }
            }
            return Native.GetShellIcon(path, px);
        }

        static double _dpi;
        static double Dpi()
        {
            if (_dpi > 0) return _dpi;
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) _dpi = g.DpiX / 96.0;
            }
            catch { _dpi = 1; }
            return _dpi;
        }

        public static string FindOnPath(string file)
        {
            try
            {
                if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                var dirs = new List<string> { Environment.CurrentDirectory, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
                dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
                var exts = new List<string> { "" };
                if (!Path.HasExtension(file)) exts.AddRange(new[] { ".exe", ".com", ".bat", ".cmd", ".lnk" });
                foreach (var d in dirs)
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    foreach (var e in exts)
                    {
                        string p = Path.Combine(d.Trim(), file + e);
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void Forget(string key)
        {
            foreach (var k in Cache.Keys) if (k.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)) { ImageSource v; Cache.TryRemove(k, out v); }
        }
    }
}
