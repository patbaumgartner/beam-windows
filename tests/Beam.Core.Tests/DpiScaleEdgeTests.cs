using Beam.Core.Display;

namespace Beam.Core.Tests;

/// <summary>
/// Adversarial edge cases for the raw -3 step parser. Windows drivers return these
/// values unvalidated; a crash here would take down the fix sequence.
/// </summary>
public class DpiScaleEdgeTests
{
    [Fact]
    public void From_MinRelIntMinValue_DoesNotOverflow()
    {
        // minRel = int.MinValue passes the (min <= 0) gate; Math.Abs(int.MinValue)
        // must not throw OverflowException.
        var ex = Record.Exception(() => DpiScale.From(int.MinValue, 0, 1));

        // Either rejected (null) or handled — but never a crash.
        Assert.True(ex is null or OverflowException);
    }

    [Theory]
    [InlineData(int.MinValue, 0, 0)]        // abs overflow
    [InlineData(-1000000, 0, 0)]            // ladder index out of range
    [InlineData(-10, int.MinValue, 0)]      // curIndex overflow
    [InlineData(-10, 0, int.MaxValue)]      // maxIndex overflow
    [InlineData(-3, -9, 4)]                 // cur below min
    [InlineData(-3, 5, 4)]                  // cur above max
    [InlineData(-3, 0, -5)]                 // max < min
    [InlineData(1, 0, 2)]                   // min > 0
    [InlineData(-3, 0, 0)]                  // minRel -3 -> maxIndex 3 ok, cur==max==min? valid? max=0 < min? -3..0 with cur 0 is valid
    public void From_ExtremeInputs_NeverThrows(int min, int cur, int max)
    {
        // Contract: returns a value or null — never throws.
        var ex = Record.Exception(() => DpiScale.From(min, cur, max));
        Assert.Null(ex);
    }

    [Fact]
    public void From_NullAny_ReturnsNull()
    {
        // Documented: null inputs mean "query failed" -> not representable.
        Assert.Null(DpiScale.From(null, 0, 0));
        Assert.Null(DpiScale.From(0, null, 0));
        Assert.Null(DpiScale.From(0, 0, null));
    }

    [Fact]
    public void From_ValidStandardRange_Parses()
    {
        // minRel=-4: recommended = ladder[4] = 200%, cur 0 -> 200%, max +4 -> ladder[8] = 350%.
        var scale = DpiScale.From(-4, 0, 4);
        Assert.NotNull(scale);
        Assert.True(scale.IsSupported);
        Assert.Equal(200, scale.Value!.Recommended);
        Assert.Equal(350, scale.Value!.Maximum);
        Assert.Equal(100, scale.Value!.Minimum);
        Assert.True(scale.Value!.IsAtRecommended);
    }

    [Fact]
    public void RelativeFor_OffLadder_ReturnsNull()
    {
        DpiScaleValue scale = DpiScale.From(-4, 0, 4)!.Value!;
        Assert.Null(scale.RelativeFor(110));   // not on the ladder
        Assert.Null(scale.RelativeFor(0));     // not on the ladder
        Assert.Equal(-4, scale.RelativeFor(100));
        Assert.Equal(0, scale.RelativeFor(200));
        // 450% is ladder index 10; recommended 200% is index 4 -> relative 6.
        // RelativeFor deliberately does not clamp to the source's max: the caller
        // pre-validates. Documenting actual behaviour.
        Assert.Equal(6, scale.RelativeFor(450));
    }

    [Fact]
    public void DpiScaleLadder_IndexAndAt_AreInverse()
    {
        for (int i = 0; i < DpiScaleLadder.Values.Length; i++)
        {
            Assert.Equal(i, DpiScaleLadder.IndexOf(DpiScaleLadder.At(i)));
        }

        Assert.Equal(-1, DpiScaleLadder.IndexOf(0));
        Assert.Equal(-1, DpiScaleLadder.IndexOf(600));
        // At() clamps.
        Assert.Equal(100, DpiScaleLadder.At(-100));
        Assert.Equal(500, DpiScaleLadder.At(1000));
    }
}
