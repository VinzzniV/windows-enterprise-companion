using Wec.Core.Abstractions;
using Wec.Infrastructure.Directory;

namespace Wec.Infrastructure.IntegrationTests.Directory;

public sealed class BoundedDirectoryResultAccumulatorTests
{
    [Fact]
    public void LargePagedFixture_KeepsExactCountButMaterializesOnlyRequestedExamples()
    {
        const int pageSize = 500;
        const int totalEntries = 25_000;
        const int exampleLimit = 20;
        var accumulator = new BoundedDirectoryResultAccumulator(exampleLimit);
        int decodedEntries = 0;

        for (int pageStart = 0; pageStart < totalEntries; pageStart += pageSize)
        {
            int entriesOnPage = Math.Min(pageSize, totalEntries - pageStart);
            for (int offset = 0; offset < entriesOnPage; offset++)
            {
                if (accumulator.CountAndShouldRetain())
                {
                    decodedEntries++;
                    accumulator.Retain(new DirectoryEntryData(
                        $"CN=user-{pageStart + offset},DC=corp,DC=example",
                        new Dictionary<string, IReadOnlyList<string>>()));
                }
            }
        }

        BoundedDirectorySearchResult result = accumulator.Build();

        Assert.Equal(totalEntries, result.TotalCount);
        Assert.Equal(exampleLimit, result.Entries.Count);
        Assert.Equal(exampleLimit, decodedEntries);
    }

    [Fact]
    public void CountOnlyFixture_DoesNotMaterializeAnyEntries()
    {
        var accumulator = new BoundedDirectoryResultAccumulator(entryLimit: 0);

        for (int index = 0; index < 10_000; index++)
        {
            Assert.False(accumulator.CountAndShouldRetain());
        }

        BoundedDirectorySearchResult result = accumulator.Build();
        Assert.Equal(10_000, result.TotalCount);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void OffsetFixture_KeepsExactCountButMaterializesOnlyRequestedPage()
    {
        const int totalEntries = 25_000;
        const int entryOffset = 1_200;
        const int pageSize = 50;
        var accumulator = new BoundedDirectoryResultAccumulator(entryOffset, pageSize);
        int decodedEntries = 0;

        for (int index = 0; index < totalEntries; index++)
        {
            if (accumulator.CountAndShouldRetain())
            {
                decodedEntries++;
                accumulator.Retain(new DirectoryEntryData(
                    $"CN=user-{index},DC=corp,DC=example",
                    new Dictionary<string, IReadOnlyList<string>>()));
            }
        }

        BoundedDirectorySearchResult result = accumulator.Build();

        Assert.Equal(totalEntries, result.TotalCount);
        Assert.Equal(pageSize, decodedEntries);
        Assert.Equal($"CN=user-{entryOffset},DC=corp,DC=example", result.Entries[0].DistinguishedName);
        Assert.Equal($"CN=user-{entryOffset + pageSize - 1},DC=corp,DC=example", result.Entries[^1].DistinguishedName);
    }
}
