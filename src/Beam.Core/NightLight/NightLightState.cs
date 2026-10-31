namespace Beam.Core.NightLight;

/// <summary>
/// How Night Light reached its current state.
/// </summary>
public enum NightLightTransitionCause
{
    /// <summary>The schedule (sunset/sunrise or fixed hours) changed the state.</summary>
    Schedule = 0,

    /// <summary>The user (or an application acting for the user) changed the state.</summary>
    Manual = 1,
}

/// <summary>
/// The semantic content of the Windows Night Light <c>bluelightreductionstate</c> value.
/// </summary>
/// <remarks>
/// The settings value (strength, schedule) is deliberately not modelled: Beam never
/// changes it, only the on/off state.
/// </remarks>
public sealed record NightLightState(
    bool Active,
    NightLightTransitionCause TransitionCause,
    uint PrologueEpochSeconds,
    long ModifiedFileTime)
{
    public static NightLightState Off(uint epochSeconds, long fileTime) =>
        new(false, NightLightTransitionCause.Manual, epochSeconds, fileTime);

    public static NightLightState On(uint epochSeconds, long fileTime) =>
        new(true, NightLightTransitionCause.Manual, epochSeconds, fileTime);
}
