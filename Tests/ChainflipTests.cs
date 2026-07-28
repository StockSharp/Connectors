namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Chainflip;
using StockSharp.Chainflip.Native;
using StockSharp.Chainflip.Native.Model;
using StockSharp.Messages;

[TestClass]
public class ChainflipTests : BaseTestClass
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
	public void SettingsRoundTripKeepsEndpointsAddressesAndLimits()
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
		source.Save(storage);

		var target = new ChainflipMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

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
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
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
		var book = await state.GetOrderBookAsync(market, 20,
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
}
