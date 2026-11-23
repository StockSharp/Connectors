namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Tpex;
using StockSharp.Tpex.Native;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Tpex.</summary>
[TestClass]
public class TpexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SnapshotRetriesAnOriginTlsFailure()
	{
		var quoteRequests = 0;
		using var client = new TpexRestClient(new("https://tpex.example.test"), new HistoryHttpHandler(request =>
		{
			if (request.RequestUri.AbsolutePath == "/openapi/v1/tpex_mainboard_daily_close_quotes")
			{
				return ++quoteRequests == 1
					? new((HttpStatusCode)525) { Content = new StringContent("error code: 525") }
					: new(HttpStatusCode.OK) { Content = new StringContent("""[{"SecuritiesCompanyCode":"6488","Close":"123"}]""") };
			}

			AreEqual("/openapi/v1/mopsfin_t187ap03_O", request.RequestUri.AbsolutePath);
			return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
		}));

		var snapshot = await client.GetSnapshot(TpexMarkets.Mainboard, false, TimeSpan.Zero, CancellationToken);

		AreEqual(2, quoteRequests);
		var quote = snapshot.MainboardPrices.Single();
		AreEqual("6488", quote.Code);
		AreEqual("123", quote.Close);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new TpexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "6488",
		Native = "6488",
		BoardCode = "TPEX",
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 7;
}
