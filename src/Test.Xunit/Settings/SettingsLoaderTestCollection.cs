namespace Test.Xunit.Settings
{
    using global::Xunit;

    /// <summary>
    /// Serializes SettingsLoader tests because they mutate process-wide environment variables.
    /// </summary>
    [CollectionDefinition("SettingsLoader", DisableParallelization = true)]
    public class SettingsLoaderTestCollection
    {
    }
}
