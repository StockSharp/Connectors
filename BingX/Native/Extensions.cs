namespace StockSharp.BingX.Native;

using System.IO;
using System.IO.Compression;

using Ecng.IO.Compression;

static class Extensions
{
	public static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			NullValueHandling = NullValueHandling.Ignore,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
		};

	/// <summary>
	/// Time frames as the REST endpoints name them.
	/// </summary>
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	/// <summary>
	/// The streams spell the same frames differently than the REST endpoints do, and the
	/// venue streams fewer of them.
	/// </summary>
	public static readonly PairSet<TimeSpan, string> StreamTimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "60min" },
		{ TimeSpan.FromHours(2), "2hour" },
		{ TimeSpan.FromHours(4), "4hour" },
		{ TimeSpan.FromHours(6), "6hour" },
		{ TimeSpan.FromHours(8), "8hour" },
		{ TimeSpan.FromHours(12), "12hour" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(3), "3day" },
		{ TimeSpan.FromDays(7), "1week" },
	};

	/// <summary>
	/// Name of the time frame in the stream data type (BTC-USDT@kline_1min).
	/// </summary>
	/// <param name="timeFrame"><see cref="TimeSpan"/></param>
	/// <returns>Stream name.</returns>
	public static string ToStream(this TimeSpan timeFrame)
		=> StreamTimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	/// <summary>
	/// The venue gzips every web socket frame.
	/// </summary>
	/// <param name="source">Received bytes.</param>
	/// <param name="destination">Buffer the unpacked bytes are written into.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Unpacked length.</returns>
	public static async ValueTask<int> UnpackFrameAsync(ReadOnlyMemory<byte> source, Memory<byte> destination, CancellationToken cancellationToken)
	{
		// keep the frame as is when it is not gzipped
		if (source.Length < 2 || source.Span[0] != 0x1f || source.Span[1] != 0x8b)
		{
			source.CopyTo(destination);
			return source.Length;
		}

		using var packed = new MemoryStream(source.ToArray(), false);
		using var unpacked = new MemoryStream(destination.Length);

		await packed.UncompressAsync<GZipStream>(unpacked, cancellationToken: cancellationToken);

		var written = (int)unpacked.Length;
		unpacked.GetBuffer().AsSpan(0, written).CopyTo(destination.Span);
		return written;
	}
}
