namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using TonSdk.Core;
using TonSdk.Core.Boc;

using StockSharp.Messages;
using StockSharp.StonFi;
using StockSharp.StonFi.Native;
using StockSharp.StonFi.Native.Model;

/// <summary>
/// Tests for the StonFi connector.
/// </summary>
[TestClass]
public class StonFiTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EventHistoryWaitsForTheServersRetryAfter()
	{
		var attempts = 0;
		await using var server = new HistoryHttpServer(() =>
		{
			var response = new HttpResponseMessage(++attempts == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
			{
				Content = new StringContent("""{"events":[]}"""),
			};
			if (attempts == 1)
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
			return response;
		}, CancellationToken);
		using var client = new StonFiRestClient(server.Address.AbsoluteUri);

		await client.GetEventsAsync(90, 100, CancellationToken);

		var times = server.RequestTimes.ToArray();
		AreEqual(2, times.Length);
		IsTrue(times[1] - times[0] >= TimeSpan.FromSeconds(1), "The native retry loop must wait for the full server delay.");
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ARepeatedHistoryConnectionFailureEscapesAfterTheRetryLimit()
	{
		var attempts = 0;
		var expected = new HttpRequestException("Connection reset by peer.");
		using var client = new StonFiRestClient("https://api.example.test");
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			attempts++;
			throw expected;
		})), "_http");

		var actual = await ThrowsAsync<HttpRequestException>(async () =>
			await client.GetEventsAsync(90, 100, CancellationToken));

		AreEqual(expected, actual);
		AreEqual(4, attempts);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task APostRequestIsNotReplayedAfterATransportFailure()
	{
		var attempts = 0;
		using var client = new StonFiRestClient("https://api.example.test");
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			AreEqual(HttpMethod.Post, request.Method);
			attempts++;
			throw new HttpRequestException("Connection reset by peer.");
		})), "_http");

		await ThrowsAsync<HttpRequestException>(async () =>
			await client.SimulateSwapAsync(_poolAddress, _poolAddress, 1, 0.01m, _poolAddress, false, CancellationToken));

		AreEqual(1, attempts);
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EventHistoryRetriesAResetConnection(int failures)
	{
		var attempts = 0;
		using var client = new StonFiRestClient("https://api.example.test");
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			if (attempts++ < failures)
				throw new HttpRequestException("Connection reset by peer.", new IOException("Connection reset by peer."));
			return new(HttpStatusCode.OK) { Content = new StringContent("""{"events":[]}""") };
		})), "_http");

		var events = await client.GetEventsAsync(90, 100, CancellationToken);

		AreEqual(0, events.Length);
		AreEqual(failures + 1, attempts);
	}

	private const string _poolAddress =
		"EQCGScrZe1xbyWqWDvdI6mzP-GAcAWFv6ZXuaJOuSqemxku4";
	private const string _ownerAddress =
		"UQAQnxLqlX2B6w4jQzzzPWA8eyWZVZBz6Y0D_8noARLOaEAn";
	private const string _askWalletAddress =
		"kQB_TOJSB7q3-Jm1O8s0jKFtqLElZDPjATs5uJGsujcjznq3";

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicMarketDataDoesNotRequireTonCenter(bool isWalletConfigured)
	{
		var token0 = _ownerAddress;
		var token1 = _poolAddress;
		var tonRequests = 0;
		HistoryHttpServer server = null;
		await using (server = new HistoryHttpServer(() =>
		{
			if (server.RequestTarget.StartsWith("/ton/"))
			{
				tonRequests++;
				return """{"ok":false,"result":"Ratelimit exceed","code":429}""";
			}

			if (server.RequestTarget.StartsWith("/v1/pools/"))
				return $$$"""{"pool":{"address":"{{{_poolAddress}}}","token0_address":"{{{token0}}}","token1_address":"{{{token1}}}","reserve0":"1000000","reserve1":"2000000"}}""";
			if (server.RequestTarget == "/v1/assets/query")
				return $$$"""{"asset_list":[{"contract_address":"{{{token0}}}","symbol":"BASE","decimals":6},{"contract_address":"{{{token1}}}","symbol":"QUOTE","decimals":6}]}""";
			return """{"block":{"blockNumber":100,"blockTimestamp":1760000000}}""";
		}, CancellationToken))
		{
			var adapter = new StonFiMessageAdapter(new IncrementalIdGenerator())
			{
				ApiEndpoint = server.Address.ToString(),
				TonCenterEndpoint = new Uri(server.Address, "ton/").ToString(),
				Pools = _poolAddress,
				WalletAddress = isWalletConfigured ? _ownerAddress : null,
			};
			await using var harness = new MarketDataTestHarness(adapter);
			if (isWalletConfigured)
			{
				var error = await ThrowsAsync<InvalidOperationException>(() => harness.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken));
				IsTrue(error.Message.Contains("Ratelimit exceed"), error.Message);
				AreEqual(1, tonRequests);
			}
			else
			{
				await harness.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken);
				AreEqual(0, tonRequests);
			}
		}
	}

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
	public async Task SettingsRoundTripKeepsEndpointsWalletAndLimits()
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
		await source.SaveAsync(storage, CancellationToken);

		var target = new StonFiMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

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
		if (!Env("STOCKSHARP_LIVE_TESTS")
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

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new StonFiMessageAdapter(new IncrementalIdGenerator())
		{
			Pools =
				"EQCGScrZe1xbyWqWDvdI6mzP-GAcAWFv6ZXuaJOuSqemxku4",
			PoolLimit = 1,
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USD₮/GRAM",
		BoardCode = BoardCodes.StonFi,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 10;
}
