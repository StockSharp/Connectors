namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Chainflip;
using StockSharp.Chainflip.Native;
using StockSharp.Chainflip.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the Chainflip connector.
/// </summary>
[TestClass]
public class ChainflipTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void DefaultsUsePublishedEndpoints()
	{
		var adapter = new ChainflipMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://rpc.mainnet.chainflip.io",
			adapter.StateRpcEndpoint);
		AreEqual("https://chainflip-swap.chainflip.io",
			adapter.BackendEndpoint);
		AreEqual("https://ethereum-rpc.publicnode.com",
			adapter.EthereumRpcEndpoint);
		AreEqual("https://arbitrum-one-rpc.publicnode.com",
			adapter.ArbitrumRpcEndpoint);
		AreEqual(0.1m, adapter.ProbeVolume);
		AreEqual(100, adapter.OrderBookDepth);
		AreEqual(TimeSpan.FromSeconds(6), adapter.PollingInterval);
		AreEqual(100, adapter.MaxBlocksPerPoll);
		AreEqual(50, adapter.InitialTickBlocks);
		AreEqual(1.25m, adapter.SlippageTolerance);
		AreEqual(300, adapter.RetryDurationBlocks);
		IsTrue(adapter.IsAutoApprove);
	}

	[TestMethod]
	public async Task SettingsRoundTripKeepsEndpointsAddressesAndLimits()
	{
		var source = new ChainflipMessageAdapter(
			new IncrementalIdGenerator())
		{
			StateRpcEndpoint = "https://state.example.test/",
			BackendEndpoint = "https://backend.example.test/",
			EthereumRpcEndpoint = "https://eth.example.test/",
			ArbitrumRpcEndpoint = "https://arb.example.test/",
			WalletAddress =
				"0x1111111111111111111111111111111111111111",
			PrivateKey = "secret".Secure(),
			BitcoinAddress = "bc1qexample",
			SolanaAddress = "solana-example",
			AssethubAddress = "asset-hub-example",
			PolkadotAddress = "polkadot-example",
			TronAddress = "tron-example",
			Pools = "ETH@ETHEREUM-USDC@ETHEREUM",
			ProbeVolume = 2m,
			OrderBookDepth = 250,
			PollingInterval = TimeSpan.FromSeconds(9),
			MaxBlocksPerPoll = 75,
			InitialTickBlocks = 25,
			SlippageTolerance = 2.5m,
			RetryDurationBlocks = 450,
			ReceiptTimeout = TimeSpan.FromMinutes(8),
			IsAutoApprove = false,
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		var target = new ChainflipMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual("https://state.example.test", target.StateRpcEndpoint);
		AreEqual("https://backend.example.test", target.BackendEndpoint);
		AreEqual("https://eth.example.test", target.EthereumRpcEndpoint);
		AreEqual("https://arb.example.test", target.ArbitrumRpcEndpoint);
		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual(source.BitcoinAddress, target.BitcoinAddress);
		AreEqual(source.SolanaAddress, target.SolanaAddress);
		AreEqual(source.AssethubAddress, target.AssethubAddress);
		AreEqual(source.PolkadotAddress, target.PolkadotAddress);
		AreEqual(source.TronAddress, target.TronAddress);
		AreEqual(source.Pools, target.Pools);
		AreEqual(2m, target.ProbeVolume);
		AreEqual(250, target.OrderBookDepth);
		AreEqual(TimeSpan.FromSeconds(9), target.PollingInterval);
		AreEqual(75, target.MaxBlocksPerPoll);
		AreEqual(25, target.InitialTickBlocks);
		AreEqual(2.5m, target.SlippageTolerance);
		AreEqual(450, target.RetryDurationBlocks);
		AreEqual(TimeSpan.FromMinutes(8), target.ReceiptTimeout);
		IsFalse(target.IsAutoApprove);
	}

	[TestMethod]
	public void Q96AndX128PricesPreserveAssetUnits()
	{
		var oneQ96 = BigInteger.Pow(2, 96).ToRpcHex();
		AreEqual(1m, ChainflipExtensions.DecodeSqrtPrice(
			oneQ96, 6, 6));
		AreEqual(1_000_000_000_000m,
			ChainflipExtensions.DecodeSqrtPrice(oneQ96, 18, 6));

		var source = Asset("Ethereum", "USDC");
		var destination = Asset("Arbitrum", "USDC");
		var expected = BigInteger.Pow(2, 128) * 198 / 100;
		AreEqual(expected,
			ChainflipExtensions.GetMinimumPriceX128("2", source,
				destination, 1m));
	}

	[TestMethod]
	public void QuotesAndVaultTransactionsAreStrictlyValidated()
	{
		var source = Asset("Ethereum", "ETH");
		var destination = Asset("Bitcoin", "BTC");
		var amount = BigInteger.Pow(10, 17);
		var quote = new ChainflipQuote
		{
			Type = "REGULAR",
			SourceAsset = source.ToRpc(),
			DestinationAsset = destination.ToRpc(),
			DepositAmount = amount.ToString(),
			EgressAmount = "300000",
			EstimatedPrice = "0.03",
			IsVaultSwap = true,
		};
		ChainflipHttpClient.ValidateQuote(quote, source, destination,
			amount, true);
		ChainflipHttpClient.ValidateVaultResponse(new()
		{
			Chain = "Ethereum",
			To = "0xf5e10380213880111522dd0efd3dbb45b9f62bcc",
			Calldata = "0x1234",
			Value = amount.ToString(),
		}, source, amount);
	}

	[TestMethod]
	public void SwapPriceUsesBaseVolumeForBothSides()
	{
		var market = new ChainflipMarket
		{
			BaseAsset = Asset("Bitcoin", "BTC"),
			QuoteAsset = Asset("Ethereum", "USDC"),
			SecurityCode = "BTC@BITCOIN-USDC@ETHEREUM",
		};
		AreEqual(60_000m, ChainflipMessageAdapter.GetSwapPrice(
			market, Sides.Sell, new BigInteger(100_000_000),
			new BigInteger(60_000_000_000)));
		AreEqual(60_000m, ChainflipMessageAdapter.GetSwapPrice(
			market, Sides.Buy, new BigInteger(60_000_000_000),
			new BigInteger(100_000_000)));
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveReadOnlyApisReturnPoolsBookFillsQuoteAndVaultData()
	{
		if (!Env("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		using var state = new ChainflipStateClient(
			"https://rpc.mainnet.chainflip.io");
		using var api = new ChainflipHttpClient(
			"https://chainflip-swap.chainflip.io");

		var markets = await state.VerifyAndGetMarketsAsync(
			CancellationToken);
		var market = markets.Single(item =>
			item.SecurityCode ==
				"USDT@ETHEREUM-USDC@ETHEREUM");
		var prices = await state.GetPricesAsync(market, null,
			CancellationToken);
		var book = await state.GetOrderBookAsync(market, 20, null,
			CancellationToken);
		var best = await state.GetBestBlockNumberAsync(CancellationToken);
		var byKey = markets.ToDictionary(static item => item.Key,
			StringComparer.OrdinalIgnoreCase);
		var fills = await state.GetBlockTradesAsync(best, byKey,
			CancellationToken);
		var source = Asset("Ethereum", "ETH");
		var destination = Asset("Bitcoin", "BTC");
		var amount = BigInteger.Pow(10, 17);
		var quote = await api.GetQuoteAsync(source, destination, amount,
			true, CancellationToken);
		var vault = await api.BuildVaultSwapAsync(quote, source,
			destination, ChainflipExtensions.ProbeAddress,
			"bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq",
			1.25m, 300, CancellationToken);

		IsGreater(markets.Length, 10);
		IsGreater(prices.Bid, 0m);
		IsGreater(prices.Ask, prices.Bid);
		IsGreater(book.Bids.Length, 0);
		IsGreater(book.Asks.Length, 0);
		AreEqual(best, fills.BlockNumber);
		IsGreater(quote.EgressAmount.ParseInteger(), BigInteger.Zero);
		AreEqual("Ethereum", vault.Chain);
		IsTrue(vault.Calldata.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase));
	}

	private static ChainflipAsset Asset(string chain, string symbol)
		=> ChainflipExtensions.Assets.Single(asset =>
			asset.Chain.EqualsIgnoreCase(chain) &&
			asset.Symbol.EqualsIgnoreCase(symbol));

	private const string _rawPoolPrice = """{"sell":"0x10020a0f99e5bd2a84731bd51","buy":"0x1000000000000000000000000"}""";
	private const string _executableBook = """{"bids":[{"amount":"0x39a849f25","sqrt_price":"0xffd3f4b1ae93dab91b1663d3"}],"asks":[{"amount":"0x8591502e","sqrt_price":"0x100319d4a7fad6ea3dbd4fc96"}]}""";

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task Level1UsesExecutableOrderBookPricesAtTheRequestedBlock()
	{
		// USDT/USDC at mainnet block 15331072 has crossed indicative pool prices.
		const string blockHash = "0x1111111111111111111111111111111111111111111111111111111111111111";
		JObject request = null;
		using var client = CreateClient(_executableBook, value => request = value);
		var prices = await client.GetPricesAsync(Market(), blockHash, CancellationToken);

		AreEqual(ChainflipExtensions.DecodeSqrtPrice("0xffd3f4b1ae93dab91b1663d3", 6, 6), prices.Bid);
		AreEqual(ChainflipExtensions.DecodeSqrtPrice("0x100319d4a7fad6ea3dbd4fc96", 6, 6), prices.Ask);
		IsTrue(prices.Bid < prices.Ask);
		AreEqual("cf_pool_orderbook", request["method"].Value<string>());
		AreEqual(blockHash, request["params"]["at"].Value<string>());
		AreEqual(1, request["params"]["orders"].Value<int>());
	}

	[TestMethod]
	[DataRow("""{"bids":[],"asks":[]}""")]
	[DataRow("""{"bids":[{"amount":"0x0","sqrt_price":"0x0"}],"asks":[]}""")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EmptyExecutableBooksStillFail(string book)
	{
		using var client = CreateClient(book, _ => { });
		await ThrowsAsync<InvalidDataException>(async () => await client.GetPricesAsync(Market(), null, CancellationToken));
	}

	private static ChainflipMarket Market() => new()
	{
		BaseAsset = new() { Chain = "Ethereum", Symbol = "USDT", Decimals = 6 },
		QuoteAsset = new() { Chain = "Ethereum", Symbol = "USDC", Decimals = 6 },
		SecurityCode = "USDT@ETHEREUM-USDC@ETHEREUM",
	};

	private ChainflipStateClient CreateClient(string book, Action<JObject> capture)
	{
		var client = new ChainflipStateClient("https://rpc.example.test");
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var payload = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			capture(payload);
			var result = payload["method"].Value<string>() == "cf_pool_orderbook" ? book : _rawPoolPrice;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{result}}}"),
			};
		})), "_http");
		return client;
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new ChainflipMessageAdapter(new IncrementalIdGenerator())
		{
			Pools = "BTC@BITCOIN-USDC@ETHEREUM",
			// Recent fills keep this check independent of the next swap in a quiet market.
			InitialTickBlocks = 250,
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC@BITCOIN-USDC@ETHEREUM",
		BoardCode = BoardCodes.Chainflip,
	};

	// The initial tick replay reads recent blocks through multiple RPC calls per block.
	protected override TimeSpan DataTimeout => TestContext?.TestName == nameof(Ticks)
		? TimeSpan.FromSeconds(90)
		: base.DataTimeout;

	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes =>
		[DataType.CandleTimeFrame];
}
