using System;
namespace Butthurt.Contracts;

public interface IPageView
{
    string Title { get; }
    event EventHandler? BackRequested;
}
