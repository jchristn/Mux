namespace Mux.Cli.App
{
    /// <summary>
    /// The kinds of rows shown in the Endpoints / models list, kept in a list parallel to the option
    /// strings so a blank separator or a trailing action never has to be inferred from index math.
    /// </summary>
    internal enum EndpointMenuAction
    {
        Switch,
        None,
        Add,
        Edit,
        Remove,
        Import
    }
}
