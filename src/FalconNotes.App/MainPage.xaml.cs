using FalconNotes.App.Services;

namespace FalconNotes.App;

/// <summary>The only page: a <c>BlazorWebView</c> showing the Razor app from <c>FalconNotes.UI</c>.</summary>
public partial class MainPage : ContentPage
{
    /// <summary>Creates the page and connects the media handler, when there is one.</summary>
    /// <param name="services">Provides the optional <see cref="MediaHandler"/>.</param>
    public MainPage(IServiceProvider services)
    {
        InitializeComponent();

        if (services.GetService<MediaHandler>() is { } media)
        {
            blazorWebView.WebResourceRequested += (_, e) => media.Handle(e);
        }

#if DEBUG
        if (DevLaunch.StartPath is { } path)
        {
            blazorWebView.StartPath = path;
        }
#endif
    }
}
