namespace Butthurt.Contracts.Models;


public enum SideBarType
{
    Header,

    /// <summary>Things physically connected to you: toys, shockers…</summary>
    Hardware,
    Streaming,
    Plugins,
    Footer
}

public static class SideBarTypeExtensions
{
    public static string DisplayName(this SideBarType type) => type switch
    {
        SideBarType.Header => "MAIN",
        SideBarType.Hardware => "HARDWARE",
        SideBarType.Plugins => "GAMES",
        SideBarType.Streaming => "STREAMING",
        SideBarType.Footer => "MORE",
        _ => type.ToString().ToUpperInvariant()
    };
}
