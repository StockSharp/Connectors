namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Pendle;
using StockSharp.Pendle.Native;
using StockSharp.Pendle.Native.Model;

[TestClass]
public class PendleTests : BaseTestClass
{
	private const string _market =
		"0x34280882267ffa6383b363e278b027be083bbe3b";
	private const string _principal =
		"0xb253eff1104802b97ac7e3ac9fdd73aece295a2c";
	private const string _yield =
		"0x04b7fa1e727d7290d6e24fa9b426d0c940283a95";
	private const string _underlying =
		"0x7f39c581f595b53c5cb19bd0b3f8da6c935e2ca0";

	[TestMethod]
	public void DefaultsUsePublishedEndpoints()
	{
		var adapter = new PendleMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(PendleChains.Ethereum, adapter.Chain);
		AreEqual("https://api-v2.pendle.finance/core",
			adapter.ApiEndpoint);
		AreEqual("https://ethereum-rpc.publicnode.com",
			adapter.RpcEndpoint);
		AreEqual(500, adapter.MaxMarkets);
		AreEqual(1m, adapter.ProbeVolume);
		AreEqual(0.5m, adapter.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(15), adapter.PollingInterval);
		AreEqual(1000, adapter.HistoryLimit);
		IsTrue(adapter.IsAutoApprove);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsEndpointsAndLimits()
	{
		var source = new PendleMessageAdapter(
			new IncrementalIdGenerator())
		{
			Chain = PendleChains.Arbitrum,
			WalletAddress =
				"0x1111111111111111111111111111111111111111",
			PrivateKey = "secret".Secure(),
			ApiEndpoint = "https://api.example.test/core/",
			RpcEndpoint = "https://rpc.example.test/",
			MarketAddresses = _market,
			MaxMarkets = 12,
			ProbeVolume = 2.5m,
			SlippageTolerance = 1.25m,
			PollingInterval = TimeSpan.FromSeconds(20),
			HistoryLimit = 250,
			ReceiptTimeout = TimeSpan.FromMinutes(4),
			IsAutoApprove = false,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new PendleMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(PendleChains.Arbitrum, target.Chain);
		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual("https://api.example.test/core", target.ApiEndpoint);
		AreEqual("https://rpc.example.test", target.RpcEndpoint);
		AreEqual(_market, target.MarketAddresses);
		AreEqual(12, target.MaxMarkets);
		AreEqual(2.5m, target.ProbeVolume);
		AreEqual(1.25m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromSeconds(20), target.PollingInterval);
		AreEqual(250, target.HistoryLimit);
		AreEqual(TimeSpan.FromMinutes(4), target.ReceiptTimeout);
		IsFalse(target.IsAutoApprove);
	}

	[TestMethod]
	public void SwapRatesBecomeBidAndAsk()
	{
		var security = CreateSecurity(PendleAssetKinds.Principal);
		var response = new PendlePricesResponse
		{
			UnderlyingToken = _underlying,
			UnderlyingToPrincipalRate = 1.25m,
			PrincipalToUnderlyingRate = 0.79m,
			UnderlyingToYieldRate = 20m,
			YieldToUnderlyingRate = 0.04m,
			ImpliedApy = 0.035m,
		};

		var principal = PendleMessageAdapter.ValidatePrices(security,
			response);
		var yield = PendleMessageAdapter.ValidatePrices(
			CreateSecurity(PendleAssetKinds.Yield), response);

		AreEqual(0.79m, principal.Bid);
		AreEqual(0.8m, principal.Ask);
		AreEqual(0.04m, yield.Bid);
		AreEqual(0.05m, yield.Ask);
		AreEqual(0.035m, principal.ImpliedApy);
	}

	[TestMethod]
	public void ApiModelsUseCurrentV3ConvertShape()
	{
		const string json =
			"{\"action\":\"swap\",\"inputs\":[{\"token\":\"" +
			_underlying + "\",\"amount\":\"1000\"}]," +
			"\"requiredApprovals\":[{\"token\":\"" + _underlying +
			"\",\"amount\":\"1000\"}],\"routes\":[{" +
			"\"tx\":{\"from\":\"0x000000000000000000000000000000000000dead\"," +
			"\"to\":\"0x8888888888888888888888888888888888888888\"," +
			"\"data\":\"0x1234\"},\"outputs\":[{\"token\":\"" +
			_principal + "\",\"amount\":\"1250\"}]," +
			"\"data\":{\"priceImpact\":-0.001,\"effectiveApy\":0.03}}]}";

		var response = JsonConvert.DeserializeObject<PendleConvertResponse>(
			json);

		AreEqual("swap", response.Action);
		AreEqual(1, response.Routes.Length);
		AreEqual("1250", response.Routes[0].Outputs[0].Amount);
		AreEqual("0x1234", response.Routes[0].Transaction.Data);
		AreEqual(0.03m, response.Routes[0].Data.EffectiveApy);
	}

	[TestMethod]
	public void EvmUnitsRoundUpWithoutLosingPrecision()
	{
		AreEqual(new BigInteger(123456789),
			123.456789m.ToBaseUnits(6));
		AreEqual(new BigInteger(123456790),
			123.4567891m.ToBaseUnitsCeiling(6));
		AreEqual(123.456789m,
			new BigInteger(123456789).FromBaseUnits(6));
		AreEqual(
			"0xabcdefabcdefabcdefabcdefabcdefabcdefabcd",
			"0xABCDEFabcdefABCDEFabcdefABCDEFabcdefABCD"
				.NormalizeAddress());
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveReadOnlyApiReturnsMarketsQuotesHistoryAndRoute()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		using var client = new PendleHttpClient(
			"https://api-v2.pendle.finance/core", PendleChains.Ethereum);

		await client.VerifyAsync(CancellationToken);
		var markets = await client.GetMarketsAsync([_market], 1,
			CancellationToken);
		var market = markets.Single();
		var assets = await client.GetAssetsAsync(
			[_principal, _yield, _underlying], CancellationToken);
		var prices = await client.GetPricesAsync(_market,
			CancellationToken);
		var history = await client.GetHistoryAsync(_market,
			TimeSpan.FromHours(1), DateTime.UtcNow.AddDays(-2),
			DateTime.UtcNow, CancellationToken);
		var route = await client.BuildConvertAsync(_underlying, _principal,
			BigInteger.Pow(10, 15),
			"0x000000000000000000000000000000000000dead",
			0.01m, CancellationToken);

		AreEqual(_market, market.Address);
		IsTrue(assets.Length >= 2);
		IsGreater(prices.UnderlyingToPrincipalRate ?? 0m, 0m);
		IsGreater(prices.PrincipalToUnderlyingRate ?? 0m, 0m);
		IsGreater(history.Length, 0);
		AreEqual("swap", route.Action);
		AreEqual(1, route.Routes.Length);
		IsGreater(route.Routes[0].Outputs[0].Amount.ParseInteger(),
			BigInteger.Zero);
	}

	private static PendleSecurity CreateSecurity(PendleAssetKinds kind)
	{
		var principal = new PendleToken
		{
			Address = _principal,
			Symbol = "PT-WSTETH",
			Name = "PT wstETH",
			Decimals = 18,
		};
		var yield = new PendleToken
		{
			Address = _yield,
			Symbol = "YT-WSTETH",
			Name = "YT wstETH",
			Decimals = 18,
		};
		var market = new PendleMarket
		{
			Address = _market,
			Name = "wstETH",
			Expiry = new DateTime(2027, 12, 30, 0, 0, 0,
				DateTimeKind.Utc),
			PrincipalToken = principal,
			YieldToken = yield,
			UnderlyingToken = new()
			{
				Address = _underlying,
				Symbol = "WSTETH",
				Name = "Wrapped stETH",
				Decimals = 18,
			},
		};
		return new()
		{
			Market = market,
			Token = kind == PendleAssetKinds.Principal
				? principal
				: yield,
			Kind = kind,
			SecurityCode = kind == PendleAssetKinds.Principal
				? "PT-WSTETH"
				: "YT-WSTETH",
		};
	}
}
