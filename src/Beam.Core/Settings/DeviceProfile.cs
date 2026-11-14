namespace Beam.Core.Settings;

/// <summary>
/// C1: a display Beam has seen before, remembered across sessions in settings.json so the
/// Devices table in the settings window can show name + identity and offer per-device
/// actions (auto-apply / ignore / forget).
/// </summary>
/// <param name="Identity"><see cref="Beam.Core.Display.DisplayTarget.Identity"/> — stable across reboots.</param>
/// <param name="FriendlyName">Last known friendly name (may change; identity does not).</param>
/// <param name="AutoApply">
/// False = this specific display must not trigger the automatic fix when it appears
/// (the user can still apply manually). True/absent = default behavior.
/// </param>
/// <param name="FirstSeenUtc">When Beam first saw this display (ISO timestamp).</param>
public sealed record DeviceProfile(
    string Identity,
    string FriendlyName,
    bool AutoApply,
    string FirstSeenUtc);
