using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WinTools.Search
{
    /// <summary>
    /// Dimensione di una cartella, sommando i file che contiene (come la finestra Proprietà di Windows), con
    /// FindFirstFileEx a blocchi grandi. Si ferma se la si annulla (i risultati sono cambiati) o dopo un tempo
    /// massimo: allora il valore è parziale e si mostra come «almeno». Le ultime cartelle calcolate restano in cache.
    /// </summary>
    public static class FolderSize
    {
        public struct Result { public long Bytes; public bool Partial; }

        static readonly Dictionary<string, Tuple<DateTime, Result>> Cache = new Dictionary<string, Tuple<DateTime, Result>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryCached(string path, out Result r)
        {
            lock (Cache)
            {
                Tuple<DateTime, Result> c;
                if (Cache.TryGetValue(path, out c) && DateTime.Now - c.Item1 < TimeSpan.FromMinutes(5)) { r = c.Item2; return true; }
            }
            r = default(Result);
            return false;
        }

        public static Result Compute(string path, CancellationToken token, TimeSpan maxTime)
        {
            var watch = Stopwatch.StartNew();
            long total = 0;
            bool partial = false;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested) { partial = true; break; }
                if (watch.Elapsed > maxTime) { partial = true; break; }
                string dir = stack.Pop();
                Native.WIN32_FIND_DATA data;
                IntPtr h = Native.FindFirst(dir, out data);
                if (h == Native.INVALID_HANDLE_VALUE) continue;
                try
                {
                    do
                    {
                        string n = data.cFileName;
                        if (n == "." || n == "..") continue;
                        if ((data.dwFileAttributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                        if ((data.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0) stack.Push(dir.EndsWith("\\") ? dir + n : dir + "\\" + n);
                        else total += ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                    } while (Native.FindNextFileW(h, out data));
                }
                finally { Native.FindClose(h); }
            }
            var r = new Result { Bytes = total, Partial = partial };
            if (!token.IsCancellationRequested)
            {
                lock (Cache)
                {
                    if (Cache.Count > 200) Cache.Clear();
                    Cache[path] = Tuple.Create(DateTime.Now, r);
                }
            }
            return r;
        }
    }
}
