namespace StockSharp.Connectors.Tests;

using System;
using System.Numerics;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Velora;
using StockSharp.Velora.Native;
using StockSharp.Velora.Native.Model;

[TestClass]
public class VeloraTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedMarketEndpoint()
	{
		var adapter = new VeloraMessageAdapter(new IncrementalIdGenerator());

		AreEqual("https://api.velora.xyz", adapter.ApiEndpoint);
		AreEqual("StockSharp", adapter.Partner);
		AreEqual(
			"https://ethereum-rpc.publicnode.com",
			adapter.RpcEndpoint);
		AreEqual(VeloraChains.Ethereum, adapter.Chain);
		AreEqual(0.5m, adapter.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
		IsTrue(adapter.IsAutoApprove);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAndTradingOptions()
	{
		var source = new VeloraMessageAdapter(new IncrementalIdGenerator())
		{
			Partner = "stocksharp-test",
			Chain = VeloraChains.Arbitrum,
			WalletAddress =
				"0x1111111111111111111111111111111111111111",
			PrivateKey = "secret".Secure(),
			ApiEndpoint = "https://example.test/api/",
			RpcEndpoint = "https://rpc.example.test/",
			Markets =
				"0x1111111111111111111111111111111111111111|" +
				"0x2222222222222222222222222222222222222222|AAA-BBB",
			ProbeVolume = 2.5m,
			SlippageTolerance = 1.25m,
			PollingInterval = TimeSpan.FromSeconds(9),
			ReceiptTimeout = TimeSpan.FromMinutes(4),
			IsAutoApprove = false,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new VeloraMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("stocksharp-test", target.Partner);
		AreEqual(VeloraChains.Arbitrum, target.Chain);
		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual("https://example.test/api", target.ApiEndpoint);
		AreEqual("https://rpc.example.test", target.RpcEndpoint);
		AreEqual(source.Markets, target.Markets);
		AreEqual(2.5m, target.ProbeVolume);
		AreEqual(1.25m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(9), target.PollingInterval);
		AreEqual(TimeSpan.FromMinutes(4), target.ReceiptTimeout);
		IsFalse(target.IsAutoApprove);
	}

	[TestMethod]
	public void PriceResponseUsesMarketApiShape()
	{
		const string sourceAddress =
			"0x1111111111111111111111111111111111111111";
		const string destinationAddress =
			"0x2222222222222222222222222222222222222222";
		const string router =
			"0x3333333333333333333333333333333333333333";
		var json =
			"{\"priceRoute\":{\"blockNumber\":123,\"network\":1," +
			$"\"srcToken\":\"{sourceAddress}\",\"srcDecimals\":6," +
			"\"srcAmount\":\"1000000\"," +
			$"\"destToken\":\"{destinationAddress}\"," +
			"\"destDecimals\":6,\"destAmount\":\"995000\"," +
			"\"bestRoute\":[{}],\"gasCost\":\"210000\"," +
			"\"version\":\"6.2\"," +
			$"\"contractAddress\":\"{router}\"," +
			$"\"tokenTransferProxy\":\"{router}\"," +
			"\"contractMethod\":\"swapExactAmountIn\"," +
			"\"maxImpactReached\":false,\"hmac\":\"route-hmac\"}}";
		var response = JsonConvert.DeserializeObject<VeloraPriceResponse>(json);
		var source = new VeloraToken
		{
			Address = sourceAddress,
			Decimals = 6,
			Symbol = "AAA",
		};
		var destination = new VeloraToken
		{
			Address = destinationAddress,
			Decimals = 6,
			Symbol = "BBB",
		};

		AreEqual(new BigInteger(995000),
			VeloraMessageAdapter.ValidatePriceRoute(response.PriceRoute,
				source, destination, new BigInteger(1000000),
				VeloraChains.Ethereum));
		AreEqual(router,
			VeloraMessageAdapter.GetRouteTarget(response.PriceRoute));
	}

	[TestMethod]
	public void BuildResponseUsesTransactionEnvelope()
	{
		const string json =
			"{\"from\":\"0x1111111111111111111111111111111111111111\"," +
			"\"to\":\"0x2222222222222222222222222222222222222222\"," +
			"\"value\":\"0\",\"data\":\"0x1234\"," +
			"\"gasPrice\":\"100\",\"gas\":\"210000\",\"chainId\":1}";

		var response = JsonConvert.DeserializeObject<VeloraTransactionData>(
			json);

		AreEqual(1, response.ChainId);
		AreEqual("210000", response.Gas);
		AreEqual("0x1234", response.Data);
	}

	[TestMethod]
	public void AddressNormalizationRejectsMalformedInput()
	{
		AreEqual(
			"0xabcdefabcdefabcdefabcdefabcdefabcdefabcd",
			"0xABCDEFabcdefABCDEFabcdefABCDEFabcdefABCD"
				.NormalizeAddress());
		Throws<ArgumentException>(() => "0x1234".NormalizeAddress());
		Throws<ArgumentException>(() =>
			"0xZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ"
				.NormalizeAddress());
	}

	[TestMethod]
	public void TokenUnitsRoundTripWithoutPrecisionLoss()
	{
		var units = 123.456789m.ToBaseUnits(6);

		AreEqual(new BigInteger(123456789), units);
		AreEqual(123.456789m, units.FromBaseUnits(6));
		Throws<InvalidOperationException>(() => 1.0000001m.ToBaseUnits(6));
	}

	[TestMethod]
	public void RpcQuantityEncodingHasNoLeadingZeroNibble()
	{
		AreEqual("0x0", BigInteger.Zero.ToRpcHex());
		AreEqual("0x80", new BigInteger(128).ToRpcHex());
		AreEqual("0xff", new BigInteger(255).ToRpcHex());
	}

	[TestMethod]
	public void EthereumDefaultsUseWrappedTokenMarket()
	{
		var market = VeloraChains.Ethereum.GetDefaultMarkets();

		IsTrue(market.Contains("WETH-USDC",
			StringComparison.Ordinal));
		AreEqual("ETH", VeloraChains.Ethereum.GetNativeSymbol());
	}
}
