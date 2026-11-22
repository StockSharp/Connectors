namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.KyberSwap;
using StockSharp.Messages;
using StockSharp.KyberSwap.Native;
using StockSharp.KyberSwap.Native.Model;

[TestClass]
public class KyberSwapTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KyberSwapMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WETH-USDC",
		BoardCode = BoardCodes.KyberSwap,
	};

	private const string _tokenIn =
		"0x1111111111111111111111111111111111111111";
	private const string _tokenOut =
		"0x2222222222222222222222222222222222222222";
	private const string _router =
		"0x3333333333333333333333333333333333333333";

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task PublicMarketDiscoveryDoesNotRequireQuotes()
	{
		HistoryHttpServer rpc = null;
		await using var chain = rpc = new HistoryHttpServer(() =>
		{
			var request = JObject.Parse(rpc.RequestBody);
			var result = (string)request["method"] switch
			{
				"eth_chainId" => "0x1",
				"eth_getCode" => "0x6000",
				"eth_call" => (string)request["params"][0]["data"] == "0x313ce567"
					? "0x6"
					: "0x" + "544f4b454e".PadRight(64, '0'),
				var method => throw new InvalidOperationException($"Unexpected KyberSwap RPC method '{method}'."),
			};
			return new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = request["id"],
				["result"] = result,
			}.ToString(Formatting.None);
		}, CancellationToken);
		await using var quotes = new HistoryHttpServer(
			() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), CancellationToken);
		var adapter = new KyberSwapMessageAdapter(new IncrementalIdGenerator())
		{
			RpcEndpoint = chain.Address.AbsoluteUri,
			ApiEndpoint = quotes.Address.AbsoluteUri,
			Markets = $"{_tokenIn}|{_tokenOut}|AAA-BBB",
			HeartbeatInterval = TimeSpan.Zero,
		};
		await using var harness = new MarketDataTestHarness(adapter);
		var timeout = TimeSpan.FromSeconds(8);
		var securityId = new SecurityId { SecurityCode = "AAA-BBB", BoardCode = BoardCodes.KyberSwap };
		await harness.ConnectAsync(timeout, CancellationToken);
		var result = await harness.LookupSecuritiesAsync(new() { SecurityId = securityId }, 1, timeout, CancellationToken);
		AreEqual(1, result.Securities.Length);
		AreEqual(securityId, result.Securities[0].SecurityId);
		AreEqual(0, quotes.RequestTimes.Count);

		var level1 = await harness.SubscribeAsync<Level1ChangeMessage>(DataType.Level1,
			securityId, null, null, timeout, CancellationToken);
		IsNotNull(level1.Error);
		IsTrue(quotes.RequestTimes.Count > 0);
	}

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
	public async Task SettingsRoundTripKeepsRoutingOptions()
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
		await source.SaveAsync(storage, CancellationToken);

		var target = new KyberSwapMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

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
