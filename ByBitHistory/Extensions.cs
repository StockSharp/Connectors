namespace StockSharp.ByBitHistory;

static class Extensions
{
	public static DateTime FromNative(this long ts)
	{
		// timestamps are in milliseconds since epoch in most CSVs.
		if (ts > 1_000_000_000_000)
			return TimeHelper.FromUnix(ts, false);

		// fallback: seconds
		return TimeHelper.FromUnix(ts, true);
	}

	public static bool? ToUpTick(this string type)
		=> type?.ToLowerInvariant() switch
		{
			"plustick" or "zeroplustick" => true,
			"minustick" or "zerominustick" => false,
			_ => null,
		};
}