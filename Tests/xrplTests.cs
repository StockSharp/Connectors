namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Xrpl;
using StockSharp.Xrpl.Native;

[TestClass]
public class XrplTests : BaseTestClass
{
	private const string _rlusdIssuer =
		"rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De";
	private const string _rlusdWire =
		"524C555344000000000000000000000000000000";
	private const string _market = "XRP/RLUSD:" + _rlusdIssuer;

	[TestMethod]
	public void DefaultsUsePublishedMainnetEndpoints()
	{
		var adapter = new XrplMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://xrplcluster.com/", adapter.RpcEndpoint);
		AreEqual("wss://xrplcluster.com/",
			adapter.StreamingEndpoint);
		AreEqual(_market, adapter.Markets);
		AreEqual(50, adapter.OrderBookDepth);
		AreEqual(10_000, adapter.HistoryLedgerLimit);
		AreEqual(1.2m, adapter.FeeMultiplier);
		AreEqual(20, adapter.LastLedgerOffset);
		AreEqual(5m, adapter.MarketOrderProtection);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PollingInterval);
		AreEqual(7, XrplMessageAdapter.AllTimeFrames.Count());
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAccountAndLimits()
	{
		var account = "rLUEXYuLiQptky37CqLcm9USQpPiz5rkpD";
		var source = new XrplMessageAdapter(
			new IncrementalIdGenerator())
		{
			RpcEndpoint = "https://rpc.example.test/",
			StreamingEndpoint = "wss://stream.example.test/",
			Account = account,
			Seed = "sEdSKaCy2JT7JaM7v95H9SxkhP9wS2r".Secure(),
			Markets = _market,
			DomainId = new string('A', 64),
			OrderBookDepth = 77,
			HistoryLedgerLimit = 1234,
			FeeMultiplier = 2.5m,
			LastLedgerOffset = 31,
			MarketOrderProtection = 7m,
			PollingInterval = TimeSpan.FromSeconds(9),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new XrplMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("https://rpc.example.test/",
			target.RpcEndpoint);
		AreEqual("wss://stream.example.test/",
			target.StreamingEndpoint);
		AreEqual(account, target.Account);
		AreEqual(source.Seed.UnSecure(), target.Seed.UnSecure());
		AreEqual(source.Markets, target.Markets);
		AreEqual(source.DomainId, target.DomainId);
		AreEqual(77, target.OrderBookDepth);
		AreEqual(1234, target.HistoryLedgerLimit);
		AreEqual(2.5m, target.FeeMultiplier);
		AreEqual(31, target.LastLedgerOffset);
		AreEqual(7m, target.MarketOrderProtection);
		AreEqual(TimeSpan.FromSeconds(9),
			target.PollingInterval);
	}

	[TestMethod]
	public void MarketsAndCurrencyCodesPreserveIssuerIdentity()
	{
		var markets = XrplExtensions.ParseMarkets(
			_market + ";USD:" + _rlusdIssuer + "/XRP", null);

		AreEqual(2, markets.Length);
		AreEqual("XRP/RLUSD", markets[0].SecurityCode);
		AreEqual(_rlusdWire,
			markets[0].Quote.CurrencyCode);
		AreEqual("RLUSD", markets[0].Quote.Symbol);
		AreEqual(_rlusdIssuer, markets[0].Quote.Issuer);
		AreEqual("USD", markets[1].Base.CurrencyCode);
		AreEqual(_rlusdIssuer + "/" + _rlusdWire,
			markets[0].Quote.BookChangeId);
	}

	[TestMethod]
	public void BookAndLedgerChangesUseXrpDropsWithoutPrecisionLoss()
	{
		var market = XrplExtensions.ParseMarkets(_market, null).Single();
		var asks = JObject.Parse(
			"""
			{
			  "ledger_index": 100,
			  "offers": [{
			    "index": "A",
			    "TakerGets": "2000000",
			    "TakerPays": {
			      "currency": "524C555344000000000000000000000000000000",
			      "issuer": "rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De",
			      "value": "4"
			    }
			  }]
			}
			""");
		var bids = JObject.Parse(
			"""
			{
			  "ledger_index": 100,
			  "offers": [{
			    "index": "B",
			    "TakerGets": {
			      "currency": "524C555344000000000000000000000000000000",
			      "issuer": "rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De",
			      "value": "3"
			    },
			    "TakerPays": "2000000"
			  }]
			}
			""");
		var book = XrplExtensions.ParseBook(market, asks, bids, 20,
			new DateTime(2026, 7, 28, 10, 0, 0,
				DateTimeKind.Utc));
		var bar = XrplExtensions.ParseBookChange(market,
			JObject.Parse(
			"""
			{
			  "currency_a": "XRP_drops",
			  "currency_b": "rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De/524C555344000000000000000000000000000000",
			  "open": "500000",
			  "high": "500000",
			  "low": "500000",
			  "close": "500000",
			  "volume_a": "10000000",
			  "volume_b": "20"
			}
			"""), 200, 800_000_000);

		AreEqual(2m, book.Asks.Single().Price);
		AreEqual(2m, book.Asks.Single().Volume);
		AreEqual(1.5m, book.Bids.Single().Price);
		AreEqual(2m, book.Bids.Single().Volume);
		AreEqual(2m, bar.Close);
		AreEqual(10m, bar.Volume);
		AreEqual(20m, bar.Turnover);
	}

	[TestMethod]
	public void LedgerBarsAggregateIntoTimeFrameCandles()
	{
		var start = new DateTime(2026, 7, 28, 10, 1, 0,
			DateTimeKind.Utc);
		var candle = XrplExtensions.AggregateBars(
		[
			new()
			{
				Time = start,
				Open = 2,
				High = 3,
				Low = 1,
				Close = 2.5m,
				Volume = 10,
				Turnover = 20,
			},
			new()
			{
				Time = start.AddMinutes(2),
				Open = 2.5m,
				High = 4,
				Low = 2,
				Close = 3,
				Volume = 5,
				Turnover = 15,
			},
		], TimeSpan.FromMinutes(5)).Single();

		AreEqual(new DateTime(2026, 7, 28, 10, 0, 0,
			DateTimeKind.Utc), candle.OpenTime);
		AreEqual(2m, candle.Open);
		AreEqual(4m, candle.High);
		AreEqual(1m, candle.Low);
		AreEqual(3m, candle.Close);
		AreEqual(15m, candle.Volume);
		AreEqual(35m, candle.Turnover);
		AreEqual(2, candle.LedgerCount);
	}

	[TestMethod]
	public void OfferSigningUsesCanonicalXrplSerialization()
	{
		using var signer = new XrplSigner(null,
			"sEdSKaCy2JT7JaM7v95H9SxkhP9wS2r".Secure());
		var market = XrplExtensions.ParseMarkets(_market, null).Single();
		var signed = signer.SignOffer(market, Sides.Sell, 2m, 3m,
			OrderTypes.Limit, TimeInForce.PutInQueue, false, null,
			10, 1000, 12);

		AreEqual("rLUEXYuLiQptky37CqLcm9USQpPiz5rkpD",
			signer.WalletAddress);
		IsTrue(signed.Blob.Length > 100);
		AreEqual(64, signed.Hash.Length);
		AreEqual(10u, signed.Sequence);
		IsTrue(signed.Blob.All(Uri.IsHexDigit));
		IsTrue(signed.Hash.All(Uri.IsHexDigit));
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveMainnetReturnsLedgerBookChangesAndStream()
	{
		if (!Environment.GetEnvironmentVariable(
			"STOCKSHARP_LIVE_TESTS").EqualsIgnoreCase("1"))
			Inconclusive(
				"Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		var adapter = new XrplMessageAdapter(
			new IncrementalIdGenerator());
		var market = XrplExtensions.ParseMarkets(
			adapter.Markets, null).Single();
		using var rpc = new XrplRpcClient(adapter.RpcEndpoint);
		await rpc.VerifyAsync(CancellationToken);
		var ledger = await rpc.GetLedgerAsync(null,
			CancellationToken);
		var book = await rpc.GetBookAsync(market, 20,
			CancellationToken);
		var changes = await rpc.GetBookChangesAsync(ledger.Index,
			CancellationToken);

		var completion = new TaskCompletionSource<JObject>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using var socket = new XrplSocketClient(
			adapter.StreamingEndpoint, null, message =>
			{
				if (message.Value<string>("type")
					.EqualsIgnoreCase("ledgerClosed"))
					completion.TrySetResult(message);
				return ValueTask.CompletedTask;
			}, error =>
			{
				completion.TrySetException(error);
				return ValueTask.CompletedTask;
			});
		await socket.ConnectAsync(CancellationToken);
		var streamLedger = await completion.Task.WaitAsync(
			TimeSpan.FromSeconds(20), CancellationToken);

		IsGreater(ledger.Index, 0u);
		IsGreater(book.Bids.Length, 0);
		IsGreater(book.Asks.Length, 0);
		AreEqual(ledger.Index,
			changes.Value<uint>("ledger_index"));
		IsGreater(streamLedger.Value<uint>("ledger_index"), 0u);
		IsTrue(socket.IsConnected);
	}
}
