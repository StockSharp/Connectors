namespace StockSharp.Connectors.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Gopax;
using StockSharp.Messages;

/// <summary>
/// Gopax publishes market data without credentials.
/// </summary>
[TestClass]
public class GopaxMarketDataTests : LiveMarketDataTestBase
{
	private const string _domain = "gopax.co.kr";

	private protected override async ValueTask<string> GetVenueUnavailableReasonAsync(Exception failure)
	{
		if (!(failure is AssertFailedException && failure.Message.Contains("No securities received.", StringComparison.Ordinal)) &&
			!(failure is RestSharpException && failure.Message.Contains("\"errorCode\":10059", StringComparison.Ordinal)))
			return null;

		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
		using var response = await client.GetAsync($"https://api.{_domain}/trading-pairs", CancellationToken);
		if (!response.IsSuccessStatusCode)
			return null;

		var json = await response.Content.ReadAsStringAsync(CancellationToken);
		return JToken.Parse(json) is JArray { Count: 0 }
			? "Gopax's public trading-pair catalog is empty."
			: null;
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GopaxMessageAdapter(new IncrementalIdGenerator())
	{
		Address = _domain,
	};

	/// <summary>
	/// Gopax trades thinly, BTC/KRW goes hours without a single minute candle,
	/// while the stablecoin pair prints one every few minutes.
	/// </summary>
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT/KRW",
		BoardCode = BoardCodes.Gopax,
	};
}
