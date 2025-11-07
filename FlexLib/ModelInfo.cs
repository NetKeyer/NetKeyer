using System.Collections.Generic;
using System.Collections.Immutable;

namespace Flex.Smoothlake.FlexLib
{
    public enum RadioPlatform
    {
        Microburst,
        DeepEddy,
        BigBend,
        DragonFire
    }

    public enum ModemSupport
    {
        Supported,
        NotSupported,
        SupportedDevOnly,
    }

    public enum ModemConfigurationType
    {
        Unknown,
        SingleModem,
        DualModemCopro,
        DualModemIndependent,
    }

    public class ModelInfo
    {
        public RadioPlatform Platform { get; init; }
        public bool IsMModel { get; init; }
        public bool IsDiversityAllowed { get; init; }
        public bool HasOledDisplay { get; init; }
        public bool HasLoopA { get; init; }
        public bool HasLoopB { get; init; }
        public bool Has4Meters { get; init; }
        public bool Has2Meters { get; init; }
        // 4m and 2m capabilities?
        public bool IsOscillatorSelectAvailable { get; init; }
        public bool HasBacklitFrontPanel { get; init; }
        public bool HasTransmitter { get; init; }
        public string ImageSource { get; init; }
        public int MaxDaxIqChannels { get; init; }
        public ImmutableList<string> SliceList { get; init; }
        
        public bool HasOverlordPa { get; init; }
        public ModemSupport SupportsRapidMModem { get; init; }
        public ImmutableList<ModemConfigurationType> SupportedRapidMModemConfigTypes { get; init; }

        private ModelInfo()
        {
        }

        public static ModelInfo GetModelInfoForModel(string modelName)
        {
            return ModelTable.GetValueOrDefault(modelName, ModelTable["DEFAULT"]);
        }
        
        // The redundancy to handle Maestro is janky here.  We need either runtime detection so that we can construct
        // the string, or we need to do something so that the resources show up at the same place.
        private static readonly ImmutableDictionary<string, ModelInfo> ModelTable = new Dictionary<string, ModelInfo>
        {
            {
                "DEFAULT", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = false,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6300-small.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-6300", new()
                {
                    Platform = RadioPlatform.Microburst,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6300-small.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-6400", new()
                {
                    Platform = RadioPlatform.DeepEddy,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-6400M", new()
                {
                    Platform = RadioPlatform.DeepEddy,
                    IsMModel = true,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600M.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-6500", new()
                {
                    Platform = RadioPlatform.Microburst,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = true,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = true,
                    HasLoopB = false,
                    Has4Meters = true,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6000-Cutout.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "FLEX-6600", new()
                {
                    Platform = RadioPlatform.DeepEddy,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "FLEX-6600M", new()
                {
                    Platform = RadioPlatform.DeepEddy,
                    IsMModel = true,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600M.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "FLEX-6700", new()
                {
                    Platform = RadioPlatform.Microburst,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = true,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = true,
                    HasLoopB = true,
                    Has4Meters = true,
                    Has2Meters = true,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6000-Cutout.png",
                    SliceList = new List<string> {"A", "B", "C", "D", "E", "F", "G", "H"}.ToImmutableList()
                }
            },
            {
                "FLEX-6700R", new()
                {
                    Platform = RadioPlatform.Microburst,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = true,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = false,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.NotSupported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6000-Cutout.png",
                    SliceList = new List<string> {"A", "B", "C", "D", "E", "F", "G", "H"}.ToImmutableList()
                }
            },
            {
                "FLEX-8400", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-8400M", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = true,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600M.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "FLEX-8600", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "FLEX-8600M", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = true,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600M.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "ML-9600W", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.Supported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "ML-9600X", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.Supported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "ML-9600", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.Supported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "CL-9300", new()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6600.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "RT-2122", new()
                {
                    Platform = RadioPlatform.DragonFire,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = false,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = false,
                    SupportsRapidMModem = ModemSupport.Supported,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/6400.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "AU-510", new ()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = true,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/A520.png",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "AU-510M", new ()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = true,
                    IsDiversityAllowed = false,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 2,
                    HasOverlordPa = true,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/A520M.jpg",
                    SliceList = new List<string> {"A", "B"}.ToImmutableList()
                }
            },
            {
                "AU-520", new ()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = false,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = true,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = true,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/A520.png",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            },
            {
                "AU-520M", new ()
                {
                    Platform = RadioPlatform.BigBend,
                    IsMModel = true,
                    IsDiversityAllowed = true,
                    HasOledDisplay = false,
                    IsOscillatorSelectAvailable = true,
                    HasBacklitFrontPanel = false,
                    HasTransmitter = true,
                    HasLoopA = false,
                    HasLoopB = false,
                    Has4Meters = false,
                    Has2Meters = false,
                    MaxDaxIqChannels = 4,
                    HasOverlordPa = true,
                    SupportsRapidMModem = ModemSupport.SupportedDevOnly,
                    SupportedRapidMModemConfigTypes = new List<ModemConfigurationType> {ModemConfigurationType.SingleModem,
                                                                                        ModemConfigurationType.DualModemCopro,
                                                                                        ModemConfigurationType.DualModemIndependent}.ToImmutableList(),
                    ImageSource = "pack://application:,,,/FlexLib;component/Images/A520M.jpg",
                    SliceList = new List<string> {"A", "B", "C", "D"}.ToImmutableList()
                }
            }
            
        }.ToImmutableDictionary();
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}