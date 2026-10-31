namespace Beam.Core.NightLight;

/// <summary>A captured Night Light state value, sufficient to restore it later.</summary>
public sealed record NightLightSnapshot(
    string KeyPath,
    string ValueName,
    byte[] RawBytes,
    NightLightBlob Blob);

/// <summary>Result of a Night Light write, including per-key detail.</summary>
public sealed record NightLightWriteResult(
    bool Success,
    string Message,
    IReadOnlyList<string> Details);
