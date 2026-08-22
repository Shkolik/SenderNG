namespace CNC.Core.Services;

public class AppSettings
{
    public int DemoAxes { get; set; } = 3;
    public string Mode { get; set; } = "Mill";
    public string Port { get; set; } = "auto";
    public Dictionary<string, object> Modules { get; set; } = new();
}
