using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WinTools.Search
{
    /// <summary>Stato dell'indice per l'interfaccia.</summary>
    public sealed class IndexStatus
    {
        public bool Scanning;
        public long Seen;
        public string Current = "";
        public int Entries;
        public DateTime LastScan;
        public double LastScanSeconds;
        public string Message = "";
        public long MemoryBytes;
    }

    /// <summary>
    /// Indice dei file e delle cartelle locali, in memoria, per la ricerca istantanea (le idee dell'indice di BlackHole:
    /// una lettura completa in sottofondo che scrive una nuova «generazione» e la sostituisce solo alla fine, poi un
    /// FileSystemWatcher per cartella che annota le modifiche; se perde eventi si rilegge tutto).
    ///
    /// Per ogni elemento si tengono nome, cartella (come indice del genitore), dimensione, data e attributi, in array
    /// compatti: i nomi stanno in un unico buffer di caratteri e, accanto, lo stesso buffer «ripiegato» (minuscolo, senza
    /// accenti, un byte per carattere) su cui la ricerca scorre in pochi millisecondi anche con un milione di voci.
    /// L'indice si salva in %LOCALAPPDATA%\WinTools\index.bin: al prossimo avvio la ricerca è pronta subito e la
    /// rilettura completa avviene dopo, con priorità bassa per processore e disco.
    /// </summary>
    public sealed class FileIndex : IDisposable
    {
        const byte FlagDir = 1, FlagHidden = 2;
        const int FileVersion = 2;
        const int MaxCandidates = 30000;
        static readonly DateTime Epoch = new DateTime(2000, 1, 1);

        /// <summary>Data come minuti dal 2000 (4 byte invece di 8); 0 = sconosciuta.</summary>
        static int MinutesOf(long ticks)
        {
            if (ticks <= 0) return 0;
            double m = (ticks - Epoch.Ticks) / (double)TimeSpan.TicksPerMinute;
            return m <= 0 || m > int.MaxValue ? 0 : (int)m;
        }

        static DateTime DateOf(int minutes) => minutes <= 0 ? DateTime.MinValue : Epoch.AddMinutes(minutes);

        // ------------------------------------------------------------------ generazione (immutabile)
        // Per voce: nome in UTF-8 (1 byte per lettera, di solito), lo stesso «ripiegato» (minuscolo, senza accenti,
        // 1 byte per carattere) per la scansione, genitore, attributi, data in minuti. Dimensione e data precisa
        // si leggono dal disco solo per i risultati mostrati.
        sealed class Snapshot
        {
            public int Count;
            public byte[] Utf8;        // tutti i nomi, uno dopo l'altro
            public int[] NameStart;    // Count + 1 posizioni in Utf8
            public byte[] Folded;      // i nomi ripiegati, uno dopo l'altro
            public int[] Start;        // Count + 1 posizioni in Folded
            public int[] Parent;       // -1 per le radici
            public byte[] Flags;
            public int[] Mtime;        // minuti dal 2000 (0 = sconosciuta)

            public string Name(int i) => Encoding.UTF8.GetString(Utf8, NameStart[i], NameStart[i + 1] - NameStart[i]);
            public bool IsDir(int i) => (Flags[i] & FlagDir) != 0;
            public long Bytes => (Utf8?.LongLength ?? 0) + (Folded?.LongLength ?? 0) + Count * 17L;

            public string FullPath(int i)
            {
                // Dalla voce alla radice: i pezzi, poi uniti al contrario
                var parts = new List<string>(8);
                int cur = i;
                while (cur >= 0) { parts.Add(Name(cur)); cur = Parent[cur]; }
                var sb = new StringBuilder(128);
                for (int k = parts.Count - 1; k >= 0; k--)
                {
                    string p = parts[k];
                    if (k == parts.Count - 1) sb.Append(p.TrimEnd('\\'));
                    else sb.Append('\\').Append(p);
                }
                return sb.ToString();
            }

            public string DirPath(int i)
            {
                int p = Parent[i];
                return p < 0 ? "" : FullPath(p);
            }
        }

        // ------------------------------------------------------------------ costruttore della generazione
        sealed class Builder
        {
            byte[] _utf8 = new byte[1 << 20];
            int _utf8Len;
            byte[] _folded = new byte[1 << 20];
            int _foldedLen;
            readonly List<int> _nameStart = new List<int>(65536) { 0 };
            readonly List<int> _start = new List<int>(65536) { 0 };
            readonly List<int> _parent = new List<int>(65536);
            readonly List<byte> _flags = new List<byte>(65536);
            readonly List<int> _mtime = new List<int>(65536);
            readonly byte[] _tmp = new byte[1024];
            public int Count => _parent.Count;

            static void Ensure(ref byte[] buf, int needed)
            {
                if (needed <= buf.Length) return;
                int n = buf.Length;
                while (n < needed) n = n < (1 << 28) ? n * 2 : n + (1 << 27);
                Array.Resize(ref buf, n);
            }

            public int Add(string name, int parent, byte flags, int mtimeMinutes)
            {
                byte[] enc = name.Length * 3 <= _tmp.Length ? _tmp : new byte[name.Length * 3];
                int n = Encoding.UTF8.GetBytes(name, 0, name.Length, enc, 0);
                Ensure(ref _utf8, _utf8Len + n);
                Buffer.BlockCopy(enc, 0, _utf8, _utf8Len, n);
                _utf8Len += n;
                _nameStart.Add(_utf8Len);
                Ensure(ref _folded, _foldedLen + name.Length);
                for (int i = 0; i < name.Length; i++) _folded[_foldedLen + i] = SearchText.FoldByte(name[i]);
                _foldedLen += name.Length;
                _start.Add(_foldedLen);
                _parent.Add(parent);
                _flags.Add(flags);
                _mtime.Add(mtimeMinutes);
                return _parent.Count - 1;
            }

            public Snapshot Build()
            {
                var s = new Snapshot
                {
                    Count = Count,
                    Utf8 = new byte[_utf8Len],
                    Folded = new byte[_foldedLen],
                    NameStart = _nameStart.ToArray(),
                    Start = _start.ToArray(),
                    Parent = _parent.ToArray(),
                    Flags = _flags.ToArray(),
                    Mtime = _mtime.ToArray()
                };
                Buffer.BlockCopy(_utf8, 0, s.Utf8, 0, _utf8Len);
                Buffer.BlockCopy(_folded, 0, s.Folded, 0, _foldedLen);
                return s;
            }
        }

        /// <summary>Dopo una generazione nuova: la vecchia e i buffer di costruzione tornano al sistema.</summary>
        static void Compact()
        {
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            catch { }
        }

        // ------------------------------------------------------------------ modifiche dopo l'ultima generazione
        sealed class Added
        {
            public string Name, Dir, Folded, FoldedPath;
            public bool IsDir;
            public long Size;
            public DateTime Modified;
        }

        readonly object _deltaGate = new object();
        Dictionary<string, Added> _added = new Dictionary<string, Added>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> _removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> _removedDirs = new List<string>();

        // ------------------------------------------------------------------ stato
        Snapshot _snap = new Builder().Build();
        IndexSettings _settings = new IndexSettings();
        readonly IndexStatus _status = new IndexStatus();
        CancellationTokenSource _scanCts;
        Thread _scanThread;
        readonly object _scanGate = new object();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        readonly ConcurrentDictionary<string, byte> _pending = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        Timer _debounce;
        Timer _periodic;
        int _changeBusy;
        bool _disposed;

        public event Action StatusChanged;
        public IndexStatus Status { get { lock (_status) return new IndexStatus { Scanning = _status.Scanning, Seen = _status.Seen, Current = _status.Current, Entries = _snap.Count, LastScan = _status.LastScan, LastScanSeconds = _status.LastScanSeconds, Message = _status.Message, MemoryBytes = MemoryEstimate(_snap) }; } }
        public int Count => _snap.Count;

        static long MemoryEstimate(Snapshot s) => s.Bytes;

        void Notify() { try { StatusChanged?.Invoke(); } catch { } }

        // ------------------------------------------------------------------ avvio e configurazione
        /// <summary>
        /// Carica l'indice salvato (se c'è, in pochi decimi di secondo), avvia i watcher e programma la rilettura
        /// completa. startupDelay = quanto aspettare prima di leggere il disco (all'avvio di Windows: il PC ha già da fare);
        /// i watcher partono comunque subito, così le modifiche nel frattempo non si perdono.
        /// </summary>
        public void Start(IndexSettings settings, TimeSpan startupDelay)
        {
            _settings = settings;
            _debounce = new Timer(_ => ProcessChanges(), null, Timeout.Infinite, Timeout.Infinite);
            var t = new Thread(() =>
            {
                bool loaded = Load();
                if (_settings.Enabled)
                {
                    StartWatchers();
                    bool stale = !loaded || (_settings.RescanHours > 0 && DateTime.Now - _status.LastScan > TimeSpan.FromHours(_settings.RescanHours));
                    if (stale)
                    {
                        var wait = startupDelay > TimeSpan.Zero ? startupDelay : (loaded ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
                        if (wait > TimeSpan.Zero)
                        {
                            if (startupDelay > TimeSpan.Zero) lock (_status) _status.Message = $"Rilettura dei file tra {Math.Max(1, Math.Round(wait.TotalMinutes))} min (avvio di Windows).";
                            Notify();
                            var until = DateTime.Now + wait;
                            while (!_disposed && DateTime.Now < until) Thread.Sleep(500);
                            lock (_status) _status.Message = "";
                        }
                        if (!_disposed) Rescan();
                    }
                }
                Notify();
                _periodic = new Timer(_ => PeriodicCheck(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            }) { IsBackground = true, Name = "WinTools.IndexStart" };
            t.Start();
        }

        void PeriodicCheck()
        {
            if (_disposed || !_settings.Enabled || _settings.RescanHours <= 0) return;
            if (!_status.Scanning && DateTime.Now - _status.LastScan > TimeSpan.FromHours(_settings.RescanHours)) Rescan();
        }

        /// <summary>Nuove impostazioni: watcher riallineati e rilettura completa.</summary>
        public void Reconfigure(IndexSettings settings)
        {
            _settings = settings;
            StopWatchers();
            if (!settings.Enabled)
            {
                CancelScan();
                _snap = new Builder().Build();
                lock (_deltaGate) { _added.Clear(); _removed.Clear(); _removedDirs.Clear(); }
                try { File.Delete(AppSettings.IndexPath); } catch { }
                lock (_status) { _status.Message = "Indice disattivato."; _status.LastScan = DateTime.MinValue; }
                Notify();
                return;
            }
            StartWatchers();
            Rescan();
        }

        public void CancelScan()
        {
            var c = _scanCts;
            if (c != null) { try { c.Cancel(); } catch { } }
        }

        // ------------------------------------------------------------------ radici ed esclusioni
        List<string> Roots()
        {
            var roots = _settings.Roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => Environment.ExpandEnvironmentVariables(r.Trim())).ToList();
            if (roots.Count == 0) roots = Tools.FixedDrives();
            var ok = new List<string>();
            foreach (var r in roots)
            {
                try
                {
                    string full = Path.GetFullPath(r);
                    if (full.Length > 3) full = full.TrimEnd('\\');
                    if (Directory.Exists(full) && !ok.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase))) ok.Add(full);
                }
                catch { }
            }
            return ok;
        }

        sealed class Excludes
        {
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Paths = new List<string>();

            public Excludes(IEnumerable<string> rules)
            {
                foreach (var raw in rules ?? Enumerable.Empty<string>())
                {
                    string r = Environment.ExpandEnvironmentVariables((raw ?? "").Trim());
                    if (r == "") continue;
                    if (r.IndexOf('\\') >= 0 || r.IndexOf(':') >= 0) Paths.Add(r.TrimEnd('\\'));
                    else Names.Add(r);
                }
                // La cartella dell'app (l'indice stesso, i log) non si indicizza mai
                Paths.Add(AppSettings.AppDir.TrimEnd('\\'));
            }

            public bool SkipDir(string name, string fullPath)
            {
                if (Names.Contains(name)) return true;
                foreach (var p in Paths)
                    if (fullPath.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            public bool Inside(string fullPath)
            {
                foreach (var p in Paths)
                    if (fullPath.Equals(p, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                var parts = fullPath.Split('\\');
                for (int i = 1; i < parts.Length - 1; i++) if (Names.Contains(parts[i])) return true;
                return false;
            }
        }

        static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thumbs.db", "desktop.ini", ".ds_store" };

        // ------------------------------------------------------------------ rilettura completa
        /// <summary>Rilegge tutte le cartelle in sottofondo in una nuova generazione; quella in corso viene fermata.</summary>
        public void Rescan()
        {
            lock (_scanGate)
            {
                CancelScan();
                var prev = _scanThread;
                if (prev != null && prev.IsAlive) prev.Join(5000);
                var cts = new CancellationTokenSource();
                _scanCts = cts;
                _scanThread = new Thread(() => ScanAll(cts.Token)) { IsBackground = true, Name = "WinTools.IndexScan", Priority = ThreadPriority.BelowNormal };
                _scanThread.Start();
            }
        }

        sealed class Work { public string Path; public int Id; }

        void ScanAll(CancellationToken token)
        {
            var roots = Roots();
            var excludes = new Excludes(_settings.Exclude);
            bool hidden = _settings.IncludeHidden;
            lock (_status) { _status.Scanning = true; _status.Seen = 0; _status.Current = ""; _status.Message = ""; }
            Notify();
            var watch = Stopwatch.StartNew();
            var builder = new Builder();
            var queue = new ConcurrentQueue<Work>();
            var signal = new SemaphoreSlim(0);
            int pending = 0;
            bool done = false;
            long seen = 0;
            int workers = Math.Max(2, Math.Min(8, Environment.ProcessorCount));

            lock (builder)
            {
                foreach (var r in roots)
                {
                    int id = builder.Add(r, -1, FlagDir, 0);
                    Interlocked.Increment(ref pending);
                    queue.Enqueue(new Work { Path = r, Id = id });
                    signal.Release();
                }
            }
            if (pending == 0) { done = true; }

            var threads = new List<Thread>();
            for (int w = 0; w < workers; w++)
            {
                var t = new Thread(() =>
                {
                    bool bg = Native.BeginBackgroundMode();
                    try
                    {
                        var subdirs = new List<Work>(64);
                        while (true)
                        {
                            signal.Wait();
                            if (done || token.IsCancellationRequested) break;
                            Work work;
                            if (!queue.TryDequeue(out work)) continue;
                            try
                            {
                                subdirs.Clear();
                                ScanDir(work, builder, excludes, hidden, subdirs, ref seen, token);
                                foreach (var sd in subdirs) { Interlocked.Increment(ref pending); queue.Enqueue(sd); signal.Release(); }
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex) { Program.LogError("Indice: " + work.Path, ex); }
                            finally
                            {
                                if (Interlocked.Decrement(ref pending) == 0) { done = true; signal.Release(workers); }
                            }
                        }
                    }
                    finally { if (bg) Native.EndBackgroundMode(); }
                }) { IsBackground = true, Name = "WinTools.IndexWorker", Priority = ThreadPriority.BelowNormal };
                threads.Add(t);
                t.Start();
            }

            // Avanzamento per l'interfaccia, ogni mezzo secondo
            while (!done && !token.IsCancellationRequested)
            {
                Thread.Sleep(500);
                lock (_status) _status.Seen = Interlocked.Read(ref seen);
                Notify();
            }
            if (token.IsCancellationRequested) { done = true; signal.Release(workers * 2); }
            foreach (var t in threads) t.Join();

            if (token.IsCancellationRequested)
            {
                lock (_status) { _status.Scanning = false; _status.Current = ""; _status.Message = _snap.Count > 0 ? "Aggiornamento fermato: resta l'indice di prima." : ""; }
                Notify();
                return;
            }

            Snapshot snap;
            try { snap = builder.Build(); }
            catch (OutOfMemoryException)
            {
                lock (_status) { _status.Scanning = false; _status.Message = "Troppi file: escludi qualche cartella dalle impostazioni."; }
                Notify();
                return;
            }
            // La nuova generazione prende il posto della vecchia; le modifiche annotate nel frattempo sono già dentro
            _snap = snap;
            builder = null;
            lock (_deltaGate) { _added.Clear(); _removed.Clear(); _removedDirs.Clear(); _pending.Clear(); }
            lock (_status)
            {
                _status.Scanning = false; _status.Current = ""; _status.LastScan = DateTime.Now; _status.LastScanSeconds = watch.Elapsed.TotalSeconds;
                _status.Seen = snap.Count; _status.Message = "";
            }
            Compact();
            Notify();
            Save(snap, _status.LastScan);
        }

        void ScanDir(Work work, Builder builder, Excludes excludes, bool includeHidden, List<Work> subdirs, ref long seen, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_status) _status.Current = work.Path;
            Native.WIN32_FIND_DATA data;
            IntPtr h = Native.FindFirst(work.Path, out data);
            if (h == Native.INVALID_HANDLE_VALUE) return;
            var names = new List<string>(256);
            var flags = new List<byte>(256);
            var times = new List<int>(256);
            try
            {
                do
                {
                    string n = data.cFileName;
                    if (n == "." || n == "..") continue;
                    uint a = data.dwFileAttributes;
                    bool isDir = (a & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    bool hidden = (a & (Native.FILE_ATTRIBUTE_HIDDEN | Native.FILE_ATTRIBUTE_SYSTEM)) != 0;
                    if (hidden && !includeHidden) continue;
                    if ((a & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;   // collegamenti e punti di montaggio: niente cicli
                    if (!isDir && (SkipNames.Contains(n) || n.StartsWith("~$", StringComparison.Ordinal))) continue;
                    if (isDir && excludes.SkipDir(n, Combine(work.Path, n))) continue;
                    names.Add(n);
                    flags.Add((byte)((isDir ? FlagDir : 0) | (hidden ? FlagHidden : 0)));
                    times.Add(MinutesOf(Native.ToTicks(data.ftLastWriteTime)));
                } while (Native.FindNextFileW(h, out data));
            }
            finally { Native.FindClose(h); }
            if (names.Count == 0) return;
            Interlocked.Add(ref seen, names.Count);
            lock (builder)
            {
                if (builder.Count + names.Count > 6000000) throw new InvalidOperationException("Troppi elementi: escludi qualche cartella.");
                for (int i = 0; i < names.Count; i++)
                {
                    int id = builder.Add(names[i], work.Id, flags[i], times[i]);
                    if ((flags[i] & FlagDir) != 0) subdirs.Add(new Work { Path = Combine(work.Path, names[i]), Id = id });
                }
            }
        }

        static string Combine(string dir, string name) => dir.EndsWith("\\") ? dir + name : dir + "\\" + name;

        // ------------------------------------------------------------------ salvataggio e caricamento
        void Save(Snapshot s, DateTime when)
        {
            bool bg = Native.BeginBackgroundMode();
            try
            {
                string tmp = AppSettings.IndexPath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8))
                {
                    bw.Write(new[] { (byte)'W', (byte)'T', (byte)'I', (byte)'X' });
                    bw.Write(FileVersion);
                    bw.Write(when.Ticks);
                    bw.Write(s.Count);
                    bw.Write(s.Utf8.Length);
                    bw.Write(s.Folded.Length);
                    bw.Write(s.Utf8);
                    bw.Write(s.Folded);
                    for (int i = 0; i <= s.Count; i++) bw.Write(s.NameStart[i]);
                    for (int i = 0; i <= s.Count; i++) bw.Write(s.Start[i]);
                    for (int i = 0; i < s.Count; i++) bw.Write(s.Parent[i]);
                    bw.Write(s.Flags);
                    for (int i = 0; i < s.Count; i++) bw.Write(s.Mtime[i]);
                }
                if (File.Exists(AppSettings.IndexPath)) File.Replace(tmp, AppSettings.IndexPath, null); else File.Move(tmp, AppSettings.IndexPath);
            }
            catch (Exception ex) { Program.LogError("Salvataggio indice", ex); }
            finally { if (bg) Native.EndBackgroundMode(); }
        }

        bool Load()
        {
            try
            {
                if (!File.Exists(AppSettings.IndexPath)) return false;
                using (var fs = new FileStream(AppSettings.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                using (var br = new BinaryReader(fs, Encoding.UTF8))
                {
                    var magic = br.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != 'W' || magic[1] != 'T' || magic[2] != 'I' || magic[3] != 'X') return false;
                    if (br.ReadInt32() != FileVersion) return false;
                    long ticks = br.ReadInt64();
                    int count = br.ReadInt32();
                    int utf8Len = br.ReadInt32();
                    int foldedLen = br.ReadInt32();
                    if (count < 0 || utf8Len < 0 || foldedLen < 0 || count > 10000000) return false;
                    var s = new Snapshot { Count = count, NameStart = new int[count + 1], Start = new int[count + 1], Parent = new int[count], Mtime = new int[count] };
                    s.Utf8 = br.ReadBytes(utf8Len);
                    s.Folded = br.ReadBytes(foldedLen);
                    if (s.Utf8.Length != utf8Len || s.Folded.Length != foldedLen) return false;
                    for (int i = 0; i <= count; i++) s.NameStart[i] = br.ReadInt32();
                    for (int i = 0; i <= count; i++) s.Start[i] = br.ReadInt32();
                    for (int i = 0; i < count; i++) s.Parent[i] = br.ReadInt32();
                    s.Flags = br.ReadBytes(count);
                    if (s.Flags.Length != count) return false;
                    for (int i = 0; i < count; i++) s.Mtime[i] = br.ReadInt32();
                    _snap = s;
                    lock (_status) { _status.LastScan = new DateTime(ticks); _status.Seen = count; }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Program.LogError("Lettura indice", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ aggiornamento continuo
        void StartWatchers()
        {
            StopWatchers();
            foreach (var r in Roots())
            {
                try
                {
                    var w = new FileSystemWatcher(r)
                    {
                        IncludeSubdirectories = true,
                        InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    w.Created += (s, e) => Queue(e.FullPath);
                    w.Changed += (s, e) => Queue(e.FullPath);
                    w.Deleted += (s, e) => Queue(e.FullPath);
                    w.Renamed += (s, e) => { Queue(e.OldFullPath); Queue(e.FullPath); };
                    w.Error += (s, e) =>
                    {
                        // Eventi persi (troppe modifiche insieme): si rilegge tutto, con calma
                        if (!_status.Scanning) Rescan();
                    };
                    w.EnableRaisingEvents = true;
                    lock (_watchers) _watchers.Add(w);
                }
                catch (Exception ex) { Program.LogError("Watcher " + r, ex); }
            }
        }

        void StopWatchers()
        {
            lock (_watchers)
            {
                foreach (var w in _watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
                _watchers.Clear();
            }
        }

        void Queue(string path)
        {
            if (string.IsNullOrEmpty(path) || _disposed) return;
            string name = Path.GetFileName(path);
            if (SkipNames.Contains(name) || name.StartsWith("~$", StringComparison.Ordinal)) return;
            _pending.TryAdd(path, 0);
            // Troppe modifiche in coda (installazioni, copie di cartelle intere): meglio rileggere tutto
            if (_pending.Count > 20000) { _pending.Clear(); if (!_status.Scanning) Rescan(); return; }
            try { _debounce?.Change(2000, Timeout.Infinite); } catch { }
        }

        void ProcessChanges()
        {
            if (Interlocked.CompareExchange(ref _changeBusy, 1, 0) != 0) { try { _debounce?.Change(2000, Timeout.Infinite); } catch { } return; }
            try
            {
                if (_status.Scanning) { try { _debounce?.Change(5000, Timeout.Infinite); } catch { } return; }
                var excludes = new Excludes(_settings.Exclude);
                var paths = _pending.Keys.ToList();
                foreach (var p in paths) { byte b; _pending.TryRemove(p, out b); }
                var snap = _snap;
                lock (_deltaGate)
                {
                    foreach (var p in paths)
                    {
                        if (excludes.Inside(p)) continue;
                        try
                        {
                            if (File.Exists(p))
                            {
                                var fi = new FileInfo(p);
                                if (!_settings.IncludeHidden && (fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) { Remove(p); continue; }
                                Add(p, false, fi.Length, fi.LastWriteTime);
                            }
                            else if (Directory.Exists(p))
                            {
                                var di = new DirectoryInfo(p);
                                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                                if (!_settings.IncludeHidden && (di.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) { Remove(p); continue; }
                                Add(p, true, 0, di.LastWriteTime);
                                _removedDirs.RemoveAll(d => d.Equals(p, StringComparison.OrdinalIgnoreCase));
                            }
                            else Remove(p);
                        }
                        catch { }
                    }
                    // Troppe differenze dall'ultima generazione: si rifà l'indice (in sottofondo)
                    if (_added.Count + _removed.Count > 5000) Rescan();
                }
                Notify();
            }
            catch (Exception ex) { Program.LogError("Modifiche all'indice", ex); }
            finally { Interlocked.Exchange(ref _changeBusy, 0); }
        }

        void Add(string path, bool isDir, long size, DateTime modified)
        {
            string name = Path.GetFileName(path);
            string dir = Path.GetDirectoryName(path) ?? "";
            _added[path] = new Added { Name = name, Dir = dir, Folded = SearchText.Fold(name), FoldedPath = SearchText.Fold(dir), IsDir = isDir, Size = size, Modified = modified };
            _removed.Remove(path);
        }

        void Remove(string path)
        {
            _added.Remove(path);
            _removed.Add(path);
            // Una cartella sparita porta via tutto il suo contenuto
            if (!_removedDirs.Any(d => d.Equals(path, StringComparison.OrdinalIgnoreCase))) _removedDirs.Add(path);
            var gone = _added.Keys.Where(k => k.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var g in gone) _added.Remove(g);
        }

        bool IsRemoved(string fullPath)
        {
            if (_removed.Contains(fullPath)) return true;
            foreach (var d in _removedDirs)
                if (fullPath.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ ricerca
        /// <summary>
        /// File e cartelle con tutte le parole nel nome, oppure la più lunga nel nome e le altre nel percorso.
        /// Il buffer ripiegato si scorre una volta sola cercando la parola più lunga; ogni voce trovata si verifica
        /// per le altre parole. Al massimo limit risultati, ordinati per pertinenza.
        /// </summary>
        public List<SearchHit> Search(List<string> words, int limit, Dictionary<string, int> usage)
        {
            var result = new List<SearchHit>();
            var snap = _snap;
            if (words == null || words.Count == 0) return result;
            string longest = words.OrderByDescending(w => w.Length).First();
            var pattern = SearchText.FoldBytes(longest);
            var others = words.Where(w => !ReferenceEquals(w, longest)).ToList();
            var otherBytes = others.Select(SearchText.FoldBytes).ToList();

            var candidates = new List<int>(1024);
            var needPath = new List<int>();    // voci con la parola più lunga nel nome ma non tutte le altre
            byte[] f = snap.Folded;
            int[] start = snap.Start;
            int n = f.Length, pl = pattern.Length;
            if (pl == 0 || snap.Count == 0) { AddDelta(result, words, usage, limit); return Rank(result, limit); }
            byte first = pattern[0];
            int e = 0;
            for (int i = 0; i <= n - pl; i++)
            {
                if (f[i] != first) continue;
                int k = 1;
                while (k < pl && f[i + k] == pattern[k]) k++;
                if (k < pl) continue;
                while (start[e + 1] <= i) e++;
                if (i + pl > start[e + 1]) { i = start[e + 1] - 1; continue; }   // a cavallo di due nomi
                // Le altre parole nello stesso nome
                bool all = true;
                foreach (var ob in otherBytes) { if (!Contains(f, start[e], start[e + 1], ob)) { all = false; break; } }
                if (all) candidates.Add(e); else if (others.Count > 0) needPath.Add(e);
                if (candidates.Count >= MaxCandidates) break;
                i = start[e + 1] - 1;
            }
            // Le parole mancanti nel nome possono stare nel percorso (al massimo per qualche migliaio di voci)
            if (needPath.Count > 0 && candidates.Count < limit * 4)
            {
                int checkedCount = 0;
                foreach (var idx in needPath)
                {
                    if (++checkedCount > 20000) break;
                    string path = SearchText.Fold(snap.DirPath(idx));
                    bool ok = true;
                    string name = SearchText.Fold(snap.Name(idx));
                    foreach (var w in others) { if (name.IndexOf(w, StringComparison.Ordinal) < 0 && path.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; } }
                    if (ok) candidates.Add(idx);
                    if (candidates.Count >= MaxCandidates) break;
                }
            }

            lock (_deltaGate)
            {
                foreach (var idx in candidates)
                {
                    string full = snap.FullPath(idx);
                    if (IsRemoved(full) || _added.ContainsKey(full)) continue;
                    bool isDir = snap.IsDir(idx);
                    string name = snap.Name(idx);
                    string dir = snap.DirPath(idx);
                    string fname = SearchText.Fold(name);
                    string stem = isDir ? fname : SearchText.Fold(Path.GetFileNameWithoutExtension(name));
                    int use; usage.TryGetValue(full, out use);
                    var modified = DateOf(snap.Mtime[idx]);
                    double score = Scoring.Score(fname, stem, SearchText.Fold(dir), words, modified, use, isDir ? 4 : 2);
                    if (!isDir && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) score += 3;
                    result.Add(new SearchHit
                    {
                        Kind = isDir ? HitKind.Folder : HitKind.File, Title = name, Path = full, Subtitle = dir, Size = -1, Modified = modified,
                        Score = score, IconKey = full
                    });
                }
                AddDelta(result, words, usage, limit);
            }
            return Rank(result, limit);
        }

        void AddDelta(List<SearchHit> result, List<string> words, Dictionary<string, int> usage, int limit)
        {
            foreach (var kv in _added)
            {
                var a = kv.Value;
                bool ok = true;
                foreach (var w in words) { if (a.Folded.IndexOf(w, StringComparison.Ordinal) < 0 && a.FoldedPath.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; } }
                if (!ok) continue;
                int use; usage.TryGetValue(kv.Key, out use);
                string stem = a.IsDir ? a.Folded : SearchText.Fold(Path.GetFileNameWithoutExtension(a.Name));
                result.Add(new SearchHit
                {
                    Kind = a.IsDir ? HitKind.Folder : HitKind.File, Title = a.Name, Path = kv.Key, Subtitle = a.Dir, Size = a.Size, Modified = a.Modified,
                    Score = Scoring.Score(a.Folded, stem, a.FoldedPath, words, a.Modified, use, a.IsDir ? 4 : 2), IconKey = kv.Key
                });
            }
        }

        static List<SearchHit> Rank(List<SearchHit> hits, int limit)
        {
            var top = hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Modified).Take(limit).ToList();
            // Dimensione e data precisa dal disco, solo per i risultati che si mostrano
            foreach (var h in top)
            {
                if (h.Kind != HitKind.File || h.Size >= 0) continue;
                try { var fi = new FileInfo(h.Path); h.Size = fi.Length; h.Modified = fi.LastWriteTime; } catch { h.Size = 0; }
            }
            return top;
        }

        static bool Contains(byte[] buf, int from, int to, byte[] pat)
        {
            int pl = pat.Length;
            for (int i = from; i + pl <= to; i++)
            {
                int k = 0;
                while (k < pl && buf[i + k] == pat[k]) k++;
                if (k == pl) return true;
            }
            return pl == 0;
        }

        /// <summary>Le parole dei nomi (per il «forse cercavi»): un campione, non tutto l'indice.</summary>
        public string SuggestWord(string word)
        {
            if (word.Length < 4) return null;
            var snap = _snap;
            int max = word.Length <= 5 ? 1 : 2;
            string best = null; int bestD = int.MaxValue;
            // Campione: una voce ogni k, al massimo 60.000 nomi
            int step = Math.Max(1, snap.Count / 60000);
            for (int i = 0; i < snap.Count && bestD > 1; i += step)
            {
                string name = SearchText.Fold(snap.Name(i));
                foreach (var part in name.Split(new[] { ' ', '.', '-', '_', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length < 4 || Math.Abs(part.Length - word.Length) > max) continue;
                    if (part[0] != word[0] && part[part.Length - 1] != word[word.Length - 1]) continue;
                    int d = SearchText.Distance(word, part, max);
                    if (d <= max && d < bestD) { bestD = d; best = part; if (d == 1 && part[0] == word[0]) return part; }
                }
            }
            return best;
        }

        public void Dispose()
        {
            _disposed = true;
            CancelScan();
            StopWatchers();
            try { _debounce?.Dispose(); } catch { }
            try { _periodic?.Dispose(); } catch { }
        }
    }
}
