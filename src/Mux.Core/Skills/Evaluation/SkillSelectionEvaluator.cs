namespace Mux.Core.Skills.Evaluation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Measures how well a set of skills would be chosen for realistic prompts. For each
    /// <see cref="SkillEvalCase"/> it builds the project fixture in a temporary directory, lists the skills the way
    /// the system prompt does (valid, model-invocable, and relevant by <c>appliesTo</c> and <c>requiresTools</c>),
    /// and ranks the listing against the prompt with <see cref="SkillSelectionScorer"/>. Thread-safe for concurrent
    /// <see cref="Evaluate"/> calls; each case uses its own directory.
    /// </summary>
    public sealed class SkillSelectionEvaluator
    {
        #region Private-Members

        private const string CasesResourceName = "Mux.Core.Skills.Evaluation.skill-eval.json";

        private readonly List<Skill> _Skills;
        private readonly Dictionary<string, Skill> _ByName;
        private readonly Func<IReadOnlyList<string>, bool> _ToolsAvailable;
        private readonly AppliesToMatcher _Matcher = new AppliesToMatcher();
        private int _TopN = 3;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillSelectionEvaluator"/> class.
        /// </summary>
        /// <param name="skills">The installed skills. Invalid and non-model-invocable skills are ignored, as the listing ignores them.</param>
        /// <param name="toolsAvailable">Decides whether a skill's <c>requiresTools</c> are installed. Null treats every tool as installed, which keeps results the same on every machine.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skills"/> is null.</exception>
        public SkillSelectionEvaluator(IEnumerable<Skill> skills, Func<IReadOnlyList<string>, bool>? toolsAvailable = null)
        {
            if (skills == null) throw new ArgumentNullException(nameof(skills));

            _Skills = skills.Where(s => s != null && s.IsValid && s.Manifest.ModelInvocable).OrderBy(s => s.Manifest.Name, StringComparer.Ordinal).ToList();
            _ByName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
            foreach (Skill skill in _Skills)
            {
                _ByName[skill.Manifest.Name] = skill;
            }

            _ToolsAvailable = toolsAvailable ?? (_ => true);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The rank an expected skill must reach for a case to pass. Clamped to 1 through 50. Default 3.
        /// </summary>
        public int TopN
        {
            get => _TopN;
            set => _TopN = Math.Clamp(value, 1, 50);
        }

        /// <summary>
        /// The names of the skills under evaluation.
        /// </summary>
        public IReadOnlyList<string> SkillNames
        {
            get => _Skills.Select(s => s.Manifest.Name).ToList();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Loads the built-in evaluation cases shipped with mux.
        /// </summary>
        /// <returns>The cases.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the embedded resource is missing.</exception>
        public static IReadOnlyList<SkillEvalCase> LoadBuiltInCases()
        {
            Assembly assembly = typeof(SkillSelectionEvaluator).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(CasesResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The built-in skill evaluation cases are missing: " + CasesResourceName);
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return ParseCases(reader.ReadToEnd());
                }
            }
        }

        /// <summary>
        /// Parses evaluation cases from a JSON array.
        /// </summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The cases.</returns>
        /// <exception cref="ArgumentException">Thrown when the text is blank.</exception>
        /// <exception cref="JsonException">Thrown when the text is not a JSON array of cases.</exception>
        public static IReadOnlyList<SkillEvalCase> ParseCases(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("The case JSON is empty.", nameof(json));

            List<SkillEvalCase>? cases = JsonSerializer.Deserialize<List<SkillEvalCase>>(json);
            if (cases == null)
            {
                throw new JsonException("The case JSON must be an array.");
            }

            return cases;
        }

        /// <summary>
        /// Evaluates every case.
        /// </summary>
        /// <param name="cases">The cases.</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cases"/> is null.</exception>
        public SkillEvalReport Evaluate(IEnumerable<SkillEvalCase> cases)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));

            SkillEvalReport report = new SkillEvalReport { TopN = _TopN };
            foreach (SkillEvalCase evalCase in cases)
            {
                report.Results.Add(EvaluateCase(evalCase));
            }

            return report;
        }

        /// <summary>
        /// Evaluates every case with a caller-supplied chooser (see <see cref="EvaluateCaseAsync"/>), one case at a time.
        /// </summary>
        /// <param name="cases">The cases.</param>
        /// <param name="chooser">Given a case and its listed skills, returns skill names best first.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cases"/> or <paramref name="chooser"/> is null.</exception>
        public async Task<SkillEvalReport> EvaluateAsync(IEnumerable<SkillEvalCase> cases, Func<SkillEvalCase, IReadOnlyList<Skill>, CancellationToken, Task<List<string>>> chooser, CancellationToken cancellationToken)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));
            if (chooser == null) throw new ArgumentNullException(nameof(chooser));

            SkillEvalReport report = new SkillEvalReport { TopN = _TopN };
            foreach (SkillEvalCase evalCase in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.Results.Add(await EvaluateCaseAsync(evalCase, (IReadOnlyList<Skill> listed, CancellationToken token) => chooser(evalCase, listed, token), cancellationToken).ConfigureAwait(false));
            }

            return report;
        }

        /// <summary>
        /// Evaluates one case.
        /// </summary>
        /// <param name="evalCase">The case.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="evalCase"/> is null.</exception>
        public SkillEvalCaseResult EvaluateCase(SkillEvalCase evalCase)
        {
            return EvaluateCaseAsync(
                evalCase,
                (IReadOnlyList<Skill> listed, CancellationToken token) => Task.FromResult(RankLexically(evalCase.Prompt, listed)),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Evaluates one case with a caller-supplied chooser instead of the lexical ranking: builds the fixture, lists
        /// the skills, asks the chooser for its picks (best first), and scores the case against them. The live
        /// evaluation passes a chooser that asks a model.
        /// </summary>
        /// <param name="evalCase">The case.</param>
        /// <param name="chooser">Given the listed skills, returns skill names best first. A name that is not listed is ignored.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="evalCase"/> or <paramref name="chooser"/> is null.</exception>
        public async Task<SkillEvalCaseResult> EvaluateCaseAsync(SkillEvalCase evalCase, Func<IReadOnlyList<Skill>, CancellationToken, Task<List<string>>> chooser, CancellationToken cancellationToken)
        {
            if (evalCase == null) throw new ArgumentNullException(nameof(evalCase));
            if (chooser == null) throw new ArgumentNullException(nameof(chooser));

            SkillEvalCaseResult result = new SkillEvalCaseResult { Case = evalCase };
            string? error = Check(evalCase);
            if (error != null)
            {
                result.Error = error;
                return result;
            }

            string root = Path.Combine(Path.GetTempPath(), "mux-skilleval-" + Guid.NewGuid().ToString("N"));
            try
            {
                BuildFixture(root, evalCase.Files);
                List<Skill> listed = ListFor(root);
                HashSet<string> listedNames = new HashSet<string>(listed.Select(s => s.Manifest.Name), StringComparer.OrdinalIgnoreCase);
                result.ListedCount = listed.Count;
                result.NotListed = evalCase.Expect.Where(name => !listedNames.Contains(name)).ToList();
                result.WronglyListed = evalCase.Absent.Where(name => listedNames.Contains(name)).ToList();

                List<string> picks = (await chooser(listed, cancellationToken).ConfigureAwait(false) ?? new List<string>())
                    .Where(name => listedNames.Contains(name))
                    .ToList();
                result.Top = picks.Take(Math.Max(_TopN, 5)).ToList();
                for (int i = 0; i < picks.Count; i++)
                {
                    if (evalCase.Expect.Contains(picks[i], StringComparer.OrdinalIgnoreCase))
                    {
                        result.Rank = i + 1;
                        result.Matched = picks[i];
                        break;
                    }
                }

                result.RankPassed = result.Rank >= 1 && result.Rank <= _TopN;
                return result;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Lists the skills the system prompt would show for a project root.
        /// </summary>
        /// <param name="projectRoot">The project root, or null to skip the <c>appliesTo</c> check.</param>
        /// <returns>The listed skills, in name order.</returns>
        public List<Skill> ListFor(string? projectRoot)
        {
            return _Skills.Where(s => SkillCatalogView.IsRelevantUncached(s, projectRoot, _Matcher, _ToolsAvailable)).ToList();
        }

        /// <summary>
        /// Finds pairs of skills whose descriptions are near duplicates, most similar first.
        /// </summary>
        /// <param name="threshold">The minimum similarity to report, from 0 to 1.</param>
        /// <returns>The pairs.</returns>
        public List<SkillCollision> FindCollisions(double threshold)
        {
            List<List<string>> terms = _Skills.Select(s => SkillSelectionScorer.Tokenize(s.Manifest.Description)).ToList();
            Dictionary<string, int> documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (List<string> doc in terms)
            {
                foreach (string term in doc.Distinct(StringComparer.Ordinal))
                {
                    documentFrequency[term] = documentFrequency.TryGetValue(term, out int n) ? n + 1 : 1;
                }
            }

            List<SkillCollision> collisions = new List<SkillCollision>();
            for (int i = 0; i < _Skills.Count; i++)
            {
                for (int j = i + 1; j < _Skills.Count; j++)
                {
                    double similarity = SkillSelectionScorer.Similarity(terms[i], terms[j], documentFrequency, _Skills.Count);
                    if (similarity >= threshold)
                    {
                        collisions.Add(new SkillCollision { First = _Skills[i].Manifest.Name, Second = _Skills[j].Manifest.Name, Similarity = similarity });
                    }
                }
            }

            return collisions.OrderByDescending(c => c.Similarity).ThenBy(c => c.First, StringComparer.Ordinal).ToList();
        }

        #endregion

        #region Private-Methods

        // Skills with a positive BM25 score, best first. A zero score is never a pick, even at the top of a tie.
        private static List<string> RankLexically(string prompt, IReadOnlyList<Skill> listed)
        {
            return SkillSelectionScorer.Rank(prompt, listed.Select(s => new KeyValuePair<string, string>(s.Manifest.Name, s.Manifest.Description)).ToList())
                .Where(r => r.Value > 0)
                .Select(r => r.Key)
                .ToList();
        }

        private string? Check(SkillEvalCase evalCase)
        {
            if (string.IsNullOrWhiteSpace(evalCase.Prompt)) return "the case has no prompt";
            if (evalCase.Expect.Count == 0) return "the case expects no skill";

            foreach (string name in evalCase.Expect.Concat(evalCase.Absent))
            {
                if (!_ByName.ContainsKey(name)) return "unknown skill '" + name + "'";
            }

            foreach (string file in evalCase.Files)
            {
                if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file) || file.Replace('\\', '/').Split('/').Contains(".."))
                {
                    return "fixture path '" + file + "' must be relative and stay inside the project";
                }
            }

            return null;
        }

        private static void BuildFixture(string root, IEnumerable<string> files)
        {
            Directory.CreateDirectory(root);
            foreach (string file in files)
            {
                string relative = file.Replace('\\', '/');
                string path = Path.Combine(root, relative.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                if (relative.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                string? parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(path, string.Empty);
            }
        }

        #endregion
    }
}
