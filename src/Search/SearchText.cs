using System;
using System.Collections.Generic;
using System.Text;

namespace WinTools.Search
{
    /// <summary>
    /// Testo per la ricerca (come SearchText di BlackHole Workspaces): normalizzazione in minuscolo e senza accenti,
    /// carattere per carattere (le posizioni restano quelle del testo), parole della richiesta, inizio di parola,
    /// distanza tra parole per il «forse cercavi».
    /// </summary>
    public static class SearchText
    {
        public const int MaxWords = 8;
        public const int MaxQueryLength = 200;

        /// <summary>Lettere latine (fino a U+024F) senza accento e in minuscolo; gli altri caratteri solo in minuscolo.</summary>
        static readonly char[] Table = BuildTable();

        static char[] BuildTable()
        {
            var t = new char[0x250];
            for (int i = 0; i < t.Length; i++)
            {
                string d = ((char)i).ToString().Normalize(NormalizationForm.FormD);
                t[i] = char.ToLowerInvariant(d[0]);
            }
            // Lettere che la scomposizione Unicode non tocca
            t['ß'] = 's'; t['æ'] = 'a'; t['Æ'] = 'a'; t['ø'] = 'o'; t['Ø'] = 'o'; t['đ'] = 'd'; t['Đ'] = 'd'; t['ł'] = 'l'; t['Ł'] = 'l'; t['œ'] = 'o'; t['Œ'] = 'o'; t['þ'] = 't'; t['Þ'] = 't'; t['ð'] = 'd'; t['Ð'] = 'd';
            return t;
        }

        public static char FoldChar(char c)
        {
            return c < 0x250 ? Table[c] : char.ToLowerInvariant(c);
        }

        /// <summary>Un byte per carattere, per il buffer di scansione: ASCII com'è, il resto ripiegato su 128-255.</summary>
        public static byte FoldByte(char c)
        {
            char f = FoldChar(c);
            return f < 128 ? (byte)f : (byte)(128 + (f % 128));
        }

        /// <summary>Testo normalizzato della stessa lunghezza: «Città Àrea» → «citta area».</summary>
        public static string Fold(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++) chars[i] = FoldChar(chars[i]);
            return new string(chars);
        }

        public static byte[] FoldBytes(string value)
        {
            var b = new byte[value.Length];
            for (int i = 0; i < b.Length; i++) b[i] = FoldByte(value[i]);
            return b;
        }

        static readonly char[] Separators = { ' ', '\t', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', '«', '»', '“', '”', '’', '*' };

        /// <summary>
        /// Parole della richiesta, normalizzate e senza doppioni (al massimo 8): si separano su spazi e punteggiatura
        /// esterna, mentre punti, trattini, barre e chiocciola restano dentro la parola (estensioni, codici, email).
        /// </summary>
        public static List<string> Words(string query)
        {
            string text = Fold((query ?? "").Trim());
            if (text.Length > MaxQueryLength) text = text.Substring(0, MaxQueryLength);
            var result = new List<string>();
            foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw.Trim('.', '-', '/', '+', '_', '\\');
                if (w == "") continue;
                if (!result.Contains(w)) result.Add(w);
                if (result.Count == MaxWords) break;
            }
            return result;
        }

        /// <summary>Vero se una parola del testo normalizzato inizia con w (all'inizio o dopo un carattere che non è lettera o cifra).</summary>
        public static bool HasWordStartingWith(string folded, string w)
        {
            int at = folded.IndexOf(w, StringComparison.Ordinal);
            while (at >= 0)
            {
                if (at == 0 || !char.IsLetterOrDigit(folded[at - 1])) return true;
                at = folded.IndexOf(w, at + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>Distanza di Damerau-Levenshtein (scambio di due lettere vicine compreso), fermandosi appena supera max.</summary>
        public static int Distance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;
            var prev2 = new int[b.Length + 1];
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;
                var t = prev2; prev2 = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
