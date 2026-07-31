namespace StockSharp.Connectors.Tests;

using System;
using System.Numerics;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.ZeroX;
using StockSharp.ZeroX.Native;
using StockSharp.ZeroX.Native.Model;

[TestClass]
public class ZeroXTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedV2Endpoint()
	{
		var adapter = new ZeroXMessageAdapter(new IncrementalIdGenerator());

		AreEqual("https://api.0x.org", adapter.ApiEndpoint);
		AreEqual(
			"https://ethereum-rpc.publicnode.com",
			adapter.RpcEndpoint);
		AreEqual(ZeroXChains.Ethereum, adapter.Chain);
		AreEqual(0.5m, adapter.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
		IsTrue(adapter.IsAutoApprove);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAndTradingOptions()
	{
		var source = new ZeroXMessageAdapter(new IncrementalIdGenerator())
		{
			ApiKey = "key".Secure(),
			Chain = ZeroXChains.Arbitrum,
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

		var target = new ZeroXMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.ApiKey.UnSecure());
		AreEqual(ZeroXChains.Arbitrum, target.Chain);
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
	public void QuoteResponseUsesSwapApiV2Shape()
	{
		const string json =
			"{\"liquidityAvailable\":true," +
			"\"sellToken\":\"0x1111111111111111111111111111111111111111\"," +
			"\"buyToken\":\"0x2222222222222222222222222222222222222222\"," +
			"\"sellAmount\":\"1000000\",\"buyAmount\":\"995000\"," +
			"\"allowanceTarget\":\"0x3333333333333333333333333333333333333333\"," +
			"\"issues\":{\"allowance\":{\"actual\":\"0\"," +
			"\"spender\":\"0x3333333333333333333333333333333333333333\"}," +
			"\"balance\":null,\"simulationIncomplete\":false," +
			"\"invalidSourcesPassed\":[]}," +
			"\"transaction\":{\"to\":\"0x4444444444444444444444444444444444444444\"," +
			"\"data\":\"0x1234\",\"gas\":\"210000\",\"gasPrice\":\"100\"," +
			"\"value\":\"0\"},\"zid\":\"request-1\"}";

		var response = JsonConvert.DeserializeObject<ZeroXQuoteResponse>(json);

		IsTrue(response.IsLiquidityAvailable);
		AreEqual("1000000", response.SellAmount);
		AreEqual("995000", response.BuyAmount);
		AreEqual(
			"0x3333333333333333333333333333333333333333",
			response.Issues.Allowance.Spender);
		AreEqual("210000", response.Transaction.Gas);
		AreEqual("request-1", response.RequestId);
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
		var market = ZeroXChains.Ethereum.GetDefaultMarkets();

		IsTrue(market.Contains("WETH-USDC",
			StringComparison.Ordinal));
		AreEqual("ETH", ZeroXChains.Ethereum.GetNativeSymbol());
	}
}
