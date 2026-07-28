namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using TonSdk.Core;
using TonSdk.Core.Boc;

using StockSharp.Messages;
using StockSharp.StonFi;
using StockSharp.StonFi.Native;
using StockSharp.StonFi.Native.Model;

[TestClass]
public class StonFiTests : BaseTestClass
{
	private const string _poolAddress =
		"EQCGScrZe1xbyWqWDvdI6mzP-GAcAWFv6ZXuaJOuSqemxku4";
	private const string _ownerAddress =
		"UQAQnxLqlX2B6w4jQzzzPWA8eyWZVZBz6Y0D_8noARLOaEAn";
	private const string _askWalletAddress =
		"kQB_TOJSB7q3-Jm1O8s0jKFtqLElZDPjATs5uJGsujcjznq3";

	[TestMethod]
	public void DefaultsUsePublishedProductionEndpoints()
	{
		var adapter = new StonFiMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.ston.fi", adapter.ApiEndpoint);
		AreEqual("https://toncenter.com/api/v2",
			adapter.TonCenterEndpoint);
		AreEqual(100, adapter.PoolLimit);
		AreEqual(1m, adapter.ProbeVolume);
		AreEqual(1m, adapter.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(3), adapter.PollingInterval);
		AreEqual(30_000, adapter.HistoryBlockLimit);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PrivatePollingInterval);
		AreEqual(TimeSpan.FromMinutes(15),
			adapter.TransactionTimeout);
		AreEqual(7, StonFiMessageAdapter.AllTimeFrames.Count());
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsWalletAndLimits()
	{
		var source = new StonFiMessageAdapter(
			new IncrementalIdGenerator())
		{
			ApiEndpoint = "https://api.example.test/",
			TonCenterEndpoint = "https://ton.example.test/",
			TonCenterApiKey = "api-key".Secure(),
			WalletAddress = _ownerAddress,
			Mnemonic = "one two three".Secure(),
			WalletSubwalletId = 42,
			WalletRevision = 1,
			Pools = _poolAddress,
			PoolLimit = 17,
			ProbeVolume = 2.5m,
			SlippageTolerance = 0.75m,
			PollingInterval = TimeSpan.FromSeconds(7),
			HistoryBlockLimit = 15_000,
			PrivatePollingInterval = TimeSpan.FromSeconds(11),
			TransactionTimeout = TimeSpan.FromMinutes(20),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new StonFiMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("https://api.example.test", target.ApiEndpoint);
		AreEqual("https://ton.example.test", target.TonCenterEndpoint);
		AreEqual("api-key", target.TonCenterApiKey.UnSecure());
		IsTrue(target.WalletAddress.SameTonAddress(_ownerAddress));
		AreEqual("one two three", target.Mnemonic.UnSecure());
		AreEqual(42u, target.WalletSubwalletId);
		AreEqual(1, target.WalletRevision);
		AreEqual(_poolAddress, target.Pools);
		AreEqual(17, target.PoolLimit);
		AreEqual(2.5m, target.ProbeVolume);
		AreEqual(0.75m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(7), target.PollingInterval);
		AreEqual(15_000, target.HistoryBlockLimit);
		AreEqual(TimeSpan.FromSeconds(11),
			target.PrivatePollingInterval);
		AreEqual(TimeSpan.FromMinutes(20),
			target.TransactionTimeout);
	}

	[TestMethod]
	public void SwapEventsPreserveSideAndAggregateIntoCandles()
	{
		var start = new DateTimeOffset(2026, 7, 28, 10, 1, 0,
			TimeSpan.Zero);
		var sell = new StonEvent
		{
			Block = new()
			{
				Number = 100,
				Timestamp = start.ToUnixTimeSeconds(),
			},
			EventType = "swap",
			TransactionId = "tx1",
			EventIndex = 1,
			PoolAddress = _poolAddress,
			Amount0In = "2",
			Amount1Out = "6",
		}.ToTrade();
		var buy = new StonEvent
		{
			Block = new()
			{
				Number = 101,
				Timestamp = start.AddMinutes(2).ToUnixTimeSeconds(),
			},
			EventType = "swap",
			TransactionId = "tx2",
			EventIndex = 2,
			PoolAddress = _poolAddress,
			Amount0Out = "4",
			Amount1In = "16",
		}.ToTrade();

		AreEqual(Sides.Sell, sell.Side);
		AreEqual(3m, sell.Price);
		AreEqual(Sides.Buy, buy.Side);
		AreEqual(4m, buy.Price);

		var candle = StonFiExtensions.AggregateTrades(
			[sell, buy], TimeSpan.FromMinutes(5)).Single();
		AreEqual(3m, candle.Open);
		AreEqual(4m, candle.High);
		AreEqual(3m, candle.Low);
		AreEqual(4m, candle.Close);
		AreEqual(6m, candle.Volume);
		AreEqual(22m, candle.Turnover);
		AreEqual(2, candle.TradeCount);
	}

	[TestMethod]
	public void RouterV2SwapBodyMatchesOfficialSdkFixture()
	{
		var askWallet = new Address(_askWalletAddress);
		var mainnetAskWallet = askWallet.ToString(AddressType.Base64,
			new AddressStringifyOptions(true, false, true,
				askWallet.GetWorkchain()));
		var body = StonTonClient.CreateSwapBodyV2(
			mainnetAskWallet, _ownerAddress,
			new BigInteger(900_000_000), 900);
		var expected = Cell.From(
			"te6cckEBAgEAoAAB4WZk3iqAD+mcSkD3Vv8TNqd5ZpGULbUWJKyGfGAnZzcSNZdG5HnQAEJ8S6pV9gesOI0M88z1gPHslmVWQc+mNA//J6AESzmiAAhPiXVKvsD1hxGhnnmesB49ksyqyDn0xoH/5PQAiWc0AAAAAAAAAcJAAQBTQ1pOkAgAIT4l1Sr7A9YcRoZ55nrAePZLMqsg59MaB/+T0AIlnNAAAAUQsBQ24Q==");

		AreEqual(expected.Hash.ToString("hex"),
			body.Hash.ToString("hex"));
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveApisReturnPoolAssetsEventsQuoteAndTonState()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");

		var adapter = new StonFiMessageAdapter(
			new IncrementalIdGenerator());
		using var rest = new StonFiRestClient(adapter.ApiEndpoint);
		var pool = (await rest.GetPoolsAsync(1, _poolAddress,
			CancellationToken)).Single();
		var assets = await rest.GetAssetsAsync(
			[pool.Token0Address, pool.Token1Address], CancellationToken);
		var offer = assets.Single(asset =>
			asset.Address.SameTonAddress(pool.Token0Address));
		var latest = await rest.GetLatestBlockAsync(CancellationToken);
		var fromBlock = Math.Max(0, latest.Block.Number - 1000);
		var events = await rest.GetEventsAsync(fromBlock,
			latest.Block.Number, CancellationToken);
		var quote = await rest.SimulateSwapAsync(
			pool.Token0Address, pool.Token1Address,
			BigInteger.Pow(10, offer.GetDecimals()), 0.01m,
			pool.Address, false, CancellationToken);

		using var ton = new StonTonClient(
			adapter.TonCenterEndpoint, null, null, null,
			adapter.WalletSubwalletId, adapter.WalletRevision);
		await ton.VerifyAsync(CancellationToken);

		IsTrue(pool.Address.SameTonAddress(_poolAddress));
		AreEqual(2, assets.Length);
		IsGreater(latest.Block.Number, 0);
		IsGreater(events.Length, 0);
		IsGreater(quote.AskUnits.ParseInteger(), BigInteger.Zero);
		IsTrue(quote.PoolAddress.SameTonAddress(pool.Address));
	}
}
