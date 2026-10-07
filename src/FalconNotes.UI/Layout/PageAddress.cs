using Microsoft.AspNetCore.Components;

namespace FalconNotes.UI.Layout;

/// <summary>The current page's path and query, as the shell and filters read them.</summary>
public static class PageAddress
{
    /// <summary>Splits the current address.</summary>
    /// <param name="navigation">The navigation manager.</param>
    /// <returns>The path (starting with <c>/</c>) and the query values.</returns>
    public static (string Path, IReadOnlyDictionary<string, string> Query) Of(NavigationManager navigation)
    {
        var uri = new Uri(navigation.Uri);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            query[Uri.UnescapeDataString(pair[0])] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
        }

        return (uri.AbsolutePath, query);
    }
}
