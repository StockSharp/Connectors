namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

using StockSharp.Binance;
using StockSharp.Messages;

[TestClass]
public class BinanceSpotUserDataTests : BaseTestClass
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(8);

	[TestMethod]
	[DataRow(false, "ws-api.binance.com")]
	[DataRow(true, "ws-api.testnet.binance.vision")]
	[CLSCompliant(false)]
	[Timeout(20000, CooperativeCancellation = true)]
	public async Task SpotConnectUsesSignedWebSocketAndPublishesAccountEvents(bool isDemo, string host)
	{
		await using var exchange = new SpotExchange(CancellationToken);
		await using var harness = new MarketDataTestHarness(exchange.CreateAdapter(isDemo));
		var (peer, connect) = await OpenSpotSubscriptionAsync(exchange, harness);
		using (peer)
		{
			var request = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
			var endpoint = exchange.SocketRequests.Single();
			AreEqual(host, endpoint.Host);
			AreEqual("/ws-api/v3", endpoint.AbsolutePath);
			AreEqual("userDataStream.subscribe.signature", request.Value<string>("method"));
			var parameters = (JObject)request["params"];
			AreEqual("test-spot-key", parameters.Value<string>("apiKey"));
			var timestamp = parameters.Value<long>("timestamp");
			IsGreater(timestamp, 0L);
			var expected = HMACSHA256.HashData("test-spot-secret"u8, FormattableString.Invariant($"apiKey=test-spot-key&timestamp={timestamp}").UTF8()).Digest().ToLowerInvariant();
			AreEqual(expected, parameters.Value<string>("signature"));
			IsFalse(connect.IsCompleted, "Spot connected before the exchange confirmed the subscription.");

			var reader = harness.CreateReader();
			await SendBalanceAsync(peer, 0, "USDT");
			await ConfirmSubscriptionAsync(peer, request.Value<long>("id"));
			await connect;
			var balance = await reader.WaitAsync<PositionChangeMessage>(null, _timeout, CancellationToken);
			AreEqual("USDT", balance.SecurityId.SecurityCode);
			AreEqual(12.5m, (decimal)balance.Changes[PositionChangeTypes.CurrentValue]);
			AreEqual(2m, (decimal)balance.Changes[PositionChangeTypes.BlockedValue]);

			await WebSocketTestServer.SendTextAsync(peer, """
				{"subscriptionId":0,"event":{"e":"executionReport","E":1760000000000,
				"s":"BTCUSDT","c":"123","S":"BUY","o":"LIMIT","f":"GTC","q":"1","p":"25000",
				"x":"NEW","X":"NEW","i":456,"z":"0","t":-1}}
				""", CancellationToken);
			var order = await reader.WaitAsync<ExecutionMessage>(null, _timeout, CancellationToken);
			AreEqual(456L, order.OrderId);
			AreEqual(OrderStates.Active, order.OrderState);

			await harness.DisconnectAsync(_timeout, CancellationToken);
		}
		AreEqual(2, exchange.RestRequests.Count);
		IsTrue(exchange.RestRequests.All(path => path == "/api/v3/time"),
			string.Join(", ", exchange.RestRequests));
	}

	[TestMethod]
	[Timeout(20000, CooperativeCancellation = true)]
	public async Task SpotConnectReportsRejectedSubscriptionAndCanBeRetried()
	{
		await using var exchange = new SpotExchange(CancellationToken);
		await using var harness = new MarketDataTestHarness(exchange.CreateAdapter(false));
		var (firstPeer, firstConnect) = await OpenSpotSubscriptionAsync(exchange, harness);
		using (firstPeer)
		{
			var request = await WebSocketTestServer.ReadRequestAsync(firstPeer, CancellationToken);
			await WebSocketTestServer.SendTextAsync(firstPeer, $$"""
				{"id":{{request.Value<long>("id")}},"status":401,"error":{"code":-2015,"msg":"Rejected key"} }
				""", CancellationToken);
			var error = await ThrowsAsync<InvalidOperationException>(() => firstConnect);
			IsTrue(error.Message.Contains("-2015", StringComparison.Ordinal), error.Message);
		}

		var (secondPeer, secondConnect) = await OpenSpotSubscriptionAsync(exchange, harness);
		using (secondPeer)
		{
			var request = await WebSocketTestServer.ReadRequestAsync(secondPeer, CancellationToken);
			await ConfirmSubscriptionAsync(secondPeer, request.Value<long>("id"));
			await secondConnect;
			await harness.DisconnectAsync(_timeout, CancellationToken);
		}
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(20000, CooperativeCancellation = true)]
	public async Task SpotResubscribesAfterTheAccountStreamEnds(bool streamTerminated)
	{
		await using var exchange = new SpotExchange(CancellationToken);
		var adapter = exchange.CreateAdapter(false);
		adapter.ReConnectionSettings.ReAttemptCount = 1;
		await using var harness = new MarketDataTestHarness(adapter);
		var (firstPeer, connect) = await OpenSpotSubscriptionAsync(exchange, harness);
		using (firstPeer)
		{
			var firstRequest = await WebSocketTestServer.ReadRequestAsync(firstPeer, CancellationToken);
			await ConfirmSubscriptionAsync(firstPeer, firstRequest.Value<long>("id"));
			await connect;
			var reader = harness.CreateReader();

			if (streamTerminated)
				await WebSocketTestServer.SendTextAsync(firstPeer,
					"""{"subscriptionId":0,"event":{"e":"eventStreamTerminated","E":1760000000000}}""", CancellationToken);
			else
				await firstPeer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken);

			using var secondPeer = await exchange.Socket.AcceptAsync(CancellationToken);
			var request = await WebSocketTestServer.ReadRequestAsync(secondPeer, CancellationToken);
			AreEqual("userDataStream.subscribe.signature", request.Value<string>("method"));
			IsGreater(request.Value<long>("id"), firstRequest.Value<long>("id"));
			await ConfirmSubscriptionAsync(secondPeer, request.Value<long>("id"), 7);
			await reader.WaitAsync<ConnectionRestoredMessage>(null, _timeout, CancellationToken);

			await SendBalanceAsync(secondPeer, 0, "BTC");
			await SendBalanceAsync(secondPeer, 7, "USDT");
			var balance = await reader.WaitAsync<PositionChangeMessage>(null, _timeout, CancellationToken);
			AreEqual("USDT", balance.SecurityId.SecurityCode);
			await harness.DisconnectAsync(_timeout, CancellationToken);
		}
	}

	[TestMethod]
	[Timeout(20000, CooperativeCancellation = true)]
	public async Task SpotDisconnectCancelsAPendingSubscription()
	{
		await using var exchange = new SpotExchange(CancellationToken);
		var adapter = exchange.CreateAdapter(false);
		await using var harness = new MarketDataTestHarness(adapter);
		var reader = harness.CreateReader();
		var connectionReader = harness.CreateReader();
		var accepting = exchange.Socket.AcceptAsync(CancellationToken);
		var connecting = adapter.SendInMessageAsync(new ConnectMessage(), CancellationToken).AsTask();
		using var peer = await accepting;
		await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);

		await adapter.SendInMessageAsync(new DisconnectMessage(), CancellationToken);
		await connecting.WaitAsync(_timeout, CancellationToken);
		var disconnected = await reader.WaitAsync<DisconnectMessage>(null, _timeout, CancellationToken);
		IsNull(disconnected.Error);
		var canceledConnection = await connectionReader.WaitAsync<ConnectMessage>(null, _timeout, CancellationToken);
		IsNotNull(canceledConnection.Error);

		var (secondPeer, secondConnect) = await OpenSpotSubscriptionAsync(exchange, harness);
		using (secondPeer)
		{
			var request = await WebSocketTestServer.ReadRequestAsync(secondPeer, CancellationToken);
			await ConfirmSubscriptionAsync(secondPeer, request.Value<long>("id"));
			await secondConnect;
			await harness.DisconnectAsync(_timeout, CancellationToken);
		}
	}

	private async Task<(WebSocket Peer, Task Connecting)> OpenSpotSubscriptionAsync(SpotExchange exchange, MarketDataTestHarness harness)
	{
		var accept = exchange.Socket.AcceptAsync(CancellationToken);
		var connect = harness.ConnectAsync(_timeout, CancellationToken);
		if (await Task.WhenAny(accept, connect) == connect)
		{
			await connect;
			await harness.DisconnectAsync(_timeout, CancellationToken);
			Fail($"Spot connected before opening its account WebSocket. The retired REST endpoint returns 410. Requests: {string.Join(", ", exchange.RestRequests)}.");
		}

		return (await accept, connect);
	}

	private Task ConfirmSubscriptionAsync(WebSocket peer, long id, int subscriptionId = 0)
		=> WebSocketTestServer.SendTextAsync(peer, $$"""
			{"id":{{id}},"status":200,"result":{"subscriptionId":{{subscriptionId}} } }
			""", CancellationToken);

	private Task SendBalanceAsync(WebSocket peer, int subscriptionId, string asset)
		=> WebSocketTestServer.SendTextAsync(peer, $$"""
			{"subscriptionId":{{subscriptionId}},"event":{"e":"outboundAccountPosition","E":1760000000000,"u":1760000000000,
			"B":[{"a":"{{asset}}","f":"12.5","l":"2"}] } }
			""", CancellationToken);

	private sealed class SpotExchange : IAsyncDisposable
	{
		private readonly HistoryHttpServer _rest;

		public SpotExchange(CancellationToken cancellationToken)
			=> _rest = new(ReplyToRest, cancellationToken);

		public WebSocketTestServer Socket { get; } = new();
		public ConcurrentQueue<string> RestRequests { get; } = new();
		public ConcurrentQueue<Uri> SocketRequests { get; } = new();

		public BinanceMessageAdapter CreateAdapter(bool isDemo)
		{
			return new(new IncrementalIdGenerator(), endpoint =>
			{
				if (endpoint.Scheme == Uri.UriSchemeWss)
				{
					SocketRequests.Enqueue(endpoint);
					return new Uri(new Uri(Socket.Endpoint), endpoint.PathAndQuery);
				}

				RestRequests.Enqueue(endpoint.AbsolutePath);
				return new Uri(_rest.Address, endpoint.PathAndQuery);
			})
			{
				Sections = [BinanceSections.Spot],
				Key = "test-spot-key".Secure(),
				Secret = "test-spot-secret".Secure(),
				IsDemo = isDemo,
			};
		}

		private HttpResponseMessage ReplyToRest()
			=> _rest.RequestTarget.EndsWith("/time", StringComparison.Ordinal)
				? new(HttpStatusCode.OK) { Content = new StringContent($$"""{"serverTime":{{(long)DateTime.UtcNow.ToUnix(false)}}}""") }
				: new(HttpStatusCode.Gone) { Content = new StringContent("Gone") };

		public async ValueTask DisposeAsync()
		{
			Socket.Dispose();
			await _rest.DisposeAsync();
		}
	}
}
