using Beam.Core.Display;
using Beam.Windows.Display;

namespace Beam.Windows.Tests;

public class DpiControllerRollbackTests
{
    [Fact]
    public void VerificationFailure_RollsBackToFreshOriginalScale()
    {
        int currentRel = 0; // recommended/current 200%
        var writes = new List<int>();
        bool result = DisplayNative.LUID.TryParse("0001594D:00000000", out DisplayNative.LUID adapter);
        Assert.True(result);

        bool succeeded = DpiController.SetPercentageAndVerifyCore(
            adapter,
            0,
            100,
            (_, _) => DpiScale.From(-4, currentRel, 4),
            (_, _, relative) =>
            {
                writes.Add(relative);
                currentRel = relative == 0 ? 0 : -3; // target 100 never materializes; rollback 200 does
                return true;
            },
            _ => { });

        Assert.False(succeeded);
        Assert.Equal(new[] { -4, 0 }, writes);
        Assert.Equal(0, currentRel); // original 200% was requested again and verified
    }

    [Fact]
    public void SuccessfulVerification_DoesNotRollBack()
    {
        int currentRel = 0;
        var writes = new List<int>();
        Assert.True(DisplayNative.LUID.TryParse("0001594D:00000000", out DisplayNative.LUID adapter));

        bool succeeded = DpiController.SetPercentageAndVerifyCore(
            adapter,
            0,
            100,
            (_, _) => DpiScale.From(-4, currentRel, 4),
            (_, _, relative) =>
            {
                writes.Add(relative);
                currentRel = relative;
                return true;
            },
            _ => { });

        Assert.True(succeeded);
        Assert.Equal(new[] { -4 }, writes);
        Assert.Equal(-4, currentRel);
    }

    [Fact]
    public void AlreadyAtTarget_DoesNotWriteOrSleep()
    {
        int writes = 0;
        int delays = 0;
        Assert.True(DisplayNative.LUID.TryParse("0001594D:00000000", out DisplayNative.LUID adapter));

        bool succeeded = DpiController.SetPercentageAndVerifyCore(
            adapter,
            0,
            200,
            (_, _) => DpiScale.From(-4, 0, 4),
            (_, _, _) =>
            {
                writes++;
                return true;
            },
            _ => delays++);

        Assert.True(succeeded);
        Assert.Equal(0, writes);
        Assert.Equal(0, delays);
    }

    [Fact]
    public void RejectedInitialWrite_DoesNotAttemptRollback()
    {
        int writes = 0;
        Assert.True(DisplayNative.LUID.TryParse("0001594D:00000000", out DisplayNative.LUID adapter));

        bool succeeded = DpiController.SetPercentageAndVerifyCore(
            adapter,
            0,
            100,
            (_, _) => DpiScale.From(-4, 0, 4),
            (_, _, _) =>
            {
                writes++;
                return false;
            },
            _ => { });

        Assert.False(succeeded);
        Assert.Equal(1, writes);
    }
}
