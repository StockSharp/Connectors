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

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SecurityCatalogUsesCompactQuotesWithoutLosingFullPriceData()
	{
		var fullQuoteRequests = 0;
		using var client = new TpexRestClient(new("https://tpex.example.test"), new HistoryHttpHandler(request =>
		{
			var path = request.RequestUri.AbsolutePath;
			var data = path switch
			{
				"/openapi/v1/tpex_mainboard_quotes" => """[{"SecuritiesCompanyCode":"00411A","Capitals":"469576000"}]""",
				"/openapi/v1/tpex_mainboard_daily_close_quotes" => """
					[{"SecuritiesCompanyCode":"00411A","Capitals":"469576000","Average":"8.07"},{"SecuritiesCompanyCode":"700001"}]
					""",
				"/openapi/v1/mopsfin_t187ap03_O" => "[]",
				_ => throw new InvalidOperationException($"Unexpected request to '{path}'."),
			};
			if (path == "/openapi/v1/tpex_mainboard_daily_close_quotes")
				fullQuoteRequests++;
			return new(HttpStatusCode.OK) { Content = new StringContent(data) };
		}));

		var cacheTimeout = TimeSpan.FromMinutes(5);
		var catalog = await client.GetSecurityCatalog(TpexMarkets.Mainboard, false, cacheTimeout, CancellationToken);
		AreEqual(0, fullQuoteRequests, "The ordinary catalog requested the full price feed.");
		var fund = catalog.GetAllProfiles(false).Single();
		AreEqual("00411A", fund.Code);
		AreEqual("469576000", fund.IssueSize);

		var prices = await client.GetSnapshot(TpexMarkets.Mainboard, false, cacheTimeout, CancellationToken);
		AreEqual("8.07", prices.MainboardPrices.Single(row => row.Code == fund.Code).Average);

		var withDerivatives = await client.GetSecurityCatalog(TpexMarkets.Mainboard, true, cacheTimeout, CancellationToken);
		IsTrue(withDerivatives.GetAllProfiles(true).Any(profile => profile.Code == "700001"));
		AreEqual(1, fullQuoteRequests);
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
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);

	/// <inheritdoc />
	protected override int CandlesDepth => 7;
}
