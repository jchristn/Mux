namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="EndpointCommandParser"/> argument parsing. Ported from the
    /// <c>EndpointCommandParserTests</c> xUnit suite; the original theory rows are expanded into one
    /// case per alias.
    /// </summary>
    public static class EndpointCommandParserSuite
    {
        /// <summary>
        /// Builds the endpoint-command-parser suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the endpoint-command-parser cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            string[] listAliases = new[] { "", "list", "ls" };
            foreach (string alias in listAliases)
            {
                string argument = alias;
                string label = argument.Length == 0 ? "empty" : argument;
                cases.Add(new TestCaseDescriptor("EndpointCommandParser", "ListAlias_" + label, "List alias '" + label + "' returns the List action", (CancellationToken ct) =>
                {
                    EndpointCommandParseResult result = EndpointCommandParser.Parse(argument);
                    MuxAssert.IsTrue(result.Success, "success");
                    MuxAssert.IsNotNull(result.Request, "request");
                    MuxAssert.AreEqual(EndpointCommandAction.List, result.Request!.Action, "action");
                    return Task.CompletedTask;
                }));
            }

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "BareNameReturnsSwitchAction", "A bare endpoint name is a switch request", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("openai-prod");
                MuxAssert.IsTrue(result.Success, "success");
                MuxAssert.IsNotNull(result.Request, "request");
                MuxAssert.AreEqual(EndpointCommandAction.Switch, result.Request!.Action, "action");
                MuxAssert.AreEqual("openai-prod", result.Request.Name, "name");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "ShowCommandReturnsShowAction", "The show subcommand targets the requested endpoint", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("show local-dev");
                MuxAssert.IsTrue(result.Success, "success");
                MuxAssert.IsNotNull(result.Request, "request");
                MuxAssert.AreEqual(EndpointCommandAction.Show, result.Request!.Action, "action");
                MuxAssert.AreEqual("local-dev", result.Request.Name, "name");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "AddCommandReturnsAddAction", "add starts the guided creation workflow", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("add");
                MuxAssert.IsTrue(result.Success, "success");
                MuxAssert.IsNotNull(result.Request, "request");
                MuxAssert.AreEqual(EndpointCommandAction.Add, result.Request!.Action, "action");
                MuxAssert.IsNull(result.Request.Name, "name null");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "AddCommandWithSeedName", "add may optionally seed the endpoint name", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("add openai-prod");
                MuxAssert.IsTrue(result.Success, "success");
                MuxAssert.IsNotNull(result.Request, "request");
                MuxAssert.AreEqual(EndpointCommandAction.Add, result.Request!.Action, "action");
                MuxAssert.AreEqual("openai-prod", result.Request.Name, "name");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "EditCommandReturnsEditAction", "edit targets a configured endpoint name", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("edit openai-prod");
                MuxAssert.IsTrue(result.Success, "success");
                MuxAssert.IsNotNull(result.Request, "request");
                MuxAssert.AreEqual(EndpointCommandAction.Edit, result.Request!.Action, "action");
                MuxAssert.AreEqual("openai-prod", result.Request.Name, "name");
                return Task.CompletedTask;
            }));

            string[] removeAliases = new[] { "remove", "delete", "rm" };
            foreach (string alias in removeAliases)
            {
                string action = alias;
                cases.Add(new TestCaseDescriptor("EndpointCommandParser", "RemoveAlias_" + action, "Remove alias '" + action + "' targets a configured endpoint name", (CancellationToken ct) =>
                {
                    EndpointCommandParseResult result = EndpointCommandParser.Parse(action + " stale-endpoint");
                    MuxAssert.IsTrue(result.Success, "success");
                    MuxAssert.IsNotNull(result.Request, "request");
                    MuxAssert.AreEqual(EndpointCommandAction.Remove, result.Request!.Action, "action");
                    MuxAssert.AreEqual("stale-endpoint", result.Request.Name, "name");
                    return Task.CompletedTask;
                }));
            }

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "RemoveAliasMissingNameModelAlias", "Remove alias errors use the invoked command name", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("delete", "/model");
                MuxAssert.IsFalse(result.Success, "failure");
                MuxAssert.Contains("Usage: /model remove|delete|rm <name>", result.ErrorMessage, "error message");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "AddCommandUnexpectedArguments", "add rejects unsupported trailing arguments", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("add one two");
                MuxAssert.IsFalse(result.Success, "failure");
                MuxAssert.Contains("Usage: /endpoint add", result.ErrorMessage, "error message");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor("EndpointCommandParser", "AddCommandUnexpectedArgumentsModelAlias", "Alias-specific parse errors use the invoked command name", (CancellationToken ct) =>
            {
                EndpointCommandParseResult result = EndpointCommandParser.Parse("add one two", "/model");
                MuxAssert.IsFalse(result.Success, "failure");
                MuxAssert.Contains("Usage: /model add", result.ErrorMessage, "error message");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor("EndpointCommandParser", "Endpoint command argument parsing", cases);
        }
    }
}
