using ZhuoDazi.Services;

var files = Enumerable.Range(1, 100).Select(index => $"pet-{index:000}.gif").ToArray();
var library = new GifLibraryService();

var firstCycle = TakeCycle(library, files);
Require(firstCycle.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length,
    "The first cycle repeated a GIF before exhausting the library.");

var secondCycle = TakeCycle(library, files, firstCycle[^1]);
Require(secondCycle[0] != firstCycle[^1],
    "The first GIF of a new cycle repeated the previous cycle's final GIF.");
Require(secondCycle.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length,
    "The second cycle repeated a GIF before exhausting the library.");

var single = new[] { "only.gif" };
Require(library.Pick(single, single[0]) == single[0],
    "A one-item library did not return its only GIF.");

var changed = new[] { "new-a.gif", "new-b.gif", "new-c.gif" };
var changedSelections = TakeCycle(library, changed);
Require(changedSelections.All(changed.Contains),
    "A selection from the previous library survived a pool change.");
Require(changedSelections.Distinct(StringComparer.OrdinalIgnoreCase).Count() == changed.Length,
    "The changed library repeated a GIF before exhausting its pool.");

Console.WriteLine("GifLibraryService checks passed.");

static string[] TakeCycle(GifLibraryService library, IReadOnlyList<string> files, string? previous = null)
{
    var selections = new string[files.Count];
    for (var index = 0; index < selections.Length; index++)
    {
        selections[index] = library.Pick(files, previous)
            ?? throw new InvalidOperationException("Selection unexpectedly returned null.");
        Require(previous is null || files.Count == 1
            || !string.Equals(selections[index], previous, StringComparison.OrdinalIgnoreCase),
            "Two consecutive selections returned the same GIF.");
        previous = selections[index];
    }
    return selections;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
