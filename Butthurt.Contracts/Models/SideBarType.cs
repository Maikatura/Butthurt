namespace Butthurt.Contracts.Models;


public enum SideBarType
{
    Header,
    Plugins,
    Streaming,
    Footer
}

public static class SideBarTypeExtensions
{
    public static string DisplayName(this SideBarType type) => type switch
    {
        SideBarType.Header => "MAIN",
        SideBarType.Plugins => "GAMES",
        SideBarType.Streaming => "STREAMING",
        SideBarType.Footer => "MORE",
        _ => type.ToString().ToUpperInvariant()
    };
}
