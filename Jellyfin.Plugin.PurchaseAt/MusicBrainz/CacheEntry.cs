using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.PurchaseAt.MusicBrainz;

/// <summary>
/// A cached lookup result.
/// </summary>
/// <param name="Urls">The purchase links.</param>
/// <param name="Fetched">When the links were looked up.</param>
public sealed record CacheEntry(IReadOnlyList<string> Urls, DateTimeOffset Fetched);
