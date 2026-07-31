namespace StockSharp.Connectors.Tests;

using System;
using System.Numerics;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.KyberSwap;
using StockSharp.KyberSwap.Native;
using StockSharp.KyberSwap.Native.Model;

[TestClass]
public class KyberSwapTests : BaseTestClass
{
	private const string _tokenIn =
		"0x1111111111111111111111111111111111111111";
	private const string _tokenOut =
		"0x2222222222222222222222222222222222222222";
	private const string _router =
		"0x3333333333333333333333333333333333333333";

	[TestMethod]
	public void DefaultsUsePublishedAggregatorEndpoint()
	{
		var adapter = new KyberSwapMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://aggregator-api.kyberswap.com",
			adapter.ApiEndpoint);
		AreEqual("StockSharp", adapter.ClientId);
		AreEqual(KyberSwapChains.Ethereum, adapter.Chain);
		AreEqual(
			"https://ethereum-rpc.publicnode.com",
			adapter.RpcEndpoint);
		AreEqual(TimeSpan.FromMinutes(5), adapter.TransactionLifetime);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsRoutingOptions()
	{
		var source = new KyberSwapMessageAdapter(
			new IncrementalIdGenerator())
		{
			ClientId = "StockSharp.Tests",
			Chain = KyberSwapChains.Arbitrum,
			WalletAddress =
				"0x1111111111111111111111111111111111111111",
			PrivateKey = "secret".Secure(),
			ApiEndpoint = "https://example.test/api/",
			RpcEndpoint = "https://rpc.example.test/",
			Markets = $"{_tokenIn}|{_tokenOut}|AAA-BBB",
			ProbeVolume = 2.5m,
			SlippageTolerance = 1.125m,
			PollingInterval = TimeSpan.FromSeconds(9),
			ReceiptTimeout = TimeSpan.FromMinutes(4),
			TransactionLifetime = TimeSpan.FromMinutes(7),
			IsAutoApprove = false,
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new KyberSwapMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("StockSharp.Tests", target.ClientId);
		AreEqual(KyberSwapChains.Arbitrum, target.Chain);
		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual("https://example.test/api", target.ApiEndpoint);
		AreEqual("https://rpc.example.test", target.RpcEndpoint);
		AreEqual(source.Markets, target.Markets);
		AreEqual(1.125m, target.SlippageTolerance);
		AreEqual(TimeSpan.FromMinutes(7), target.TransactionLifetime);
		IsFalse(target.IsAutoApprove);
	}

	[TestMethod]
	public void ChainNamesMatchPublishedPathIdentifiers()
	{
		AreEqual("ethereum", KyberSwapChains.Ethereum.GetApiName());
		AreEqual("bsc", KyberSwapChains.Bnb.GetApiName());
		AreEqual("arbitrum", KyberSwapChains.Arbitrum.GetApiName());
		AreEqual("avalanche", KyberSwapChains.Avalanche.GetApiName());
	}

	[TestMethod]
	public void RouteResponsePreservesCompleteSummaryForBuild()
	{
		var json =
			"{\"code\":0,\"message\":\"successfully\",\"data\":{" +
			"\"routeSummary\":{\"tokenIn\":\"" + _tokenIn + "\"," +
			"\"amountIn\":\"1000000\",\"amountInUsd\":\"1\"," +
			"\"tokenOut\":\"" + _tokenOut + "\"," +
			"\"amountOut\":\"995000\",\"amountOutUsd\":\"0.995\"," +
			"\"gas\":\"250000\",\"gasPrice\":\"100\",\"gasUsd\":\"0.1\"," +
			"\"l1FeeUsd\":\"0\",\"route\":[[{\"pool\":\"" + _router +
			"\",\"tokenIn\":\"" + _tokenIn + "\",\"tokenOut\":\"" +
			_tokenOut + "\",\"swapAmount\":\"1000000\"," +
			"\"amountOut\":\"995000\",\"exchange\":\"test\"," +
			"\"poolType\":\"test\",\"poolExtra\":{\"preserved\":true}," +
			"\"extra\":{\"value\":7}}]],\"routeID\":\"route-1\"," +
			"\"checksum\":\"check\",\"timestamp\":\"123\"}," +
			"\"routerAddress\":\"" + _router + "\"},\"requestId\":\"req\"}";

		var response = JsonConvert.DeserializeObject<KyberSwapRouteResponse>(
			json);
		var source = new KyberSwapToken
		{
			Address = _tokenIn,
			Symbol = "AAA",
			Decimals = 6,
		};
		var destination = new KyberSwapToken
		{
			Address = _tokenOut,
			Symbol = "BBB",
			Decimals = 6,
		};

		AreEqual(
			new BigInteger(995000),
			KyberSwapMessageAdapter.ValidateRoute(
				response.Data, source, destination, new BigInteger(1000000)));
		IsTrue((bool)response.Data.RouteSummary["route"][0][0]
			["poolExtra"]["preserved"]);
		AreEqual("route-1",
			KyberSwapMessageAdapter.ReadSummaryString(
				response.Data.RouteSummary, "routeID"));
	}

	[TestMethod]
	public void BuildResponseCreatesRouterTransaction()
	{
		var summary = JObject.Parse(
			"{\"tokenIn\":\"" + _tokenIn + "\",\"amountIn\":\"1000000\"," +
			"\"tokenOut\":\"" + _tokenOut + "\",\"amountOut\":\"995000\"," +
			"\"gas\":\"250000\",\"routeID\":\"route-1\",\"route\":[[{}]]}");
		var route = new KyberSwapRouteData
		{
			RouteSummary = summary,
			RouterAddress = _router,
		};
		var built = new KyberSwapBuildData
		{
			AmountIn = "1000000",
			AmountOut = "994000",
			Gas = "275000",
			Data = "0x1234",
			RouterAddress = _router,
			TransactionValue = "0",
		};

		AreEqual(
			new BigInteger(994000),
			KyberSwapMessageAdapter.ValidateBuildResponse(
				built, route, new BigInteger(1000000)));
		var transaction = KyberSwapMessageAdapter.ToTransaction(
			built, _router);
		AreEqual(_router, transaction.To);
		AreEqual(new BigInteger(275000), transaction.SuggestedGas);
		AreEqual("0x1234", transaction.Data);
	}

	[TestMethod]
	public void ClientIdAndSlippageAreValidated()
	{
		var adapter = new KyberSwapMessageAdapter(
			new IncrementalIdGenerator());

		Throws<ArgumentException>(() => adapter.ClientId = "bad id");
		Throws<ArgumentOutOfRangeException>(() =>
			adapter.SlippageTolerance = 20.001m);
		adapter.SlippageTolerance = 0m;
		AreEqual(0m, adapter.SlippageTolerance);
	}

	[TestMethod]
	public void TokenUnitsRoundTrip()
	{
		var units = 123.456789m.ToBaseUnits(6);

		AreEqual(new BigInteger(123456789), units);
		AreEqual(123.456789m, units.FromBaseUnits(6));
	}
}
