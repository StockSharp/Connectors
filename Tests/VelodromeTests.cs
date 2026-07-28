namespace StockSharp.Connectors.Tests;

using System;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Velodrome;
using StockSharp.Velodrome.Native;
using StockSharp.Velodrome.Native.Model;
using StockSharp.Messages;

[TestClass]
public class VelodromeTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUseOptimismAndPublishedPools()
	{
		var adapter = new VelodromeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://mainnet.optimism.io", adapter.RpcEndpoint);
		AreEqual("wss://mainnet.optimism.io",
			adapter.WebSocketEndpoint);
		IsTrue(adapter.Pools.Contains("WETH-USDC-VOLATILE",
			StringComparison.Ordinal));
		IsTrue(adapter.Pools.Contains("VELO-WETH-VOLATILE",
			StringComparison.Ordinal));
		IsTrue(adapter.Pools.Contains("WETH-USDC-CL1",
			StringComparison.Ordinal));
		AreEqual(10, VelodromeExtensions.ChainId);
		AreEqual(0.5m, adapter.SlippageTolerance);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAndRiskControls()
	{
		var source = new VelodromeMessageAdapter(
			new IncrementalIdGenerator())
		{
			WalletAddress =
				"0x1111111111111111111111111111111111111111",
			PrivateKey = "secret".Secure(),
			RpcEndpoint = "https://rpc.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			Pools =
				"0x2222222222222222222222222222222222222222|" +
				"0x3333333333333333333333333333333333333333|" +
				"0x4444444444444444444444444444444444444444|" +
				"AAA-BBB-VOLATILE",
			HistoryBlockRange = 1000,
			HistoryBlockCount = 10000,
			ProbeVolume = 2m,
			SlippageTolerance = 1.25m,
			PollingInterval = TimeSpan.FromSeconds(8),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new VelodromeMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual("https://rpc.example.test", target.RpcEndpoint);
		AreEqual("wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual(source.Pools, target.Pools);
		AreEqual(1000, target.HistoryBlockRange);
		AreEqual(10000, target.HistoryBlockCount);
		AreEqual(2m, target.ProbeVolume);
		AreEqual(1.25m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(8), target.PollingInterval);
	}

	[TestMethod]
	public void PublishedContractAddressesAreValid()
	{
		AreEqual(
			"0xf1046053aa5682b4f9a81b5481394da16be5ff5a",
			VelodromeExtensions.ClassicFactoryAddress.NormalizeAddress());
		AreEqual(
			"0xa062ae8a9c5e11aaa026fc2670b0d65ccc8b2858",
			VelodromeExtensions.ClassicRouterAddress.NormalizeAddress());
		AreEqual(
			"0xcc0bddb707055e04e497ab22a59c2af4391cd12f",
			VelodromeExtensions.InitialSlipstreamFactoryAddress
				.NormalizeAddress());
		AreEqual(
			"0x0792a633f0c19c351081cf4b211f68f79bcc9676",
			VelodromeExtensions.InitialSlipstreamRouterAddress
				.NormalizeAddress());
	}

	[TestMethod]
	public void TokenUnitsAndRpcQuantitiesAreExact()
	{
		var units = 123.456789m.ToBaseUnits(6);

		AreEqual(123456789.To<BigInteger>(), units);
		AreEqual(123.456789m, units.FromBaseUnits(6));
		AreEqual("0x80", 128.To<BigInteger>().ToRpcHex());
		Throws<ArgumentException>(() => "0x1234".NormalizeAddress());
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveClassicPoolReturnsBothQuoteDirections()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		using var client = new VelodromeRpcClient(
			"https://optimism-rpc.publicnode.com", null, null);
		await client.VerifyChainAsync(CancellationToken);
		var pool = await client.GetPoolAsync(
			"0xf4f2657ae744354baca871e56775e5083f7276ab",
			CancellationToken);
		var weth = pool.Token0.Symbol.EqualsIgnoreCase("WETH")
			? pool.Token0
			: pool.Token1;
		var usdc = ReferenceEquals(weth, pool.Token0)
			? pool.Token1
			: pool.Token0;
		var market = new VelodromeMarket
		{
			PoolId = pool.PoolId,
			PoolType = pool.PoolType,
			FactoryAddress = pool.FactoryAddress,
			RouterAddress = pool.RouterAddress,
			QuoterAddress = pool.QuoterAddress,
			TickSpacing = pool.TickSpacing,
			Token0 = pool.Token0,
			Token1 = pool.Token1,
			BaseToken = weth,
			QuoteToken = usdc,
			SecurityCode = "WETH-USDC-VOLATILE",
		};
		var amount = 1m.ToBaseUnits(weth.Decimals);
		var bid = await client.GetQuoteAsync(market,
			VelodromeTradeTypes.ExactInput, amount, CancellationToken);
		var ask = await client.GetQuoteAsync(market,
			VelodromeTradeTypes.ExactOutput, amount, CancellationToken);

		IsGreater(bid.OutputAmount, BigInteger.Zero);
		IsGreater(ask.InputAmount, BigInteger.Zero);
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveAdapterPublishesLevel1()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		var adapter = new VelodromeMessageAdapter(
			new IncrementalIdGenerator())
		{
			RpcEndpoint = "https://optimism-rpc.publicnode.com",
			WebSocketEndpoint = null,
		};
		await using var harness = new MarketDataTestHarness(adapter);
		await harness.ConnectAsync(TimeSpan.FromSeconds(30),
			CancellationToken);
		try
		{
			var result = await harness.SubscribeAsync<Level1ChangeMessage>(
				DataType.Level1, new()
				{
					SecurityCode = "WETH-USDC-VOLATILE",
					BoardCode = BoardCodes.Velodrome,
				}, null, message => message.Changes.Count > 0,
				TimeSpan.FromSeconds(30), CancellationToken);

			if (result.Error is not null)
				throw result.Error;
			IsTrue(result.Data is not null);
		}
		finally
		{
			await harness.DisconnectAsync(TimeSpan.FromSeconds(30),
				CancellationToken);
		}
	}
}
