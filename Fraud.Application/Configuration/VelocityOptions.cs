public class VelocityOptions
{
    public const string SectionName = "Velocity";

    public int AccountThreshold { get; set; }

    public int DeviceThreshold { get; set; }

    public int IpThreshold { get; set; }

    public int WindowSeconds { get; set; }

    public int DeduplicationRetentionSeconds { get; set; } = 604800;
}