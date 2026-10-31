using System.Runtime.InteropServices;

namespace Beam.Windows.Display;

/// <summary>P/Invoke surface for the Windows display configuration API.</summary>
/// <remarks>
/// <para>
/// Beam uses <c>QueryDisplayConfig</c> rather than WMI or
/// <c>System.Windows.Forms.Screen</c> because it is the only API that exposes the
/// source/target split needed to reason about clone (duplicate) mode.
/// </para>
/// <para>
/// In duplicate mode one <em>source</em> drives two <em>targets</em>. The source owns the
/// desktop size and the DPI scale; each target owns its own signal mode. Changing the
/// shared source mode is the only way to remove black bars in duplicate mode.
/// </para>
/// </remarks>
public static class DisplayNative
{
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    // QueryDisplayConfig flags.
    public const uint QdcAllPaths = 0x00000001;
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const uint QdcDatabaseCurrent = 0x00000004;
    public const uint QdcVirtualModeAware = 0x00000010;
    public const uint QdcIncludeHmd = 0x00000020;

    // DISPLAYCONFIG_PATH flags.
    public const uint PathActive = 0x00000001;

    // DISPLAYCONFIG_DEVICE_INFO_TYPE values we need.
    public const int DeviceInfoGetSourceName = 1;
    public const int DeviceInfoGetTargetName = 2;
    public const int DeviceInfoGetTargetPreferredMode = 3;
    public const int DeviceInfoGetAdvancedColorInfo = 9;
    public const int DeviceInfoGetSdrWhiteLevel = 11;
    public const int DeviceInfoGetDpiScale = -3;
    public const int DeviceInfoSetDpiScale = -4;

    public const uint ModeIdxInvalid = 0xFFFFFFFF;

    // EnumDisplaySettingsEx mode index.
    public const int EnumCurrentSettings = -1;
    public const int EnumRegistrySettings = -2;

    // DEVMODE dmFields.
    public const uint DmPosition = 0x00000020;
    public const uint DmBitsPerPel = 0x00040000;
    public const uint DmPelsWidth = 0x00080000;
    public const uint DmPelsHeight = 0x00100000;
    public const uint DmDisplayFlags = 0x00200000;
    public const uint DmDisplayFrequency = 0x00400000;

    // ChangeDisplaySettingsEx flags.
    public const uint CdsTest = 0x00000002;
    public const uint CdsFullscreen = 0x00000004;
    public const uint CdsUpdateRegistry = 0x00000001;

    // SetDisplayConfig flags (SDC_*). The SDC_TOPOLOGY_* values are the same bit values
    // as DISPLAYCONFIG_TOPOLOGY_ID: INTERNAL=1, CLONE=2, EXTEND=4, EXTERNAL=8.
    public const uint SdcTopologyInternal = 0x00000001;
    public const uint SdcTopologyClone = 0x00000002;
    public const uint SdcTopologyExtend = 0x00000004;
    public const uint SdcTopologyExternal = 0x00000008;
    public const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    public const uint SdcApply = 0x00000080;
    public const uint SdcSaveToDatabase = 0x00000100;
    public const uint SdcAllowChanges = 0x00000400;

