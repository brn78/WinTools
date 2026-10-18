using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WinTools.Search
{
    /// <summary>
    /// Le app del menu Start, desktop e Store, lette dalla cartella virtuale «shell:AppsFolder» (la stessa che usa il
    /// menu Start): ogni voce ha un nome e un «parsing name» con cui si avvia (explorer shell:AppsFolder\…) e di cui
    /// la shell fornisce l'icona. Si rilegge all'avvio, ogni ora e quando cambia il menu Start.
    /// </summary>
    public sealed class AppCatalog : IDisposable
    {
        sealed class AppEntry { public string Name, Launch, Folded, Stem; }

        List<AppEntry> _apps = new List<AppEntry>();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        Timer _refresh;
        Timer _debounce;
        int _busy;

        public int Count => _apps.Count;
        public event Action Changed;

        public void Start()
        {
            _debounce = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
            _refresh = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(2), TimeSpan.FromHours(1));
            foreach (var dir in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var w = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                    FileSystemEventHandler h = (s, e) => { try { _debounce?.Change(5000, Timeout.Infinite); } catch { } };
                    w.Created += h; w.Deleted += h; w.Changed += h;
                    w.Renamed += (s, e) => h(s, null);
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch { }
            }
        }

        /// <summary>Rilegge l'elenco delle app (su un thread STA: la cartella virtuale è COM).</summary>
        public void Refresh()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            var t = new Thread(() =>
            {
                try
                {
                    var list = new List<AppEntry>();
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    dynamic folder = shell.NameSpace("shell:AppsFolder");
                    if (folder != null)
                    {
                        dynamic items = folder.Items();
                        int count = items.Count;
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic it = items.Item(i);
                                string name = it.Name as string;
                                string path = it.Path as string;
                                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                                if (!seen.Add(path)) continue;
                                string folded = SearchText.Fold(name);
                                list.Add(new AppEntry { Name = name, Launch = "shell:AppsFolder\\" + path, Folded = folded, Stem = folded });
                            }
                            catch { }
                        }
                    }
                    if (list.Count > 0)
                    {
                        _apps = list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                        try { Changed?.Invoke(); } catch { }
                    }
                }
                catch (Exception ex) { Program.LogError("Elenco app", ex); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            }) { IsBackground = true, Name = "WinTools.Apps" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        /// <summary>Le app con tutte le parole nel nome, già con il punteggio (le app pesano di più dei file).</summary>
        public List<SearchHit> Search(List<string> words, Dictionary<string, int> usage)
        {
            var hits = new List<SearchHit>();
            if (words.Count == 0) return hits;
            foreach (var a in _apps)
            {
                bool ok = true;
                foreach (var w in words) { if (a.Folded.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; } }
                if (!ok) continue;
                int use; usage.TryGetValue(a.Launch, out use);
                hits.Add(new SearchHit
                {
                    Kind = HitKind.App, Title = a.Name, Path = a.Launch, Subtitle = "App", IconKey = a.Launch,
                    Score = Scoring.Score(a.Folded, a.Stem, null, words, DateTime.MinValue, use, 12)
                });
            }
            return hits;
        }

        /// <summary>Le app aperte più spesso, per la casella vuota.</summary>
        public List<SearchHit> Frequent(Dictionary<string, int> usage, int limit)
        {
            return _apps.Where(a => usage.ContainsKey(a.Launch)).OrderByDescending(a => usage[a.Launch]).Take(limit)
                .Select(a => new SearchHit { Kind = HitKind.App, Title = a.Name, Path = a.Launch, Subtitle = "App", IconKey = a.Launch }).ToList();
        }

        public void Dispose()
        {
            foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
            try { _refresh?.Dispose(); } catch { }
            try { _debounce?.Dispose(); } catch { }
        }
    }
}
