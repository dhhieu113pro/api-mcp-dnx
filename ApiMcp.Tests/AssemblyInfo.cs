using Xunit;

// Most tests mutate process-global state (HeaderStore mappings,
// environment variables, Console streams, FileAccessPolicy roots), so test
// classes must run sequentially to avoid cross-contamination.
[assembly: CollectionBehavior(DisableTestParallelization = true)]