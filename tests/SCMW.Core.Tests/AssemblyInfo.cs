// Log is a process-wide static whose target directory is re-pointed via Log.SetLogsDir.
// Tests that touch it must not run concurrently: a parallel class re-pointing the directory
// can redirect another test's writes (making the AesKey read or the concurrent-write count
// observe the wrong directory). Serialize the whole Core test assembly for determinism.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
