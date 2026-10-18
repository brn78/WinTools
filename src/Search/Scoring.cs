using System;
using System.Collections.Generic;

namespace WinTools.Search
{
    /// <summary>
    /// Pertinenza di un risultato (le regole di BlackHole, adattate ai file): la richiesta intera come nome (+120) o al
    /// suo inizio (+70); per parola, inizio di parola nel nome (+24), nel nome (+12), nel percorso (+8/+4); tutte nel nome
    /// +20; fino a +6 per ciò che è cambiato di recente; un bonus per ciò che si apre spesso; un piccolo peso per tipo.
    /// </summary>
    public static class Scoring
    {
        public static double Score(string foldedName, string foldedStem, string foldedPath, List<string> words, DateTime modified, int usage, double weight)
        {
            double s = weight;
            string phrase = string.Join(" ", words);
            if (foldedStem == phrase || foldedName == phrase) s += 120;
            else if (foldedName.StartsWith(phrase, StringComparison.Ordinal)) s += 70;
            else if (words.Count > 1 && foldedName.IndexOf(phrase, StringComparison.Ordinal) >= 0) s += 40;

            int inName = 0;
            foreach (var w in words)
            {
                if (SearchText.HasWordStartingWith(foldedName, w)) { s += 24; inName++; }
                else if (foldedName.IndexOf(w, StringComparison.Ordinal) >= 0) { s += 12; inName++; }
                else if (foldedPath != null && SearchText.HasWordStartingWith(foldedPath, w)) s += 8;
                else s += 4;
            }
            if (inName == words.Count) s += 20;
            if (modified.Year > 1980) s += Math.Max(0, 6 - (DateTime.Now - modified).TotalDays / 60);
            if (usage > 0) s += Math.Min(30, 8 * usage);
            // Nomi corti vincono su nomi lunghi che contengono le stesse parole
            s -= Math.Min(10, foldedName.Length / 12.0);
            return s;
        }
    }
}
