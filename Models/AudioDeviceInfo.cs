namespace NetKeyer.Models
{
    public class AudioDeviceInfo
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string DisplayName => string.IsNullOrEmpty(DeviceId) || DeviceId == "System Default"
                                       ? "System Default"
                                       : Name;
        public override string ToString() => DisplayName;
    }
}
