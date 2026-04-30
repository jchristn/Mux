namespace Test.Xunit.Commands
{
    using System;
    using global::Xunit;
    using Spectre.Console;

    /// <summary>
    /// Regression tests for interactive command help markup.
    /// </summary>
    public class InteractiveCommandMarkupTests
    {
        /// <summary>
        /// Verifies that the interactive help strings use valid Spectre markup syntax.
        /// </summary>
        /// <param name="markup">The help markup string to validate.</param>
        [Theory]
        [InlineData("[yellow]Usage: /compact, /compact [[summary|trim]], or /compact strategy [[summary|trim]][/]")]
        [InlineData("[yellow]Usage: /compact strategy [[summary|trim]][/]")]
        [InlineData("[cyan]/compact strategy[/] [dim][[summary|trim]][/]")]
        [InlineData("[yellow]Usage: /mcp add <name> <command> [[args...]][/]")]
        [InlineData("  Allow? [[[green]Y[/]/[red]n[/]/[blue]always[/]]] ")]
        [InlineData("[yellow]Approval required:[/] write_file: sample [dim][[Y/n/always]]?[/]")]
        public void InteractiveHelpMarkup_IsValid(string markup)
        {
            Exception? exception = Record.Exception(() => _ = new Markup(markup));
            Assert.Null(exception);
        }
    }
}
