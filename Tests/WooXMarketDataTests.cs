namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.WooX;

/// <summary>
/// WooX publishes market data without credentials.
/// </summary>
[TestClass]
public class WooXMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void DefaultsUseV3WebSockets()
	{
		using var adapter = new WooXMessageAdapter(new IncrementalIdGenerator());
		AreEqual("wss://wss.woox.io/v3/public", adapter.PublicWsEndpoint);
		AreEqual("wss://wss.woox.io/v3/private", adapter.PrivateWsEndpoint);
	}

	[TestMethod]
	public async Task SavedV2AddressesUseV3WhenLoaded()
	{
		using var adapter = new WooXMessageAdapter(new IncrementalIdGenerator());
		var settings = new SettingsStorage()
			.Set(nameof(adapter.PublicWsEndpoint), "wss://wss.woox.io/ws/stream/")
			.Set(nameof(adapter.PrivateWsEndpoint), "wss://wss.woox.io/v2/ws/private/stream/");
		await adapter.LoadAsync(settings, CancellationToken);
		AreEqual("wss://wss.woox.io/v3/public", adapter.PublicWsEndpoint);
		AreEqual("wss://wss.woox.io/v3/private", adapter.PrivateWsEndpoint);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task V3OrderBookUpdatesPreserveUnchangedLevels()
	{
		var timeout = TimeSpan.FromSeconds(5);
		HistoryHttpServer rest = null;
		var snapshots = 0;
		await using var http = rest = new HistoryHttpServer(() => rest.RequestTarget.Split('?')[0] switch
		{
			"/v1/public/info" => """{"success":true,"rows":[{"symbol":"SPOT_BTC_USDT","status":"TRADING"}]}""",
			"/v1/public/orderbook/SPOT_BTC_USDT" => """{"success":true,"timestamp":1700000000000,"bids":[{"price":99,"quantity":1}],"asks":[{"price":101,"quantity":3}]}""",
			"/v3/public/orderbook" when ++snapshots == 1 => """{"success":true,"timestamp":1700000000000,"data":{"bids":[{"price":"99","quantity":"1"}],"asks":[{"price":"101","quantity":"3"}]}}""",
			"/v3/public/orderbook" => """{"success":true,"timestamp":1700000000400,"data":{"bids":[{"price":"109","quantity":"4"}],"asks":[{"price":"111","quantity":"5"}]}}""",
			_ => """{"success":false,"message":"Unexpected request"}""",
		}, CancellationToken);
		using var socket = new WebSocketTestServer();
		await using var harness = new MarketDataTestHarness(new WooXMessageAdapter(new IncrementalIdGenerator())
		{
			RestEndpoint = http.Address.AbsoluteUri,
			PublicWsEndpoint = socket.Endpoint,
		});
		var connecting = harness.ConnectAsync(timeout, CancellationToken);
		using var peer = await socket.AcceptAsync(CancellationToken);
		await connecting;

		var reader = harness.CreateReader();
		var request = new MarketDataMessage
		{
			TransactionId = 100,
			IsSubscribe = true,
			SecurityId = TestSecurityId,
			DataType2 = DataType.MarketDepth,
			MaxDepth = 100,
		};
		harness.Post(request, CancellationToken);
		var subscription = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		AreEqual("SUBSCRIBE", (string)subscription["cmd"]);
		AreEqual("orderbookupdate@SPOT_BTC_USDT@200", (string)subscription["params"][0]);

		await WebSocketTestServer.SendTextAsync(peer, """
			{"topic":"orderbookupdate@SPOT_BTC_USDT@200","ts":1700000000201,"data":{"s":"SPOT_BTC_USDT","prevTs":1700000000000,"ts":1700000000200,"bids":[["99","0"],["100","2"]],"asks":[]}}
			""", CancellationToken);
		var book = await reader.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == 100 && message.Bids.Length > 0 && message.Bids[0].Price == 100m,
			timeout, CancellationToken);
		AreEqual(1, book.Bids.Length);
		AreEqual(2m, book.Bids[0].Volume);
		AreEqual(1, book.Asks.Length);
		AreEqual(101m, book.Asks[0].Price);
		AreEqual(3m, book.Asks[0].Volume);

		await WebSocketTestServer.SendTextAsync(peer, """
			{"topic":"orderbookupdate@SPOT_BTC_USDT@200","ts":1700000000501,"data":{"s":"SPOT_BTC_USDT","prevTs":1700000000400,"ts":1700000000500,"bids":[["109","0"],["110","6"]],"asks":[]}}
			""", CancellationToken);
		var restored = await reader.WaitAsync<QuoteChangeMessage>(message =>
			message.OriginalTransactionId == 100 && message.Bids.Length > 0 && message.Bids[0].Price == 110m,
			timeout, CancellationToken);
		AreEqual(1, restored.Bids.Length);
		AreEqual(6m, restored.Bids[0].Volume);
		AreEqual(1, restored.Asks.Length);
		AreEqual(111m, restored.Asks[0].Price);
		AreEqual(5m, restored.Asks[0].Volume);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PrivateV3AuthenticatesWithListenKeyAndPublishesBalances()
	{
		const string key = "mock listen+/key";
		var timeout = TimeSpan.FromSeconds(5);
		HistoryHttpServer rest = null;
		await using var http = rest = new HistoryHttpServer(() => rest.RequestTarget switch
		{
			"/v1/public/info" => """{"success":true,"rows":[]}""",
			"/v3/account/listenKey" when rest.RequestBody == """{"type":"WEBSOCKET"}""" && rest.RequestHeaders["x-api-key"] == "mock-api-key" =>
				$$$"""{"success":true,"data":{"authKey":"{{{key}}}","expiredTime":{{{DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds()}}}}}""",
			"/v3/balances" or "/v3/positions" => """{"success":true,"data":{}}""",
			_ => """{"success":false,"message":"Unexpected request"}""",
		}, CancellationToken);
		using var publicSocket = new WebSocketTestServer();
		using var privateSocket = new WebSocketTestServer();
		await using var harness = new MarketDataTestHarness(new WooXMessageAdapter(new IncrementalIdGenerator())
		{
			RestEndpoint = http.Address.AbsoluteUri,
			PublicWsEndpoint = publicSocket.Endpoint,
			PrivateWsEndpoint = privateSocket.Endpoint,
			Key = "mock-api-key".Secure(),
			Secret = "mock-api-secret".Secure(),
		});

		string socketKey = null;
		var publicAccept = publicSocket.AcceptAsync(CancellationToken);
		var privateAccept = privateSocket.AcceptAsync(CancellationToken, request => socketKey = request.QueryString["key"]);
		var accepting = Task.WhenAll(publicAccept, privateAccept);
		var connecting = harness.ConnectAsync(timeout, CancellationToken);
		if (await Task.WhenAny(connecting, accepting) == connecting)
			await connecting;
		using var publicPeer = await publicAccept;
		using var privatePeer = await privateAccept;
		await connecting;
		AreEqual(key, socketKey);

		var reader = harness.CreateReader();
		harness.Post(new PortfolioLookupMessage { IsSubscribe = true, TransactionId = 100 }, CancellationToken);
		var balanceSubscription = await WebSocketTestServer.ReadRequestAsync(privatePeer, CancellationToken);
		AreEqual("SUBSCRIBE", (string)balanceSubscription["cmd"]);
		AreEqual("balance", (string)balanceSubscription["params"][0]);
		var positionSubscription = await WebSocketTestServer.ReadRequestAsync(privatePeer, CancellationToken);
		AreEqual("position", (string)positionSubscription["params"][0]);

		await WebSocketTestServer.SendTextAsync(privatePeer, """
			{"topic":"balance","ts":1700000000123,"data":{"balances":[{"t":"USDT","h":"10","f":"3","pnl":"2","ts":1700000000000}]}}
			""", CancellationToken);
		var balance = await reader.WaitAsync<PositionChangeMessage>(message => message.OriginalTransactionId == 100,
			timeout, CancellationToken);
		AreEqual("USDT", balance.SecurityId.SecurityCode);
		AreEqual(10m, (decimal)balance.Changes[PositionChangeTypes.CurrentValue]);
		AreEqual(3m, (decimal)balance.Changes[PositionChangeTypes.BlockedValue]);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new WooXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SPOT_BTC_USDT",
		BoardCode = BoardCodes.WooX,
	};
}
