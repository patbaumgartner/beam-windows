namespace Beam.Core.Status;

/// <summary>
/// B6: the live session status the tray publishes as <c>status.json</c>, so
/// <c>beam status</c> can report what the running tray is actually doing
/// (UC-16: the CLI must tell the truth about the tray).
/// </summary>
/// <remarks>
/// The record is written on every session state transition and cleared when the tray
/// exits. <see cref="AppliedSourceMode"/>, <see cref="AppliedDpiPercent"/> and
/// <see cref="NightLightDisabled"/> describe what the active fix applied; they are null
/// while idle. The record carries no user/machine names (A4 privacy rule).
/// </remarks>
public sealed record StatusFileRecord(
    string State,
    string? Reason,
    string UpdatedAtUtc,
    int Pid,
    string? AppliedSourceMode = null,
    int? AppliedDpiPercent = null,
    bool? NightLightDisabled = null);
