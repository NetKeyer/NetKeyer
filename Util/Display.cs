// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedMember.Local

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Flex.Util;

public class DisplayMode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public long x;
        public long y;
    }
        
    [Flags]
    private enum Dm
    {
        Orientation = 0x00000001,
        PaperSize = 0x00000002,
        PaperLength = 0x00000004,
        PaperWidth = 0x00000008,
        Scale = 0x00000010,
        Position = 0x00000020,
        NUP = 0x00000040,
        DisplayOrientation = 0x00000080,
        Copies = 0x00000100,
        DefaultSource = 0x00000200,
        PrintQuality = 0x00000400,
        Color = 0x00000800,
        Duplex = 0x00001000,
        YResolution = 0x00002000,
        TTOption = 0x00004000,
        Collate = 0x00008000,
        FormName = 0x00010000,
        LogPixels = 0x00020000,
        BitsPerPixel = 0x00040000,
        PelsWidth = 0x00080000,
        PelsHeight = 0x00100000,
        DisplayFlags = 0x00200000,
        DisplayFrequency = 0x00400000,
        ICMMethod = 0x00800000,
        ICMIntent = 0x01000000,
        MediaType = 0x02000000,
        DitherType = 0x04000000,
        PanningWidth = 0x08000000,
        PanningHeight = 0x10000000,
        DisplayFixedOutput = 0x20000000
    }
        
    // See: https://msdn.microsoft.com/en-us/library/windows/desktop/dd183565(v=vs.85).aspx
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Ansi)]
    private struct DisplayModeStruct
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        [FieldOffset(0)]
        public string dmDeviceName;
        [FieldOffset(32)]
        public Int16 dmSpecVersion;
        [FieldOffset(34)]
        public Int16 dmDriverVersion;
        [FieldOffset(36)]
        public Int16 dmSize;
        [FieldOffset(38)]
        public Int16 dmDriverExtra;
        [FieldOffset(40)]
        public Dm dmFields;

        [FieldOffset(44)]
        public Int16 dmOrientation;
        [FieldOffset(46)]
        public Int16 dmPaperSize;
        [FieldOffset(48)]
        public Int16 dmPaperLength;
        [FieldOffset(50)]
        public Int16 dmPaperWidth;
        [FieldOffset(52)]
        public Int16 dmScale;
        [FieldOffset(54)]
        public Int16 dmCopies;
        [FieldOffset(56)]
        public Int16 dmDefaultSource;
        [FieldOffset(58)]
        public Int16 dmPrintQuality;

        [FieldOffset(44)]
        public PointL dmPosition;
        [FieldOffset(52)]
        public Int32 dmDisplayOrientation;
        [FieldOffset(56)]
        public Int32 dmDisplayFixedOutput;

        [FieldOffset(60)]
        public short dmColor;
        [FieldOffset(62)]
        public short dmDuplex;
        [FieldOffset(64)]
        public short dmYResolution;
        [FieldOffset(66)]
        public short dmTTOption;
        [FieldOffset(68)]
        public short dmCollate;
        [FieldOffset(72)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        [FieldOffset(102)]
        public Int16 dmLogPixels;
        [FieldOffset(104)]
        public Int32 dmBitsPerPel;
        [FieldOffset(108)]
        public Int32 dmPelsWidth;
        [FieldOffset(112)]
        public Int32 dmPelsHeight;
        [FieldOffset(116)]
        public Int32 dmDisplayFlags;
        [FieldOffset(116)]
        public Int32 dmNup;
        [FieldOffset(120)]
        public Int32 dmDisplayFrequency;
    }
        
    private enum DisplayChangeResult
    {
        Successful = 0,
        Restart = 1,
        Failed = -1,
        BadMode = -2,
        NotUpdated = -3,
        BadFlags = -4,
        BadParam = -5,
        BadDualView = -6
    }
        
    [Flags]
    private enum DisplaySettingsFlags
    {
        CDS_NONE = 0,
        CDS_UPDATEREGISTRY = 0x00000001,
        CDS_TEST = 0x00000002,
        CDS_FULLSCREEN = 0x00000004,
        CDS_GLOBAL = 0x00000008,
        CDS_SET_PRIMARY = 0x00000010,
        CDS_VIDEOPARAMETERS = 0x00000020,
        CDS_ENABLE_UNSAFE_MODES = 0x00000100,
        CDS_DISABLE_UNSAFE_MODES = 0x00000200,
        CDS_RESET = 0x40000000,
        CDS_RESET_EX = 0x20000000,
        CDS_NORESET = 0x10000000
    }
        
    public const int ENUM_CURRENT_SETTINGS = -1;
    
    public enum Orientations
    {
        DEGREES_CW_0 = 0,
        DEGREES_CW_90 = 3,
        DEGREES_CW_180 = 2,
        DEGREES_CW_270 = 1
    }
    
    private class DisplayModeEnumerator : IEnumerator<DisplayMode>
    {
        private int _index = -1;
        private readonly string _displayName;

        public DisplayModeEnumerator(string displayName)
        {
            _displayName = displayName;
        }
        
        public DisplayMode Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            try
            {
                Current = new DisplayMode(_displayName, ++_index);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
        }
    }

    internal class DisplayModeEnumerable : IEnumerable<DisplayMode>
    {
        private readonly string _displayName;

        public DisplayModeEnumerable(string displayName)
        {
            _displayName = displayName;
        }
        
        public IEnumerator<DisplayMode> GetEnumerator() => new DisplayModeEnumerator(_displayName);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    
    private DisplayModeStruct _mode;
    private readonly string _displayName;

    public DisplayMode(string displayName, int index)
    {
        _displayName = displayName;
        if (EnumDisplaySettings(displayName, index, ref _mode) == 0)
            throw new InvalidOperationException("Cannot get display settings");
    }
    
    public Orientations Orientation
    {
        get => (Orientations) _mode.dmDisplayOrientation;

        set
        {
            if ((_mode.dmDisplayOrientation + (int)value) % 2 == 1) // Need to swap height and width?
            {
                (_mode.dmPelsHeight, _mode.dmPelsWidth) = (_mode.dmPelsWidth, _mode.dmPelsHeight);
            }

            _mode.dmDisplayOrientation = (int) value;

            var result = ChangeDisplaySettingsEx(_displayName, ref _mode, IntPtr.Zero,
                DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
                
            if (result != DisplayChangeResult.Successful)
                throw new InvalidOperationException($"Failed to update display: {result}");
        }
    }

    public int Width => _mode.dmPelsWidth;
    public int Height => _mode.dmPelsHeight;
    public string DeviceName => _mode.dmDeviceName;
    public DisplayConfig.Point Position => new() { X = (int)_mode.dmPosition.x, Y = (int)_mode.dmPosition.y };

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int EnumDisplaySettings(
        string lpszDeviceName, int iModeNum, ref DisplayModeStruct lpDevMode);
        
    [DllImport("user32.dll")]
    private static extern DisplayChangeResult ChangeDisplaySettingsEx(
        string lpszDeviceName, ref DisplayModeStruct lpDevMode, IntPtr hwnd,
        DisplaySettingsFlags dwflags, IntPtr lParam);
}

public class DisplayDevice
{
    [Flags]
    private enum DisplayDeviceStateFlags
    {
        /// <summary>The device is part of the desktop.</summary>
        AttachedToDesktop = 0x1,
        MultiDriver = 0x2,
        /// <summary>The device is part of the desktop.</summary>
        PrimaryDevice = 0x4,
        /// <summary>Represents a pseudo device used to mirror application drawing for remoting or other purposes.</summary>
        MirroringDriver = 0x8,
        /// <summary>The device is VGA compatible.</summary>
        VgaCompatible = 0x10,
        /// <summary>The device is removable; it cannot be the primary display.</summary>
        Removable = 0x20,
        /// <summary>The device has more display modes than its output devices support.</summary>
        ModesPruned = 0x8000000,
        Remote = 0x4000000,
        Disconnect = 0x2000000
    }
        
    // See: https://msdn.microsoft.com/en-us/library/windows/desktop/dd183569(v=vs.85).aspx
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DisplayDeviceStruct
    {
        [MarshalAs(UnmanagedType.U4)]
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        [MarshalAs(UnmanagedType.U4)]
        public DisplayDeviceStateFlags StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
    
    private DisplayDeviceStruct _display;
        
    private class DisplayDeviceEnumerator : IEnumerator<DisplayDevice>
    {
        private int _index = -1;

        public DisplayDevice Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            try
            {
                Current = new DisplayDevice((uint) ++_index);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
        }
    }
        
    private class DisplayDeviceEnumerable : IEnumerable<DisplayDevice>
    {
        public IEnumerator<DisplayDevice> GetEnumerator() => new DisplayDeviceEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public DisplayDevice(uint displayIndex)
    {
        _display.cb = Marshal.SizeOf(_display);
        if (EnumDisplayDevices(null, displayIndex, ref _display, 0) == false)
            throw new IndexOutOfRangeException($"Invalid display number: {displayIndex}");
    }

    public static IEnumerable<DisplayDevice> GetAllDisplays() => new DisplayDeviceEnumerable();

    public IEnumerable<DisplayMode> GetAllModes() => new DisplayMode.DisplayModeEnumerable(Name);
    public DisplayMode CurrentMode => new (Name, DisplayMode.ENUM_CURRENT_SETTINGS);
    public string Name => _display.DeviceName;
    public string Id => _display.DeviceID;
    public string Key => _display.DeviceKey;
    public string Description => _display.DeviceString;

    public bool IsAttachedToDesktop => _display.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayDevices(
        string lpDevice, uint iDevNum, ref DisplayDeviceStruct lpDisplayDevice,
        uint dwFlags);
}

public class DisplayConfigPath
{
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    internal struct InfoStruct
    {
        public SourceInfo Source;
        public TargetInfo Target;
        public uint Flags;
    }
    
    [StructLayout(LayoutKind.Explicit)]
    internal struct SourceInfo
    {
        [FieldOffset(0)] public DisplayConfig.LUid adapterId;
        [FieldOffset(8)] public uint id;
        [FieldOffset(12)] public uint modeInfoIdx;
        [FieldOffset(12)] public ushort cloneGroupId;
        [FieldOffset(14)] public ushort sourceModeInfoIdx;
        [FieldOffset(16)] public SourceInfoStatusFlags StatusStatusFlags;
    }
    
    internal struct TargetInfo
    {
        public DisplayConfig.LUid AdapterId;
        public uint Id;
        public TargetModeInfo TargetModeInfo;
        public VideoOutputTechnology OutputTechnology;
        public Rotation Rotation;
        public Scaling Scaling;
        public DisplayConfig.Rational RefreshRate;
        public DisplayConfig.ScanlineOrdering ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public TargetInfoStatusFlags StatusFlags;
    }
    
    internal struct TargetModeInfo
    {
        private uint _bitvector;

        public uint DesktopModeInfoIdx
        {
            get => _bitvector & 0xFFFF;
            set => _bitvector = value | _bitvector;
        }

        public uint TargetModeInfoIdx
        {
            get => (_bitvector & 0xFFFF0000) / 0x10000;
            set => _bitvector = (value * 0x10000) | _bitvector;
        }
    }
    
    [Flags]
    internal enum SourceInfoStatusFlags
    {
        None = 0x0,
        SourceInUse = 0x1
    }
    
    [Flags]
    internal enum TargetInfoStatusFlags
    {
        None = 0x00,
        InUse = 0x01,
        Forcible = 0x02,
        ForcedAvailabilityBoot = 0x04,
        ForcedAvailabilityPath = 0x08,
        ForcedAvailabilitySystem = 0x10
    }
    
    public enum Rotation
    {
        Identity = 1,
        Rotate90 = 2,
        Rotate180 = 3,
        Rotate270 = 4,
        ForceUInt32 = -1
    }
    
    public enum Scaling
    {
        Identity = 1,
        Centered = 2,
        Stretched = 3,
        AspectRatioCenteredMax = 4,
        Custom = 5,
        Preferred = 128,
        ForceUInt32 = -1
    }
    
    public enum VideoOutputTechnology
    {
        Other = -1,
        HD15 = 0,
        SVideo = 1,
        CompositeVideo = 2,
        ComponentVideo = 3,
        DVI = 4,
        HDMI = 5,
        LVDS = 6,
        DJpn = 8,
        SDI = 9,
        DisplayPortExternal = 10,
        DisplayPortEmbedded = 11,
        UdiExternal = 12,
        UdiEmbedded = 13,
        SdTvDongle = 14,
        MiraCast = 15,
        Internal = -2147483648,
        ForceUInt32 = -1
    }
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
    
    internal InfoStruct Path;

    internal DisplayConfigPath(InfoStruct path)
    {
        Path = path;
    }

    public Rotation TargetRotation
    {
        get => Path.Target.Rotation;
        set => Path.Target.Rotation = value;
    }

    public Scaling TargetScaling
    {
        get => Path.Target.Scaling;
        set => Path.Target.Scaling = value;
    }

    public VideoOutputTechnology OutputTechnology => Path.Target.OutputTechnology;
}

public class DisplayConfigMode
{
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    [StructLayout(LayoutKind.Explicit)]
    internal struct InfoStruct
    {
        [FieldOffset(0)] public InfoType infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public DisplayConfig.LUid adatpterId;
        [FieldOffset(16)] public TargetMode targetMode;
        [FieldOffset(16)] public SourceMode sourceMode;
        [FieldOffset(16)] public DesktopImageInfo desktopImageInfo;
    }
    
    [StructLayout(LayoutKind.Explicit)]
    internal struct VideoSignalInfo
    {
        [FieldOffset(0)] public ulong pixelRate;
        [FieldOffset(8)] public DisplayConfig.Rational hSyncFreq;
        [FieldOffset(16)] public DisplayConfig.Rational vSyncFreq;
        [FieldOffset(24)] public DisplayConfig.Region2D activeSize;
        [FieldOffset(32)] public DisplayConfig.Region2D totalSize;
        [FieldOffset(40)] public AdditionalSignalInfo AdditionalSignalInfo;
        [FieldOffset(40)] public uint videoStandard;
        [FieldOffset(44)] public DisplayConfig.ScanlineOrdering ScanlineOrdering;
    }
    
    internal struct SourceMode
    {
        public uint Width;
        public uint Height;
        public PixelFormat PixelFormat;
        public DisplayConfig.Point Position;
    }
    
    internal struct AdditionalSignalInfo
    {
        private const int V_SYNC_FREQ_DIVIDER_BIT_MASK = 0x3f;

        public ushort VideoStandard;
        private ushort _split;

        public int VSyncFreqDivider
        {
            get => _split & V_SYNC_FREQ_DIVIDER_BIT_MASK;
            set => _split = (ushort) ((_split & ~V_SYNC_FREQ_DIVIDER_BIT_MASK) | value);
        }
    }
    
    internal struct DesktopImageInfo
    {
        public DisplayConfig.Point PathSourceSize;
        public DisplayConfig.Rect DesktopImageRegion;
        public DisplayConfig.Rect DesktopImageClip;
    }
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
    
    internal enum InfoType
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3,
        ForceUInt32 = -1
    }
    
    internal enum PixelFormat
    {
        Bpp8 = 1,
        Bpp16 = 2,
        Bpp24 = 3,
        Bpp32 = 4,
        NonGdi = 5,
        ForceUInt32 = -1
    }

    internal struct TargetMode
    {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        public VideoSignalInfo TargetVideoSignalInfo;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
    }
    
    private readonly InfoStruct _mode;

    internal DisplayConfigMode(InfoStruct mode)
    {
        _mode = mode;
    }

    public (uint, uint) Resolution => (_mode.sourceMode.Width, _mode.sourceMode.Height);
}

public static class DisplayConfig
{
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }
    
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    internal struct LUid
    {
        public uint LowPart;
        public uint HighPart;
    }
    
    internal struct Region2D
    {
        public uint Cx;
        public uint Cy;
    }
    
    public struct Point
    {
        public int X;
        public int Y;
    }

    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

    public enum ScanlineOrdering
    {
        Unspecified = 0,
        Progressive = 1,
        Interlaced = 2,
        InterlacedUpperFieldFirst = Interlaced,
        InterlacedLowerFieldFirst = 3,
        ForceUInt32 = -1
    }
    
    [Flags]
    public enum PathFlags
    {
        AllPaths = 0x01,
        OnlyActivePaths = 0x02,
        DatabaseCurrent = 0x04,
        VirtualModeAware = 0x10,
        IncludeHmd = 0x20,
        VirtualRefreshRateAware = 0x40
    }

    [Flags]
    private enum DisplayConfigSetFlags: uint
    {
        Apply = 0x00000080,
        NoOptimization = 0x00000100,
        UseSuppliedDisplayConfig = 0x00000020,
        SaveToDatabase = 0x00000200,
        Validate = 0x00000040,
        AllowChanges = 0x00000400,
        TopologyClone = 0x00000002,
        TopologyExtend = 0x00000004,
        TopologyInternal = 0x00000001,
        TopologyExternal = 0x00000008,
        TopologySupplied = 0x00000010,
        UseDatabaseCurrent = TopologyInternal | TopologyClone | TopologyExtend | TopologyExternal,
        PathPersistIfRequired = 0x00000800,
        ForceModeEnumeration = 0x00001000,
        AllowPathOrderChanges = 0x00002000,
        VirtualModeAware = 0x00008000,
        VirtualRefreshRateAware = 0x00020000,
    }
    
    private enum DisplayConfigTopologyId
    {
        Internal = 1,
        Clone = 2,
        Extend = 4,
        External = 8,
        ForceUInt32 = -1
    }

    private static (DisplayConfigPath.InfoStruct[] paths, DisplayConfigMode.InfoStruct[] modes) QueryConfig(PathFlags pathFlags)
    {
        uint numPathArrayElements = 0;
        uint numModeInfoArrayElements = 0;

        var result =
            GetDisplayConfigBufferSizes((uint) pathFlags, ref numPathArrayElements, ref numModeInfoArrayElements);
        if (result != ResultCode.Successful)
            throw new InvalidOperationException($"Can't get display config sizes: {result}");

        Debug.WriteLine($"We have {numPathArrayElements} path elements and {numModeInfoArrayElements} mode elements");

        var paths = new DisplayConfigPath.InfoStruct[numPathArrayElements];
        var modes = new DisplayConfigMode.InfoStruct[numModeInfoArrayElements];

        result = QueryDisplayConfig((uint) pathFlags, ref numPathArrayElements, paths, ref numModeInfoArrayElements,
            modes, null);

        if (result != ResultCode.Successful)
            throw new InvalidOperationException($"Can't query the display configuration: {result}");

        return (paths, modes);
    }

    public static IEnumerable<DisplayConfigPath> GetPaths(PathFlags pathFlags)
    {
        var (paths, _) = QueryConfig(pathFlags);
        
        return paths.Select(p => new DisplayConfigPath(p));
    }

    public static void SetPaths(IEnumerable<DisplayConfigPath> paths)
    {
        var pathStructs = paths.Select(p => p.Path).ToArray();

        var (_, modes) = QueryConfig(PathFlags.OnlyActivePaths);

        var result = SetDisplayConfig((uint) pathStructs.Length, pathStructs, (uint) modes.Length, modes,
            (uint) (DisplayConfigSetFlags.UseSuppliedDisplayConfig | DisplayConfigSetFlags.SaveToDatabase | DisplayConfigSetFlags.Apply |
                    DisplayConfigSetFlags.AllowChanges));
        if (result != ResultCode.Successful)
            throw new InvalidOperationException($"Can't set the display configuration: {result}");
    }

    public static IEnumerable<DisplayConfigMode> GetModes(PathFlags pathFlags)
    {
        var (_, modes) = QueryConfig(pathFlags);
        
        return modes.Select(m => new DisplayConfigMode(m));
    }
    
    #region Native Methods
    
    private enum ResultCode
    {
        Successful = 0x0000,
        InvalidParameter = 0x0057,
        NotSupported = 0x0032,
        AccessDenied = 0x0005,
        GenFailure = 0x001F,
        BadConfiguration = 0x064A
    }

    [DllImport("user32.dll")]
    private static extern ResultCode GetDisplayConfigBufferSizes(uint pathFlags, ref uint numPathArrayElements,
        ref uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern ResultCode QueryDisplayConfig(uint flags,
        ref uint numPathArrayElements, [Out] DisplayConfigPath.InfoStruct[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DisplayConfigMode.InfoStruct[] modeArray,
        DisplayConfigTopologyId[] currentTopologyId);

    [DllImport("user32.dll")]
    private static extern ResultCode SetDisplayConfig(
        uint numPathArrayElements, [In] DisplayConfigPath.InfoStruct[] paths,
        uint numModeArrayElements, [In] DisplayConfigMode.InfoStruct[] modes,
        uint flags);
    
    #endregion
}

public static class DisplayHelper
{
    public static void FixMaestroCExternalMonitors()
    {
        Debug.WriteLine("Fixing Maestro C External Monitors");
        var displayPaths = DisplayConfig.GetPaths(DisplayConfig.PathFlags.OnlyActivePaths).ToList();
                    
        //  In factory we might not have the internal display plugged in, so we have to handle whether this is null
        var firstMonitor = displayPaths.FirstOrDefault(p => p.OutputTechnology == DisplayConfigPath.VideoOutputTechnology.Internal);
        if (firstMonitor != null)
            firstMonitor.TargetRotation = DisplayConfigPath.Rotation.Rotate270;
                    
        var externalMonitor = displayPaths.FirstOrDefault(p => p.OutputTechnology == DisplayConfigPath.VideoOutputTechnology.HDMI);
        if (externalMonitor != null)
        {
            externalMonitor.TargetRotation = DisplayConfigPath.Rotation.Identity;
            externalMonitor.TargetScaling = DisplayConfigPath.Scaling.AspectRatioCenteredMax;
        }

        DisplayConfig.SetPaths(displayPaths);
    }
}