global using Xunit;

// One real database and one API host for the whole run; tests share them, so run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
