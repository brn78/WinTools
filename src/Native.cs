using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinTools
{
    /// <summary>
    /// Funzioni di Windows usate dall'app: DWM (angoli, tema), tasti rapidi globali, cursore, finestre,
    /// enumerazione veloce dei file (FindFirstFileEx), icone della shell e priorità «in sottofondo» dei thread.
    /// Ogni chiamata è avvolta in modo che un sistema senza la funzione non faccia cadere l'app.
    /// </summary>
    internal static class Native
    {
        // ------------------------------------------------------------------ DWM / aspetto finestra
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref v, 4);
            }
            catch { }
        }

        /// <summary>Angoli arrotondati di Windows 11 (2 = arrotondati, 3 = arrotondati piccoli, 1 = squadrati).</summary>
        public static void SetCornerPreference(IntPtr hwnd, int preference)
        {
            try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, 4); } catch { }
        }

        /// <summary>Materiale Mica (2) sulla finestra: solo Windows 11 22H2+. Restituisce false se non supportato.</summary>
        public static bool SetBackdrop(IntPtr hwnd, int type)
        {
            try { return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) == 0; } catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);

        /// <summary>Materiale Mica su tutta la finestra (Windows 11 22H2+), come in WinOptima. False se non supportato.</summary>
        public static bool EnableMica(IntPtr hwnd)
        {
            try
            {
                if (!SupportsMica) return false;
                int backdrop = 2;   // DWMSBT_MAINWINDOW
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) != 0) return false;
                var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                return DwmExtendFrameIntoClientArea(hwnd, ref m) == 0;
            }
            catch { return false; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;
        public static bool SupportsMica => Environment.OSVersion.Version.Build >= 22621;

        // ------------------------------------------------------------------ stile finestra
        [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>La finestra non compare in Alt+Tab (è una barra, non un'app).</summary>
        public static void MakeToolWindow(IntPtr hwnd)
        {
            try { SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW); } catch { }
        }

        /// <summary>Con true la finestra non prende il fuoco quando compare (apertura dal bordo).</summary>
        public static void SetNoActivate(IntPtr hwnd, bool on)
        {
            try
            {
                int s = GetWindowLong(hwnd, GWL_EXSTYLE);
                s = on ? (s | WS_EX_NOACTIVATE) : (s & ~WS_EX_NOACTIVATE);
                SetWindowLong(hwnd, GWL_EXSTYLE, s);
            }
            catch { }
        }

        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("user32.dll")] static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, IntPtr procId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);

        /// <summary>Porta la finestra in primo piano anche se nessun'altra finestra nostra è attiva (Windows lo vieta ai processi in secondo piano).</summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint fgThread = (uint)GetWindowThreadProcessId(fg, IntPtr.Zero).ToInt64();
                uint me = GetCurrentThreadId();
                bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                if (attached) AttachThreadInput(me, fgThread, false);
            }
            catch { }
        }

        // ------------------------------------------------------------------ cursore e schermi
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        /// <summary>Vero se il punto (in pixel dello schermo) sta sopra la finestra indicata.</summary>
        public static bool IsPointOverWindow(POINT pt, IntPtr hwnd)
        {
            try { return GetAncestor(WindowFromPoint(pt), 2) == hwnd; } catch { return false; }
        }

        // ------------------------------------------------------------------ tasto rapido globale
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

        // ------------------------------------------------------------------ identità e priorità
        [DllImport("shell32.dll", PreserveSig = false)]
        static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        /// <summary>Raggruppa le finestre sotto «WinTools» nella barra delle applicazioni.</summary>
        public static void SetAppId(string id) { try { SetCurrentProcessExplicitAppUserModelID(id); } catch { } }

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetThreadPriority(IntPtr thread, int priority);
        const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
        const int THREAD_MODE_BACKGROUND_END = 0x00020000;

        /// <summary>Modalità «in sottofondo» del thread corrente: priorità bassa per processore, disco e memoria (come l'indice di BlackHole).</summary>
        public static bool BeginBackgroundMode() { try { return SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN); } catch { return false; } }
        public static void EndBackgroundMode() { try { SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END); } catch { } }

        // ------------------------------------------------------------------ enumerazione veloce dei file
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_HIDDEN = 0x2, FILE_ATTRIBUTE_SYSTEM = 0x4, FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        const int FindExInfoBasic = 1;
        const int FindExSearchNameMatch = 0;
        const int FIND_FIRST_EX_LARGE_FETCH = 2;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATA lpFindFileData, int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        /// <summary>Apre l'elenco di una cartella con il recupero «a blocchi grandi» (meno chiamate al disco).</summary>
        public static IntPtr FindFirst(string dir, out WIN32_FIND_DATA data)
        {
            string pattern = dir.EndsWith("\\") ? dir + "*" : dir + "\\*";
            // Il prefisso \\?\ evita il limite dei 260 caratteri
            if (!pattern.StartsWith("\\\\")) pattern = "\\\\?\\" + pattern;
            return FindFirstFileExW(pattern, FindExInfoBasic, out data, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
        }

        public static long ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            long v = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
            try { return v <= 0 ? 0 : DateTime.FromFileTimeUtc(v).ToLocalTime().Ticks; } catch { return 0; }
        }

        // ------------------------------------------------------------------ icone della shell
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        const int SIIGBF_RESIZETOFIT = 0x00, SIIGBF_BIGGERSIZEOK = 0x01, SIIGBF_ICONONLY = 0x04;

        /// <summary>
        /// Icona di un file, cartella, collegamento o app (anche «shell:AppsFolder\…») alla dimensione chiesta, in pixel.
        /// null se la shell non la fornisce.
        /// </summary>
        public static BitmapSource GetShellIcon(string path, int size)
        {
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory f);
                if (f == null) return null;
                int hr = f.GetImage(new SIZE(size, size), SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero)
                {
                    hr = f.GetImage(new SIZE(size, size), SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero) return null;
                }
                using (var bmp = System.Drawing.Image.FromHbitmap(hbm))
                {
                    // Image.FromHbitmap perde il canale alfa: si rilegge il bitmap a 32 bit con la trasparenza
                    var src = FromHBitmapWithAlpha(hbm, bmp.Width, bmp.Height);
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
            finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        /// <summary>
        /// I pixel del bitmap, con il canale alfa, già nel verso giusto: GetDIBits con altezza negativa restituisce le
        /// righe dall'alto in basso (leggere i bit grezzi della DIB section, come prima, li dava capovolti).
        /// </summary>
        static BitmapSource FromHBitmapWithAlpha(IntPtr hbm, int w, int h)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                int stride = w * 4;
                var bmi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0 };
                byte[] pixels = new byte[stride * h];
                if (GetDIBits(hdc, hbm, 0, (uint)h, pixels, ref bmi, 0) == h)
                {
                    // Alcune icone a 32 bit hanno l'alfa tutto a zero: allora si considerano opache
                    bool anyAlpha = false;
                    for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] != 0) { anyAlpha = true; break; } }
                    return BitmapSource.Create(w, h, 96, 96, anyAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32, null, pixels, stride);
                }
            }
            catch { }
            finally { if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc); }
            return Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        // ------------------------------------------------------------------ messaggi di Windows
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    }
}
