namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SessionMetadataNormalizer"/> — the single owner of the label/tag
    /// validation and canonicalization rules every surface shares. Covers the positive (normalization)
    /// and negative (rejection) directions.
    /// </summary>
    public static class SessionMetadataNormalizerSuite
    {
        /// <summary>Builds the normalizer suite descriptor.</summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SessionMetadataNormalizer",
                "Label/tag normalization and validation rules",
                new List<TestCaseDescriptor>
                {
                    Case("LabelCollapsesWhitespace", "Label trims and collapses internal whitespace, preserving case", () =>
                    {
                        bool ok = SessionMetadataNormalizer.TryNormalizeLabel("  Customer   Acme  ", out string label, out _);
                        MuxAssert.IsTrue(ok, "valid");
                        MuxAssert.AreEqual("Customer Acme", label, "collapsed, case preserved");
                    }),

                    Case("LabelRejectsEmpty", "Empty/whitespace labels are rejected", () =>
                    {
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryNormalizeLabel("   ", out _, out string? err1), "whitespace rejected");
                        MuxAssert.IsNotNull(err1, "error message present");
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryNormalizeLabel(null, out _, out _), "null rejected");
                    }),

                    Case("LabelRejectsOverCap", "A label longer than the cap is rejected", () =>
                    {
                        string tooLong = new string('a', SessionMetadataNormalizer.MaxLabelLength + 1);
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryNormalizeLabel(tooLong, out _, out _), "over-cap rejected");
                        string atCap = new string('a', SessionMetadataNormalizer.MaxLabelLength);
                        MuxAssert.IsTrue(SessionMetadataNormalizer.TryNormalizeLabel(atCap, out _, out _), "at cap accepted");
                    }),

                    Case("TagKeySlugified", "Tag key is lowercased and slugified with whitespace to hyphens", () =>
                    {
                        bool ok = SessionMetadataNormalizer.TryNormalizeTagKey("  Sprint Number ", out string key, out _);
                        MuxAssert.IsTrue(ok, "valid");
                        MuxAssert.AreEqual("sprint-number", key, "slugified");
                    }),

                    Case("TagKeyDropsPunctuation", "Tag key drops characters outside [a-z0-9._-]", () =>
                    {
                        bool ok = SessionMetadataNormalizer.TryNormalizeTagKey("Env!!@#", out string key, out _);
                        MuxAssert.IsTrue(ok, "valid");
                        MuxAssert.AreEqual("env", key, "punctuation dropped");
                    }),

                    Case("TagKeyRejectsPunctuationOnly", "A key with no letters or digits is rejected", () =>
                    {
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryNormalizeTagKey("!!!", out _, out string? err), "rejected");
                        MuxAssert.IsNotNull(err, "error present");
                    }),

                    Case("TagValueFreeform", "Tag value keeps UTF-8, trims, collapses whitespace", () =>
                    {
                        bool ok = SessionMetadataNormalizer.TryNormalizeTagValue("  prod  环境  ", out string value, out _);
                        MuxAssert.IsTrue(ok, "valid");
                        MuxAssert.AreEqual("prod 环境", value, "collapsed, utf-8 preserved");
                    }),

                    Case("TagValueRejectsEmpty", "Empty tag value is rejected", () =>
                    {
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryNormalizeTagValue("  ", out _, out _), "rejected");
                    }),

                    Case("ParseTagSplitsOnFirstColon", "TryParseTag splits on the first colon and normalizes both sides", () =>
                    {
                        bool ok = SessionMetadataNormalizer.TryParseTag("Env: us-east:1", out SessionTag? tag, out _);
                        MuxAssert.IsTrue(ok, "valid");
                        MuxAssert.IsNotNull(tag, "tag");
                        MuxAssert.AreEqual("env", tag!.Key, "key normalized");
                        MuxAssert.AreEqual("us-east:1", tag.Value, "value keeps later colons");
                    }),

                    Case("ParseTagRejectsMissingColon", "A tag without a colon is rejected", () =>
                    {
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryParseTag("envprod", out SessionTag? tag, out string? err), "rejected");
                        MuxAssert.IsNull(tag, "no tag");
                        MuxAssert.IsNotNull(err, "error present");
                    }),

                    Case("ParseTagRejectsEmptyValue", "A tag with an empty value is rejected", () =>
                    {
                        MuxAssert.IsFalse(SessionMetadataNormalizer.TryParseTag("env:   ", out _, out _), "rejected");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, System.Action body)
        {
            return new TestCaseDescriptor("SessionMetadataNormalizer", caseId, displayName, _ =>
            {
                body();
                return Task.CompletedTask;
            });
        }
    }
}
