using System;

namespace WinTools.Search
{
    public enum HitKind { App, Folder, File, Command }

    /// <summary>Un risultato della ricerca: app del menu Start, cartella o file dell'indice, o un comando da eseguire.</summary>
    public sealed class SearchHit
    {
        public HitKind Kind;
        public string Title;
        /// <summary>Percorso completo (file e cartelle), «shell:AppsFolder\…» per le app, il comando per i comandi.</summary>
        public string Path;
        /// <summary>Riga sotto il titolo: la cartella, oppure la descrizione.</summary>
        public string Subtitle;
        /// <summary>Terza informazione a destra: tipo e dimensione, data.</summary>
        public string Meta;
        public long Size;
        public DateTime Modified;
        public double Score;
        /// <summary>Che cosa chiedere alla shell per l'icona (percorso o parsing name); «glyph:E8B7» per un glifo.</summary>
        public string IconKey;

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case HitKind.App: return "App";
                    case HitKind.Folder: return "Cartella";
                    case HitKind.Command: return "Comando";
                    default:
                        string ext = System.IO.Path.GetExtension(Title ?? "").TrimStart('.');
                        return ext == "" ? "File" : ext.ToUpperInvariant();
                }
            }
        }
    }
}
