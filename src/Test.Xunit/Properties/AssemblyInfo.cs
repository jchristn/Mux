// MUX Touchstone suites mutate process-wide state (environment variables such as MUX_CONFIG_DIR and
// the redirected Console output streams used by in-process CLI invocations). They must therefore run
// sequentially; disable xUnit's default cross-collection parallelization for this assembly.
[assembly: global::Xunit.CollectionBehavior(DisableTestParallelization = true)]