    public const int DispChangeSuccessful = 0;
    public const int DispChangeRestart = 1;
    public const int DispChangeFailed = -1;
    public const int DispChangeBadMode = -2;
    public const int DispChangeNotUpdated = -3;
    public const int DispChangeBadFlags = -4;
    public const int DispChangeBadParam = -5;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        ref uint currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements,
        DISPLAYCONFIG_MODE_INFO[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_DEVICE_INFO_HEADER requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_DPI_SCALE requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_SDR_WHITE_LEVEL requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_DPI_SCALE requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(
        string? device,
        uint devNum,
        ref DISPLAY_DEVICE displayDevice,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettingsEx(
        string deviceName,
        int modeNum,
        ref DEVMODE devMode,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? deviceName,
        ref DEVMODE devMode,
        IntPtr hwnd,
        uint flags,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    public static readonly IntPtr DpiAwarenessPerMonitorV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;

        public override readonly string ToString() => $"{HighPart:X8}:{LowPart:X8}";

        public readonly bool adapterIdEquals(LUID other) =>
            LowPart == other.LowPart && HighPart == other.HighPart;

        /// <summary>Parses a string of the form "HighPart:LowPart" (as produced by ToString()).</summary>
        public static bool TryParse(ReadOnlySpan<char> s, out LUID result)
        {
            result = default;
            int colon = s.IndexOf(':');
            if (colon < 0) return false;

            if (!int.TryParse(s[..colon], System.Globalization.NumberStyles.HexNumber, null, out int high))
                return false;
            if (!uint.TryParse(s[(colon + 1)..], System.Globalization.NumberStyles.HexNumber, null, out uint low))
                return false;

            result = new LUID { HighPart = high, LowPart = low };
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;

        public readonly double ToDouble() => Denominator == 0 ? 0 : (double)Numerator / Denominator;
        public override readonly string ToString() => Denominator is 0 or 1
            ? Numerator.ToString()
            : $"{Numerator}/{Denominator}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public int scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public int pixelFormat;
        public DISPLAYCONFIG_POINT position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public DISPLAYCONFIG_2DREGION PathSourceSize;
        public RECT DesktopImageRegion;
        public RECT DesktopImageClip;
    }

    /// <summary>
    /// <c>DISPLAYCONFIG_MODE_INFO</c>, 64 bytes. Declared as an explicit union so the
    /// source, target and desktop-image views can share one array.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)]
        public int infoType;

        [FieldOffset(4)]
        public uint id;

        [FieldOffset(8)]
        public LUID adapterId;

        [FieldOffset(16)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    public const int ModeInfoTypeSource = 1;
    public const int ModeInfoTypeTarget = 2;
    public const int ModeInfoTypeDesktopImage = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    /// <summary>Undocumented <c>DISPLAYCONFIG_GET_DPI_SCALE</c> (type <c>-3</c>), 32 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_DPI_SCALE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public int minScaleRel;
        public int curScaleRel;
        public int maxScaleRel;
    }

    /// <summary>Undocumented <c>DISPLAYCONFIG_SET_DPI_SCALE</c> (type <c>-4</c>), 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_DPI_SCALE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public int scaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_SDR_WHITE_LEVEL
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    /// <summary>Decodes the 3-letter EDID manufacturer id (5 bits per letter).</summary>
    public static string DecodeEdidManufacturer(ushort id)
    {
        if (id == 0)
        {
            return string.Empty;
        }

        Span<char> letters = stackalloc char[3];
        letters[0] = (char)('A' + ((id >> 10) & 0x1F) - 1);
        letters[1] = (char)('A' + ((id >> 5) & 0x1F) - 1);
        letters[2] = (char)('A' + (id & 0x1F) - 1);
        return new string(letters);
    }

    private const int OutputTechnologyInternal = 18;

    /// <summary><c>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL_VIRTUAL</c> (<c>0x80000000</c>).</summary>
    public const int OutputTechnologyInternalVirtual = unchecked((int)0x80000000);

    /// <summary>Maps <c>DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY</c> to a readable name.</summary>
    public static string OutputTechnologyName(int value) => value switch
    {
        -2 => "Uninitialized",
        -1 => "Other",
        0 => "VGA",
        1 => "S-Video",
        2 => "Composite",
        3 => "Component",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort external",
        11 => "DisplayPort embedded",
        12 => "UDI external",
        13 => "UDI embedded",
        14 => "SDTV dongle",
        15 => "Miracast",
        16 => "Indirect wired",
        17 => "HDMI embedded",
        18 => "Internal",
        19 => "Force UDIF",
        20 => "DisplayPort USB tunnel",
        OutputTechnologyInternalVirtual => "Internal (virtual)",
        _ => $"Unknown ({value})",
    };

    public static string RotationName(int value) => value switch
    {
        1 => "90",
        2 => "180",
        3 => "270",
        _ => "0",
    };
}
