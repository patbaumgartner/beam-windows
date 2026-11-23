using System.Runtime.InteropServices;
using Beam.Windows.Extras;

namespace Beam.Windows.Tests;

/// <summary>Pins the COM ABI without changing this machine's default audio device.</summary>
public class AudioControllerTests
{
    [Fact]
    public void PolicyConfigClient_UsesPolicyCoclass_NotDeviceEnumerator()
    {
        Type type = NestedType("PolicyConfigClientClass");

        Assert.Equal(
            new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),
            type.GUID);
    }

    [Fact]
    public void PolicyConfig_HasCorrectIidAndCompleteVtableThroughSetDefaultEndpoint()
    {
        Type type = NestedType("IPolicyConfig");

        Assert.Equal(new Guid("F8679F50-850A-41CF-9C72-430F290290C8"), type.GUID);
        string[] methods = type.GetMethods()
            .OrderBy(method => method.MetadataToken)
            .Select(method => method.Name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "GetMixFormat",
                "GetDeviceFormat",
                "ResetDeviceFormat",
                "SetDeviceFormat",
                "GetProcessingPeriod",
                "SetProcessingPeriod",
                "GetShareMode",
                "SetShareMode",
                "GetPropertyValue",
                "SetPropertyValue",
                "SetDefaultEndpoint",
                "SetEndpointVisibility",
            },
            methods);
    }

    [Fact]
    public void PropertyKey_IsGuidPlusPid_AndPropertyStoreUsesIt()
    {
        Type key = NestedType("PropertyKey");
        Type store = NestedType("IPropertyStore");

        Assert.Equal(20, Marshal.SizeOf(key));
        Assert.Equal(0, checked((int)Marshal.OffsetOf(key, "FormatId")));
        Assert.Equal(16, checked((int)Marshal.OffsetOf(key, "Pid")));
        Assert.Equal(key, store.GetMethod("GetValue")!.GetParameters()[0].ParameterType.GetElementType());
    }

    [Fact]
    public void PropertyKeys_UseDocumentedFormFactorAndFriendlyNamePids()
    {
        object formFactor = GetStaticField("PkeyAudioEndpointFormFactor");
        object friendlyName = GetStaticField("PkeyDeviceFriendlyName");

        Assert.Equal(0u, GetPid(formFactor));
        Assert.Equal(14u, GetPid(friendlyName));
    }

    private static Type NestedType(string name) =>
        typeof(AudioController).GetNestedType(name, System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing nested COM type {name}");

    private static object GetStaticField(string name) =>
        typeof(AudioController)
            .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

    private static uint GetPid(object key) =>
        (uint)key.GetType().GetField("Pid")!.GetValue(key)!;
}
