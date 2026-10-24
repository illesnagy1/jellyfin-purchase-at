using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PurchaseAt.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        MusicBrainzServer = "https://musicbrainz.org";
        CacheDays = 30;
        IncludeOtherReleases = true;
        LinkName = "Buy (mail-order)";
    }

    /// <summary>
    /// Gets or sets the base url of the MusicBrainz server to query.
    /// </summary>
    public string MusicBrainzServer { get; set; }

    /// <summary>
    /// Gets or sets the number of days a looked up purchase link is cached before it is looked up again.
    /// </summary>
    public int CacheDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether links from other releases in the same release group
    /// are used when the album's own release has none.
    /// </summary>
    public bool IncludeOtherReleases { get; set; }

    /// <summary>
    /// Gets or sets the name shown on the link.
    /// </summary>
    public string LinkName { get; set; }
}
