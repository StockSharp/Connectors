namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Gopax;
using StockSharp.Messages;

/// <summary>
/// Gopax publishes market data without credentials.
/// </summary>
[TestClass]
public class GopaxMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void DefaultsToDocumentedRestDomain()
	{
		using var adapter = new GopaxMessageAdapter(new IncrementalIdGenerator());

		AreEqual("gopax.co.kr", adapter.Address);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TradePollingUsesUnixSecondsAfterHistory()
	{
		HistoryHttpServer api = null;
		await using var server = api = new HistoryHttpServer(() => api.RequestTimes.Count == 1
			? """[{"id":42,"time":"2024-03-09T16:00:00.125Z","price":1200,"amount":2,"side":"buy"}]"""
			: "[]", CancellationToken);
		var adapter = new GopaxMessageAdapter(new IncrementalIdGenerator(), server.Address)
		{
			HeartbeatInterval = TimeSpan.Zero,
		};
		adapter.RemoveTransactionalSupport();
		await using var harness = new MarketDataTestHarness(adapter);
		await harness.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken);

		var tradeTime = DateTimeOffset.FromUnixTimeSeconds(1_710_000_000).UtcDateTime;
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			IsSubscribe = true,
			TransactionId = adapter.TransactionIdGenerator.GetNextId(),
			DataType2 = DataType.Ticks,
			SecurityId = TestSecurityId,
			From = tradeTime.AddSeconds(-1),
			To = tradeTime.AddSeconds(1),
		}, CancellationToken);
		AreEqual("/trading-pairs/USDT-KRW/trades?limit=100&after=1709999999", server.RequestTarget);

		await adapter.SendInMessageAsync(new TimeMessage(), CancellationToken);

		AreEqual(2, server.RequestTimes.Count);
		AreEqual("/trading-pairs/USDT-KRW/trades?limit=100&after=1710000000", server.RequestTarget);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GopaxMessageAdapter(new IncrementalIdGenerator());

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
