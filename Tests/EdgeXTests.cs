namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.EdgeX;
using StockSharp.EdgeX.Native;
using StockSharp.EdgeX.Native.Common;
using StockSharp.Messages;

/// <summary>
/// Tests for the EdgeX connector.
/// </summary>
[TestClass]
[CLSCompliant(false)]
public class EdgeXTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void PollingCanCompleteTheOnlyBarReturnedAtTheStartOfASubscription()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var cursor = StockSharp.EdgeX.Native.Extensions.StartCandleCursor(open);
		cursor = StockSharp.EdgeX.Native.Extensions.AdvanceCandleCursor(cursor, open, CandleStates.Active);

		IsTrue(cursor < open, "The forming first bar must remain eligible for another poll.");
		AreEqual(open, StockSharp.EdgeX.Native.Extensions.AdvanceCandleCursor(cursor, open, CandleStates.Finished));
	}

	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void HistoricalAndStreamingCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var row = new JObject
		{
			["klineTime"] = (long)open.ToUnix(false), ["open"] = "100", ["high"] = "110", ["low"] = "90", ["close"] = "105", ["size"] = "12.5",
		};

		var message = row.ToCandleMessage(TestSecurityId, TimeSpan.FromMinutes(1), 42, open.AddMilliseconds(elapsedMilliseconds));

		AreEqual(expected, message.State);
		AreEqual(open, message.OpenTime);
		AreEqual(105m, message.ClosePrice);
		AreEqual(12.5m, message.TotalVolume);
	}

	[TestMethod]
	public void PollingRevisitsAnOpenMinuteUntilItCloses()
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var previous = open.AddMinutes(-1);
		AreEqual(previous, StockSharp.EdgeX.Native.Extensions.AdvanceCandleCursor(previous, open, CandleStates.Active));
		AreEqual(open, StockSharp.EdgeX.Native.Extensions.AdvanceCandleCursor(previous, open, CandleStates.Finished));
	}

	[TestMethod]
	[DataRow(nameof(EdgeXRestClient.GetMetaDataAsync), "/api/v2/public/meta/getMetaData")]
	[DataRow(nameof(EdgeXRestClient.GetServerTimeAsync), "/api/v2/public/meta/getServerTime")]
	[DataRow(nameof(EdgeXRestClient.GetTickerAsync), "/api/v2/public/quote/getTicker")]
	[DataRow(nameof(EdgeXRestClient.GetDepthAsync), "/api/v2/public/quote/getDepth")]
	[DataRow(nameof(EdgeXRestClient.GetKlineAsync), "/api/v2/public/quote/getKline")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicRequestsUseCurrentApiRoutes(string operation, string expectedPath)
	{
		await using var server = new HistoryHttpServer(
			() => """{"code":"SUCCESS","data":{"ready":true}}""", CancellationToken);
		using var client = new EdgeXRestClient(server.Address.ToString(), null, null, null, null);

		var response = await (operation switch
		{
			nameof(EdgeXRestClient.GetMetaDataAsync) => client.GetMetaDataAsync(CancellationToken),
			nameof(EdgeXRestClient.GetServerTimeAsync) => client.GetServerTimeAsync(CancellationToken),
			nameof(EdgeXRestClient.GetTickerAsync) => client.GetTickerAsync("30000001", CancellationToken),
			nameof(EdgeXRestClient.GetDepthAsync) => client.GetDepthAsync("30000001", 15, CancellationToken),
			_ => client.GetKlineAsync("30000001", "MINUTE_1", "LAST_PRICE", 10, null, null, CancellationToken),
		});

		AreEqual("SUCCESS", response["code"].ToString());
		AreEqual(expectedPath, server.RequestTarget.Split('?')[0]);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new EdgeXMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [EdgeXSections.Derivatives],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDC",
		BoardCode = BoardCodes.EdgeXDerivatives,
	};
}
