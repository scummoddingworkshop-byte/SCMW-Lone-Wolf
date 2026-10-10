using Xunit;

// Avalonia UI tests must not run in parallel with the rest of the suite: the headless
// platform and XAML loading are not safe to drive from several threads at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
