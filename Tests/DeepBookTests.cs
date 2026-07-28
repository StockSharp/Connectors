namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.DeepBook;
using StockSharp.DeepBook.Native;
using StockSharp.DeepBook.Native.Model;
using StockSharp.DeepBook.Native.Sui;
using StockSharp.Messages;

[TestClass]
public class DeepBookTests : BaseTestClass
{
	private const string _baseCoin =
		"0x2::sui::SUI";
	private const string _quoteCoin =
		"0xdba34672e30cb065b1f93e3ab55318768fd6fef66c15942c9f7cb846e2f900e7" +
		"::usdc::USDC";
	private const string _pool =
		"0xe05dafb5133bcffb8d59f4e12465dc0e9faeaa05e3e342a08fe135800e3e4407";

	[TestMethod]
	public void DefaultsUsePublishedMainnetEndpoints()
	{
		var adapter = new DeepBookMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://deepbook-indexer.mainnet.mystenlabs.com",
			adapter.IndexerEndpoint);
		AreEqual("https://fullnode.mainnet.sui.io:443",
			adapter.GrpcEndpoint);
		AreEqual(DeepBookExtensions.MainnetPackage, adapter.PackageId);
		AreEqual(DeepBookExtensions.Clock, adapter.ClockObjectId);
		AreEqual(100, adapter.OrderBookDepth);
		AreEqual(500, adapter.HistoryLimit);
		AreEqual(0.5m, adapter.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAndLimits()
	{
		var source = new DeepBookMessageAdapter(
			new IncrementalIdGenerator())
		{
			WalletAddress = "0x1234",
			PrivateKey = "secret".Secure(),
			IndexerEndpoint = "https://indexer.example.test",
			GrpcEndpoint = "https://grpc.example.test",
			PackageId = "0x5678",
			ClockObjectId = "0x6",
			Pools = "SUI_USDC;DEEP_USDC",
			OrderBookDepth = 200,
			HistoryLimit = 250,
			SlippageTolerance = 1.25m,
			PollingInterval = TimeSpan.FromSeconds(9),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new DeepBookMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual(source.IndexerEndpoint, target.IndexerEndpoint);
		AreEqual(source.GrpcEndpoint, target.GrpcEndpoint);
		AreEqual(source.PackageId, target.PackageId);
		AreEqual(source.ClockObjectId, target.ClockObjectId);
		AreEqual(source.Pools, target.Pools);
		AreEqual(200, target.OrderBookDepth);
		AreEqual(250, target.HistoryLimit);
		AreEqual(1.25m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(9), target.PollingInterval);
	}

	[TestMethod]
	public void IndexerModelsUseCurrentResponseShape()
	{
		const string json =
			"[{\"pool_id\":\"0xe05dafb5133bcffb8d59f4e12465dc0e9faeaa05e3e342a08fe135800e3e4407\"," +
			"\"pool_name\":\"SUI_USDC\",\"base_asset_id\":\"0x2::sui::SUI\"," +
			"\"base_asset_decimals\":9,\"base_asset_symbol\":\"SUI\"," +
			"\"base_asset_name\":\"Sui\",\"quote_asset_id\":\"0xdba34672e30cb065b1f93e3ab55318768fd6fef66c15942c9f7cb846e2f900e7::usdc::USDC\"," +
			"\"quote_asset_decimals\":6,\"quote_asset_symbol\":\"USDC\"," +
			"\"quote_asset_name\":\"Native USDC Token\",\"min_size\":1000000000," +
			"\"lot_size\":100000000,\"tick_size\":100}]";

		var pools = JsonConvert.DeserializeObject<DeepBookPoolData[]>(json);

		AreEqual(1, pools.Length);
		AreEqual("SUI_USDC", pools[0].PoolName);
		AreEqual(9, pools[0].BaseAssetDecimals);
		AreEqual(100000000UL, pools[0].LotSize);
		AreEqual(100UL, pools[0].TickSize);
	}

	[TestMethod]
	public void SuiUnitsAndIntervalsAreExact()
	{
		AreEqual(
			"0x0000000000000000000000000000000000000000000000000000000000000002",
			"0x2".NormalizeSuiAddress());
		AreEqual(123456789UL, 123.456789m.ToBaseUnits(6));
		AreEqual(123.456789m, 123456789UL.FromBaseUnits(6));
		AreEqual(2UL, 0.00000101m.ToBaseUnitsRoundedUp(6));
		AreEqual("1m", TimeSpan.FromMinutes(1).ToDeepBookInterval());
		AreEqual("1w", TimeSpan.FromDays(7).ToDeepBookInterval());
		Throws<NotSupportedException>(() =>
			TimeSpan.FromMinutes(3).ToDeepBookInterval());
		Throws<ArgumentException>(() => "not-an-address".NormalizeSuiAddress());
	}

	[TestMethod]
	public void DirectSwapBuildsSplitCallAndTransfersEveryReturnCoin()
	{
		var market = CreateMarket();
		var quote = new DeepBookQuote
		{
			Side = Sides.Sell,
			InputAmount = 1_000_000_000,
			OutputAmount = 680_000,
			Price = 0.68m,
			Volume = 1m,
		};
		var coin = new StockSharp.DeepBook.Native.Sui.Object
		{
			ObjectId = "0x123",
			Version = 7,
			Digest = new string('1', 32),
			ObjectType = "0x2::coin::Coin<0x2::sui::SUI>",
			Balance = 2_000_000_000,
		};

		var transaction = DeepBookTransactionBuilder.BuildSwap(
			"0x456", DeepBookExtensions.MainnetPackage, market, quote,
			quote.InputAmount, 670_000, [coin],
			new()
			{
				ObjectId = _pool,
				InitialVersion = 11,
				IsMutable = true,
			},
			new()
			{
				ObjectId = "0x6",
				InitialVersion = 1,
				IsMutable = false,
			});
		var commands = transaction.Kind.ProgrammableTransaction.Commands;

		AreEqual(4, commands.Count);
		IsTrue(commands[0].SplitCoins is not null);
		AreEqual("zero", commands[1].MoveCall.Function);
		AreEqual("swap_exact_base_for_quote",
			commands[2].MoveCall.Function);
		AreEqual(3, commands[3].TransferObjects.Objects.Count);
		IsTrue(commands[2].MoveCall.Arguments[1].HasSubresult);
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveReadOnlyEndpointsReturnCurrentDeepBookData()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		using var indexer = new DeepBookApiClient(
			"https://deepbook-indexer.mainnet.mystenlabs.com");
		using var sui = new DeepBookSuiClient(
			"https://fullnode.mainnet.sui.io:443", null, null);

		var status = await indexer.GetStatusAsync(CancellationToken);
		var markets = await indexer.GetMarketsAsync(CancellationToken);
		var market = markets.Single(item => item.PoolName == "SUI_USDC");
		var book = await indexer.GetOrderBookAsync(market, 10,
			CancellationToken);
		var trades = await indexer.GetTradesAsync(market,
			DateTime.UtcNow - TimeSpan.FromHours(1), DateTime.UtcNow, 10,
			CancellationToken);
		var candles = await indexer.GetCandlesAsync(market,
			TimeSpan.FromHours(1), null, null, 3, CancellationToken);
		var service = await sui.GetServiceInfoAsync(CancellationToken);
		var package = await sui.GetObjectAsync(
			DeepBookExtensions.MainnetPackage, CancellationToken);
		var pool = await sui.GetSharedObjectAsync(market.PoolId, true,
			CancellationToken);

		IsGreater(status.LatestCheckpoint, 0UL);
		IsGreater(markets.Length, 10);
		IsGreater(book.Bids.Length, 0);
		IsGreater(book.Asks.Length, 0);
		IsGreater(trades.Length, 0);
		IsGreater(candles.Length, 0);
		AreEqual("mainnet", service.Chain);
		AreEqual("package", package.ObjectType);
		AreEqual(market.PoolId, pool.ObjectId);
		IsGreater(pool.InitialVersion, 0UL);
	}

	private static DeepBookMarket CreateMarket()
		=> new()
		{
			PoolId = _pool.NormalizeSuiAddress(),
			PoolName = "SUI_USDC",
			SecurityCode = "SUI-USDC",
			BaseToken = new()
			{
				CoinType = _baseCoin.NormalizeCoinType(),
				Symbol = "SUI",
				Name = "Sui",
				Decimals = 9,
			},
			QuoteToken = new()
			{
				CoinType = _quoteCoin.NormalizeCoinType(),
				Symbol = "USDC",
				Name = "USD Coin",
				Decimals = 6,
			},
			MinSize = 1m,
			LotSize = 0.1m,
			TickSize = 0.0001m,
		};
}
