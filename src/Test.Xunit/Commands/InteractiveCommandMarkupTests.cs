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
        [InlineData("[cyan]/help[/], [cyan]/?[/]")]
        [InlineData("[cyan]/endpoint[/], [cyan]/endpoint list|ls[/], [cyan]/model[/], [cyan]/model list|ls[/]")]
        [InlineData("[cyan]/endpoint show[/] [dim]<name>[/], [cyan]/model show[/] [dim]<name>[/]")]
        [InlineData("[cyan]/endpoint remove|delete|rm[/] [dim]<name>[/], [cyan]/model remove|delete|rm[/] [dim]<name>[/]")]
        [InlineData("[cyan]/search[/], [cyan]/search list|ls[/]")]
        [InlineData("[cyan]/search add[/] [dim][[name]][/]")]
        [InlineData("[cyan]/search remove|delete|rm[/] [dim]<name>[/]")]
        [InlineData("[cyan]/mcp list|ls[/]")]
        [InlineData("[cyan]/mcp add[/]")]
        [InlineData("[cyan]/mcp remove|delete|rm[/] [dim]<name>[/]")]
        [InlineData("[yellow]  \u2514 Allow?[/] [[[green]Y[/]/[red]n[/]/[blue]always[/]]] ")]
        [InlineData("[yellow]  \u251C Approval required: write_file: sample [[Y/n/always]]?[/]")]
        public void InteractiveHelpMarkup_IsValid(string markup)
        {
            Exception? exception = Record.Exception(() => _ = new Markup(markup));
            Assert.Null(exception);
        }
    }
}
