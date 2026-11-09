using Beam.Core.Display;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

/// <summary>
/// UC-12 property tests for the 16:9 mode selection: for random mode lists and random
/// driver accept/reject outcomes, the selected mode is always null or a member of the
/// original candidate list — never an invented mode, never one outside the list.
/// The RNG is seeded, so the tests are deterministic.
/// </summary>
public class DisplayModeControllerPropertyTests
{
    private static DisplayMode Mode(uint w, uint h, double hz) => new(w, h, hz);

    private static readonly uint[] Dims = { 640, 720, 768, 800, 1024, 1080, 1200, 1280, 1440, 1600, 1920, 2160, 2560, 3440, 3840 };

    [Fact]
    public void SelectFirstAccepted_RandomListsAndRandomFailures_ResultIsNullOrMemberOfInput()
    {
        var rng = new Random(20261108);
        var pool = new[]
        {
            Mode(3840, 2160, 60), Mode(3840, 2160, 30), Mode(2560, 1440, 60),
            Mode(1920, 1080, 60), Mode(1920, 1080, 30), Mode(1366, 768, 60),
            Mode(1280, 720, 60), Mode(1920, 1200, 60), Mode(2560, 1600, 60),
        };

        for (int trial = 0; trial < 500; trial++)
        {
            // Random sublist of random size (possibly empty).
            int count = rng.Next(0, pool.Length + 1);
            var list = pool.OrderBy(_ => rng.Next()).Take(count).ToList();

            // Random accept/reject per position, as a flaky driver would produce.
            var accept = new bool[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                accept[i] = rng.Next(100) < 60;
            }

            int calls = 0;
            DisplayMode? result = DisplayModeController.SelectFirstAccepted(list, _ =>
            {
                bool ok = accept[calls];
                calls++;
                return ok;
            });

            if (result is null)
            {
                Assert.Equal(list.Count, calls); // every candidate was tried, none accepted
            }
            else
            {
                Assert.Contains(result, list);                    // UC-12 property: member of the input list
                int firstAccepted = Array.IndexOf(accept, true);
                Assert.Equal(firstAccepted + 1, calls);           // short-circuits at the first accept
                Assert.Equal(list[firstAccepted], result);        // and returns exactly that candidate
            }
        }
    }

    [Fact]
    public void Rank16x9_RandomInput_OutputIsSubsetOfInput_All169_SortedByPixels()
    {
        var rng = new Random(42);

        for (int trial = 0; trial < 200; trial++)
        {
            var input = Enumerable.Range(0, rng.Next(0, 25))
                .Select(_ => Mode(Dims[rng.Next(Dims.Length)], Dims[rng.Next(Dims.Length)], 30 + rng.Next(0, 91)))
                .ToList();

            IReadOnlyList<DisplayMode> ranked = DisplayModeController.Rank16x9(input);

            Assert.All(ranked, m => Assert.Contains(m, input)); // never invents a mode
            Assert.All(ranked, m => Assert.Equal(AspectKind.SixteenNine, AspectRatio.Classify(m)));
            Assert.True(ranked.Count <= input.Count);
            for (int i = 1; i < ranked.Count; i++)
            {
                Assert.True(ranked[i - 1].Pixels >= ranked[i].Pixels); // ranked by pixels descending
            }
        }
    }

    [Fact]
    public void RankThenBound_RandomInput_SubsetOfInput_AndCappedAtMaxCandidates()
    {
        // The full production pipeline (rank → bound, as used by TrySet16x9) must never
        // emit a mode that was not in the driver's list and never exceed the UC-12
        // rule 5 cap, no matter how many modes the driver reports.
        var rng = new Random(7);

        for (int trial = 0; trial < 200; trial++)
        {
            var input = Enumerable.Range(0, rng.Next(0, 30))
                .Select(_ => Mode(Dims[rng.Next(Dims.Length)], Dims[rng.Next(Dims.Length)], 30 + rng.Next(0, 91)))
                .ToList();

            IReadOnlyList<DisplayMode> selected =
                DisplayModeController.BoundedCandidates(DisplayModeController.Rank16x9(input));

            Assert.True(selected.Count <= DisplayModeController.MaxCandidates);
            Assert.All(selected, m => Assert.Contains(m, input));
        }
    }

    [Fact]
    public void SelectFirstAccepted_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => DisplayModeController.SelectFirstAccepted(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => DisplayModeController.SelectFirstAccepted(Array.Empty<DisplayMode>(), null!));
    }
}
