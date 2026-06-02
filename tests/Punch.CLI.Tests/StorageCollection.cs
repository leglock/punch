using Xunit;

namespace Punch.CLI.Tests;

// Suites that mutate the shared static PunchStorage.DataDirectoryOverride must
// not run in parallel with one another or they clobber each other's data dir.
// xUnit never runs tests in the same collection concurrently, so tagging each
// such suite with [Collection(Name)] serializes them while leaving the rest of
// the assembly free to parallelize.
[CollectionDefinition(Name)]
public sealed class StorageCollection
{
    public const string Name = "PunchStorage state";
}
