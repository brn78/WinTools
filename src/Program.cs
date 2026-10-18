using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;

namespace WinTools
{
    /// <summary>
    /// Punto d'ingresso: una sola istanza (la seconda chiede alla prima di mostrare la barra), errori fatali nel log
    /// e in un messaggio visibile, pulizia dell'exe precedente dopo un aggiornamento.
    /// </summary>
    public static class Program
    {
        public const string Title = "WinTools";
        public const string RepoUrl = "https://github.com/brn78/WinTools";
        public const string UpdateRepo = "brn78/WinTools";
        const string MutexName = "Local\\WinTools.Instance";
        const string ShowEventName = "Local\\WinTools.Show";

        static Mutex _instance;   // tenuto vivo per tutta la durata del processo

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Revision == 0 && v.Build == 0 ? $"{v.Major}.{v.Minor}" : v.Build >= 0 && v.Revision == 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
            }
        }

        public static string ExePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch { return Assembly.GetExecutingAssembly().Location; }
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Fatal(e.ExceptionObject as Exception);
            try
            {
                bool createdNew;
                _instance = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    // Un'altra istanza c'è già: le si chiede di mostrare la barra
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                    }
                    catch { }
                    return 0;
                }

                CleanOldExe();
                Native.SetAppId("brn78.WinTools");

                var app = new App();
                app.InitializeComponent();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    LogError("Interfaccia", e.Exception);
                    e.Handled = true;   // un errore in un gestore non deve chiudere la barra
                };

                // La seconda istanza (o un collegamento) segnala qui: la barra compare
                var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        show.WaitOne();
                        try { app.Dispatcher.BeginInvoke(new Action(() => app.ShowBar(true))); } catch { break; }
                    }
                }) { IsBackground = true, Name = "WinTools.ShowSignal" };
                waiter.Start();

                return app.Run();
            }
            catch (Exception ex)
            {
                Fatal(ex);
                return 1;
            }
        }

        /// <summary>Dopo un aggiornamento la versione precedente resta come «WinTools.old.exe»: si elimina in background.</summary>
        static void CleanOldExe()
        {
            try
            {
                string exe = ExePath;
                string old = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + ".old.exe");
                if (!File.Exists(old)) return;
                var t = new Thread(() =>
                {
                    for (int i = 0; i < 30 && File.Exists(old); i++)
                    {
                        try { File.Delete(old); } catch { Thread.Sleep(1000); }
                    }
                }) { IsBackground = true };
                t.Start();
            }
            catch { }
        }

        static readonly object LogGate = new object();

        public static void LogError(string context, Exception ex)
        {
            try
            {
                lock (LogGate)
                {
                    string line = $"[{DateTime.Now:s}] {context}: {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}{ex?.StackTrace}{Environment.NewLine}";
                    File.AppendAllText(AppSettings.ErrorLog, line, Encoding.UTF8);
                    // Il log non cresce all'infinito
                    var fi = new FileInfo(AppSettings.ErrorLog);
                    if (fi.Length > 512 * 1024) File.WriteAllText(AppSettings.ErrorLog, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        static void Fatal(Exception ex)
        {
            if (ex == null) return;
            LogError("Errore fatale", ex);
            try
            {
                MessageBox.Show($"{Title} si è chiuso per un errore inatteso:\n\n{ex.Message}\n\nDettagli nel file:\n{AppSettings.ErrorLog}",
                    Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}
