namespace Butthurt.Contracts.Plugins;

public interface IPlugin
{
    string Name { get; }
    void Initialize();
    string Execute(string input);
}
