namespace Mux.Core.Skills.Evaluation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Ranks skill listing lines against a prompt with BM25, using only what the model sees before it chooses: the
    /// skill name (split on dashes, counted twice because names are short and deliberate) and the description.
    /// A lexical proxy for model choice, deterministic so it can run in the test suites. Stateless and thread-safe.
    /// </summary>
    public static class SkillSelectionScorer
    {
        #region Private-Members

        private const double K1 = 1.2;
        private const double B = 0.75;

        private static readonly HashSet<string> _StopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "and", "any", "are", "as", "at", "be", "by", "can", "do", "does", "for", "from", "has", "have",
            "how", "i", "if", "in", "into", "is", "it", "its", "me", "my", "of", "on", "one", "or", "our", "please",
            "so", "that", "the", "their", "them", "then", "this", "to", "up", "us", "we", "what", "when", "which",
            "with", "you", "your", "all", "every", "only", "out", "there", "here", "some", "would", "could", "should",
            "will", "just", "about", "get", "make", "want", "need", "let", "s", "not", "no"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Splits text into normalized terms: lowercase alphanumeric runs, stop words removed, and a light suffix
        /// strip so "tests", "testing", and "tested" meet "test". Terms containing digits are kept whole.
        /// </summary>
        /// <param name="text">The text. Null yields no terms.</param>
        /// <returns>The terms, in order, with repeats.</returns>
        public static List<string> Tokenize(string? text)
        {
            List<string> terms = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return terms;
            }

            StringBuilder current = new StringBuilder();
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    current.Append(c);
                }
                else
                {
                    Flush(current, terms);
                }
            }

            Flush(current, terms);
            return terms;
        }

        /// <summary>
        /// Reduces a lowercase word to a crude stem. Exposed so tests can pin the behavior.
        /// </summary>
        /// <param name="word">The word.</param>
        /// <returns>The stem.</returns>
        public static string Stem(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Any(char.IsDigit))
            {
                return word ?? string.Empty;
            }

            if (word.Length > 5 && word.EndsWith("ies", StringComparison.Ordinal)) return word.Substring(0, word.Length - 3) + "y";
            if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal)) return TrimDouble(word.Substring(0, word.Length - 3));
            if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal)) return TrimDouble(word.Substring(0, word.Length - 2));
            if (word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal) && (word.EndsWith("sses", StringComparison.Ordinal) || word.EndsWith("xes", StringComparison.Ordinal) || word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal))) return word.Substring(0, word.Length - 2);
            if (word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal)) return word.Substring(0, word.Length - 1);
            return word;
        }

        /// <summary>
        /// The terms a skill's listing line contributes: its name parts twice, then its description.
        /// </summary>
        /// <param name="name">The skill name.</param>
        /// <param name="description">The skill description.</param>
        /// <returns>The terms.</returns>
        public static List<string> DocumentTerms(string name, string description)
        {
            List<string> nameTerms = Tokenize((name ?? string.Empty).Replace('-', ' '));
            List<string> terms = new List<string>(nameTerms);
            terms.AddRange(nameTerms);
            terms.AddRange(Tokenize(description));
            return terms;
        }

        /// <summary>
        /// Ranks listing entries against a prompt, best first. Ties keep the name order so results are stable.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="entries">(name, description) pairs: the listing.</param>
        /// <returns>(name, score) pairs, best first.</returns>
        public static List<KeyValuePair<string, double>> Rank(string prompt, IReadOnlyList<KeyValuePair<string, string>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            List<string> query = Tokenize(prompt).Distinct(StringComparer.Ordinal).ToList();
            List<List<string>> docs = entries.Select(e => DocumentTerms(e.Key, e.Value)).ToList();
            double averageLength = docs.Count == 0 ? 1 : Math.Max(1, docs.Average(d => d.Count));

            Dictionary<string, int> documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (List<string> doc in docs)
            {
                foreach (string term in doc.Distinct(StringComparer.Ordinal))
                {
                    documentFrequency[term] = documentFrequency.TryGetValue(term, out int n) ? n + 1 : 1;
                }
            }

            List<KeyValuePair<string, double>> scored = new List<KeyValuePair<string, double>>();
            for (int i = 0; i < entries.Count; i++)
            {
                List<string> doc = docs[i];
                Dictionary<string, int> frequency = doc.GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                double score = 0;
                foreach (string term in query)
                {
                    if (!frequency.TryGetValue(term, out int tf)) continue;
                    int df = documentFrequency[term];
                    double idf = Math.Log(1 + ((docs.Count - df + 0.5) / (df + 0.5)));
                    score += idf * (tf * (K1 + 1)) / (tf + (K1 * (1 - B + (B * doc.Count / averageLength))));
                }

                scored.Add(new KeyValuePair<string, double>(entries[i].Key, score));
            }

            return scored
                .OrderByDescending(s => s.Value)
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Cosine similarity of two descriptions' TF-IDF vectors, with document frequencies taken from a corpus.
        /// </summary>
        /// <param name="first">The first description's terms.</param>
        /// <param name="second">The second description's terms.</param>
        /// <param name="documentFrequency">Term to the number of corpus documents containing it.</param>
        /// <param name="corpusSize">The number of corpus documents.</param>
        /// <returns>The similarity, from 0 to 1.</returns>
        public static double Similarity(IReadOnlyList<string> first, IReadOnlyList<string> second, IReadOnlyDictionary<string, int> documentFrequency, int corpusSize)
        {
            Dictionary<string, double> a = Weigh(first, documentFrequency, corpusSize);
            Dictionary<string, double> b = Weigh(second, documentFrequency, corpusSize);
            double dot = 0;
            foreach (KeyValuePair<string, double> pair in a)
            {
                if (b.TryGetValue(pair.Key, out double other)) dot += pair.Value * other;
            }

            double norm = Math.Sqrt(a.Values.Sum(v => v * v)) * Math.Sqrt(b.Values.Sum(v => v * v));
            return norm == 0 ? 0 : dot / norm;
        }

        #endregion

        #region Private-Methods

        private static void Flush(StringBuilder current, List<string> terms)
        {
            if (current.Length == 0)
            {
                return;
            }

            string word = current.ToString();
            current.Clear();
            if (_StopWords.Contains(word))
            {
                return;
            }

            terms.Add(Stem(word));
        }

        private static string TrimDouble(string stem)
        {
            if (stem.Length > 2 && stem[stem.Length - 1] == stem[stem.Length - 2] && "bdgmnprt".IndexOf(stem[stem.Length - 1]) >= 0)
            {
                return stem.Substring(0, stem.Length - 1);
            }

            return stem;
        }

        private static Dictionary<string, double> Weigh(IReadOnlyList<string> terms, IReadOnlyDictionary<string, int> documentFrequency, int corpusSize)
        {
            Dictionary<string, double> weights = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (IGrouping<string, string> group in terms.GroupBy(t => t, StringComparer.Ordinal))
            {
                int df = documentFrequency.TryGetValue(group.Key, out int n) ? n : 1;
                weights[group.Key] = group.Count() * Math.Log((1.0 + corpusSize) / (1.0 + df));
            }

            return weights;
        }

        #endregion
    }
}
