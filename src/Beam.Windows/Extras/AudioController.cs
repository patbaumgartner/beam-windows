using System.Runtime.InteropServices;
using Beam.Core.Ports;

namespace Beam.Windows.Extras;

/// <summary>
/// Switches the default audio render endpoint to the HDMI/projector output while
/// presenting, and restores the previous endpoint afterwards, using the Core Audio
/// COM API (<c>IMMDeviceEnumerator</c> for enumeration, <c>IPolicyConfig</c> for the
/// default-endpoint switch).
/// </summary>
/// <remarks>
/// <para>
/// Every method is best-effort and never throws: audio is a convenience extra, so a
/// failure here must never fail the presentation session.
/// </para>
/// <para>
/// HDMI/projector endpoints are detected authoritatively via
/// <c>PKEY_AudioEndpoint_FormFactor == DigitalAudioDisplayDevice</c> (9), with the
/// device friendly name containing "HDMI" as a fallback. IIDs and vtable orders match
/// the Windows SDK headers (<c>mmdeviceapi.h</c>, <c>audiopolicy.h</c>).
/// </para>
/// </remarks>
public sealed class AudioController : IAudioController
{
    /// <summary>Endpoint form factor: digital audio on a display device (HDMI/DP).</summary>
    private const uint FormFactorDigitalAudioDisplayDevice = 9;

    private const ushort VtUi4 = 19;
    private const ushort VtLpwstr = 31;
    private const uint StgmRead = 0;
    private const uint DeviceStateActive = 0x1;

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    // PKEY_AudioEndpoint_FormFactor, pid 0.
    private static readonly Guid PkeyAudioEndpointFormFactor = new("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E");

    // PKEY_Device_FriendlyName, pid 14.
    private static readonly Guid PkeyDeviceFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] // CLSID_MMDeviceEnumerator
    private class MMDeviceEnumeratorClass
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")] // IID_IMMDeviceEnumerator
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")] // IID_IMMDeviceCollection
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);

        int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")] // IID_IMMDevice
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        // Declared for vtable position only — never called.
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr ppInterface);

        int OpenPropertyStore(uint stgmAccess, out IPropertyStore store);

        int GetId(out IntPtr id); // LPWSTR allocated with CoTaskMemAlloc

        int GetState(out uint state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")] // IID_IPropertyStore
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint count);

        int GetAt(uint index, out Guid key);

        int GetValue(ref Guid key, out PropVariant value);

        int SetValue(ref Guid key, ref PropVariant value);

        int Commit();
    }

    [ComImport]
    [Guid("F8679F50-850A-41CE-8C69-0AAE27B02D41")] // IID_IPolicyConfig
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Declared for vtable position only — never called.
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr waveFormat);

        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr iconPath);

        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    }

    /// <summary>Minimal PROPVARIANT (16 bytes on x64): type tag plus a value union.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(2)] public ushort Reserved1;
        [FieldOffset(4)] public ushort Reserved2;
        [FieldOffset(6)] public ushort Reserved3;
        [FieldOffset(8)] public uint UInt32; // VT_UI4, e.g. the endpoint form factor
        [FieldOffset(8)] public IntPtr Ptr;  // VT_LPWSTR, e.g. the friendly name
    }

    /// <inheritdoc />
    public string? GetDefaultRenderEndpoint()
    {
        try
        {
            IMMDeviceEnumerator enumerator = CreateEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out IMMDevice device) != 0)
                {
                    return null;
                }

                try
                {
                    return ReadDeviceId(device);
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? SwitchToHdmi()
    {
        try
        {
            string? hdmiId = FindHdmiRenderEndpoint();
            if (hdmiId is null)
            {
                return null;
            }

            return SetDefaultEndpoint(hdmiId) ? hdmiId : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool Restore(string endpointId)
    {
        try
        {
            if (string.IsNullOrEmpty(endpointId))
            {
                return false;
            }

            // Only restore if the previous endpoint still exists (it may have been unplugged).
            IMMDeviceEnumerator enumerator = CreateEnumerator();
            try
            {
                if (enumerator.GetDevice(endpointId, out IMMDevice device) != 0)
                {
                    return false;
                }

                Marshal.ReleaseComObject(device);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            return SetDefaultEndpoint(endpointId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates active render endpoints and returns the id of the first one whose
    /// form factor is <c>DigitalAudioDisplayDevice</c>; falls back to the first endpoint
    /// whose friendly name mentions HDMI.
    /// </summary>
    private static string? FindHdmiRenderEndpoint()
    {
        IMMDeviceEnumerator enumerator = CreateEnumerator();
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out IMMDeviceCollection collection) != 0)
            {
                return null;
            }

            try
            {
                _ = collection.GetCount(out int count);
                string? byName = null;

                for (int i = 0; i < count; i++)
                {
                    if (collection.Item(i, out IMMDevice device) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        string? id = ReadDeviceId(device);
                        if (id is null)
                        {
                            continue;
                        }

                        (uint? formFactor, string? friendlyName) = ReadEndpointInfo(device);
                        if (formFactor == FormFactorDigitalAudioDisplayDevice)
                        {
                            return id; // authoritative: this endpoint is display audio (HDMI/DP)
                        }

                        if (byName is null && friendlyName?.Contains("HDMI", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            byName = id; // fallback: name-based detection
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                return byName;
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static (uint? FormFactor, string? FriendlyName) ReadEndpointInfo(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out IPropertyStore store) != 0)
        {
            return (null, null);
        }

        try
        {
            uint? formFactor = null;
            string? friendlyName = null;

            Guid key = PkeyAudioEndpointFormFactor;
            if (store.GetValue(ref key, out PropVariant ff) == 0 && ff.Vt == VtUi4)
            {
                formFactor = ff.UInt32;
            }

            key = PkeyDeviceFriendlyName;
            if (store.GetValue(ref key, out PropVariant name) == 0
                && name.Vt == VtLpwstr
                && name.Ptr != IntPtr.Zero)
            {
                try
                {
                    friendlyName = Marshal.PtrToStringUni(name.Ptr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name.Ptr);
                }
            }

            return (formFactor, friendlyName);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static string? ReadDeviceId(IMMDevice device)
    {
        if (device.GetId(out IntPtr idPtr) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(idPtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(idPtr);
        }
    }

    private static bool SetDefaultEndpoint(string deviceId)
    {
        // The MMDeviceEnumerator coclass also implements IPolicyConfig; casting performs the QI.
        var policy = (IPolicyConfig)new MMDeviceEnumeratorClass();
        try
        {
            return policy.SetDefaultEndpoint(deviceId, ERole.Multimedia) == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
}
