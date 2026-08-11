namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for Phase 4 headless features: <c>--output-schema</c> structural validation, the
    /// new print flags (<c>--output-schema</c>, <c>--mcp-config</c>, <c>--strict-mcp-config</c>), and MCP
    /// config parsing used by headless MCP. Both the conforming and violating directions are covered.
    /// Live MCP round-trips are exercised by the interactive path and are not repeated here (they require a
    /// running MCP server, which the environment does not provide).
    /// </summary>
    public static class Phase4FeaturesSuite
    {
        /// <summary>
        /// Builds the Phase 4 feature suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all Phase 4 cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "Phase4Features",
                "Output schema validation, print flags, and MCP config parsing",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Phase4Features", "SchemaDocumentValidity", "schema documents are checked for valid JSON", (CancellationToken ct) =>
                    {
                        MuxAssert.IsTrue(OutputSchemaValidator.IsValidSchemaDocument("{\"type\":\"object\"}", out string _), "valid schema accepted");
                        MuxAssert.IsFalse(OutputSchemaValidator.IsValidSchemaDocument("{not json", out string err), "invalid schema rejected");
                        MuxAssert.IsTrue(err.Length > 0, "an error message is provided for an invalid schema");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaAcceptsConformingObject", "a conforming object response passes validation", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"required\":[\"name\",\"age\"],\"properties\":{\"name\":{\"type\":\"string\"},\"age\":{\"type\":\"number\"}}}";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "{\"name\":\"Ada\",\"age\":36}"), "conforming object accepted");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaStripsCodeFences", "a fenced JSON response is unwrapped and validated", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"required\":[\"ok\"]}";
                        string fenced = "```json\n{\"ok\":true}\n```";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, fenced), "fenced JSON accepted after unwrapping");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaRejectsMissingRequired", "a response missing a required property fails", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"required\":[\"name\",\"age\"]}";
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "{\"name\":\"Ada\"}"), "missing required property rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaRejectsTypeMismatch", "a response of the wrong top-level type fails", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\"}";
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "[1,2,3]"), "array where object required rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaRejectsNonJson", "a non-JSON response fails", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate("{\"type\":\"object\"}", "here is your answer"), "prose response rejected");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate("{\"type\":\"object\"}", string.Empty), "empty response rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaAcceptsArray", "an array schema accepts an array response", (CancellationToken ct) =>
                    {
                        MuxAssert.IsNull(OutputSchemaValidator.Validate("{\"type\":\"array\"}", "[1,2,3]"), "array response accepted for array schema");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaValidatesNestedProperties", "nested object properties are validated recursively", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"required\":[\"user\"],\"properties\":{\"user\":{\"type\":\"object\",\"required\":[\"id\",\"name\"],\"properties\":{\"id\":{\"type\":\"number\"},\"name\":{\"type\":\"string\"}}}}}";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "{\"user\":{\"id\":1,\"name\":\"Ada\"}}"), "conforming nested object accepted");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "{\"user\":{\"id\":1}}"), "missing nested required property rejected");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "{\"user\":{\"id\":\"x\",\"name\":\"Ada\"}}"), "wrong nested property type rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaValidatesArrayItems", "array item schemas are validated recursively", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"sku\"],\"properties\":{\"sku\":{\"type\":\"string\"}}}}";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "[{\"sku\":\"a\"},{\"sku\":\"b\"}]"), "conforming array items accepted");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "[{\"sku\":\"a\"},{\"nope\":true}]"), "non-conforming array item rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaEnforcesEnum", "enum values are enforced", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"properties\":{\"status\":{\"enum\":[\"open\",\"closed\"]}}}";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "{\"status\":\"open\"}"), "allowed enum value accepted");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "{\"status\":\"pending\"}"), "disallowed enum value rejected");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "SchemaSupportsUnionAndNullableTypes", "union type arrays are accepted", (CancellationToken ct) =>
                    {
                        string schema = "{\"type\":\"object\",\"properties\":{\"note\":{\"type\":[\"string\",\"null\"]}}}";
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "{\"note\":\"hi\"}"), "string satisfies union type");
                        MuxAssert.IsNull(OutputSchemaValidator.Validate(schema, "{\"note\":null}"), "null satisfies union type");
                        MuxAssert.IsNotNull(OutputSchemaValidator.Validate(schema, "{\"note\":42}"), "number rejected by string|null union");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "ParsePrintReadsPhase4Flags", "print parser reads the Phase 4 flags", (CancellationToken ct) =>
                    {
                        PrintSettings settings = CliArgumentParser.ParsePrint(new[]
                        {
                            "--output-schema", "schema.json",
                            "--mcp-config", "servers.json",
                            "--strict-mcp-config",
                            "extract the data"
                        });

                        MuxAssert.AreEqual("schema.json", settings.OutputSchema, "OutputSchema parsed");
                        MuxAssert.AreEqual("servers.json", settings.McpConfig, "McpConfig parsed");
                        MuxAssert.IsTrue(settings.StrictMcpConfig, "StrictMcpConfig parsed");
                        MuxAssert.AreEqual("extract the data", settings.Prompt, "prompt preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "McpConfigInlineJsonParses", "inline MCP config JSON parses to servers", (CancellationToken ct) =>
                    {
                        string json = "{\"servers\":[{\"name\":\"ctx\",\"transport\":\"stdio\",\"command\":\"echo\"}]}";
                        List<McpServerConfig> servers = SettingsLoader.ParseMcpServers(json);
                        MuxAssert.AreEqual(1, servers.Count, "one server parsed");
                        MuxAssert.AreEqual("ctx", servers[0].Name, "server name parsed");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("Phase4Features", "McpConfigEmptyAndInvalid", "empty config yields none and invalid JSON throws", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(0, SettingsLoader.ParseMcpServers("{}").Count, "no servers when none declared");
                        MuxAssert.Throws<JsonException>(() => SettingsLoader.ParseMcpServers("{not json"), "invalid MCP config JSON throws");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
