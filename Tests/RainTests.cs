namespace StockSharp.Connectors.Tests;

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Rain;
using StockSharp.Rain.Native;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Rain.</summary>
[TestClass]
public class RainTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandlePaginationStartsAtTheNewerBound()
	{
		Uri requestUri = null;
		using var client = new RainRestClient("https://public.example.test/", null, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			requestUri = request.RequestUri;
			return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
		})), "_http");

		var older = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
		var newer = older.AddDays(7);
		await client.GetCandlesAsync("BTC-USD", TimeSpan.FromDays(1), older, newer, 10, CancellationToken);

		// Rain paginates backwards: from is the newer cursor, to is the older bound.
		AreEqual("?from=" + Uri.EscapeDataString(newer.ToString("O", CultureInfo.InvariantCulture)) +
			"&to=" + Uri.EscapeDataString(older.ToString("O", CultureInfo.InvariantCulture)) + "&count=10", requestUri.Query);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new RainMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.Rain,
	};
}
