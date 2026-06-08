namespace ShowCast.Core;

public class NetworkSettings
{
    public bool   TcpEnabled       { get; set; } = false;
    public int    TcpPort          { get; set; } = 5100;
    public string TcpPassword      { get; set; } = "";
    public string BindAdapterName  { get; set; } = "";
}
