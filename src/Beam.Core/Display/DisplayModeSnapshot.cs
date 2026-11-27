namespace Beam.Core.Display;

/// <summary>One display source mode captured before Beam changed it.</summary>
public sealed record DisplayModeSnapshot(string GdiName, uint Width, uint Height, uint Frequency);
