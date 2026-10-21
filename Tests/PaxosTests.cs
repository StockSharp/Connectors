namespace StockSharp.Connectors.Tests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Paxos;
using StockSharp.Paxos.Native;
using StockSharp.Paxos.Native.Model;

/// <summary>
/// Tests for the Paxos connector.
/// </summary>
[TestClass]
[CLSCompliant(false)]
public class PaxosTests : LiveMarketDataTestBase
{
	private const BindingFlags _instance = BindingFlags.Instance | BindingFlags.NonPublic;

	private static string Book(string type, string time, string amount = "1.5")
		=> $$$"""{"channel":"market_data","payload":{"market":"BTCUSD","type":"{{{type}}}","time":"{{{time}}}","bids":[{"price":"50000","amount":"{{{amount}}}"}],"asks":[{"price":"50100","amount":"2"}]}}""";

	private ValueTask ProcessAsync(PaxosSocketClient client, string json)
		=> (ValueTask)typeof(PaxosSocketClient).GetMethod("OnProcessAsync", _instance).Invoke(client,
			[null, new WebSocketMessage(Encoding.UTF8, Encoding.UTF8.GetBytes(json)), CancellationToken]);

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SocketTokensUseTheRequestedScopeAndShareTheOAuthCache()
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		using var client = new PaxosRestClient("https://api.example.test/v2", "https://auth.example.test/token",
			"test-id".Secure(), "test-secret".Secure(), adapter.Scopes);
		var calls = 0;
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(request =>
		{
			calls++;
			AreEqual("https://auth.example.test/token", request.RequestUri.AbsoluteUri);
			var form = Uri.UnescapeDataString(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			IsTrue(form.Contains("exchange:read_aggregated_marketdata_stream", StringComparison.Ordinal));
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"access_token":"test-access-token","expires_in":3600,"token_type":"bearer"}"""),
			};
		})));
		var method = typeof(PaxosRestClient).GetMethod("GetAccessTokenAsync");
		IsNotNull(method);
		var first = ((ValueTask<string>)method.Invoke(client, [CancellationToken])).AsTask();
		var second = ((ValueTask<string>)method.Invoke(client, [CancellationToken])).AsTask();

		AreEqual("test-access-token", await first);
		AreEqual("test-access-token", await second);
		AreEqual(1, calls);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void ChannelsUseTheWebSocketRoot(bool executions)
	{
		using var client = new PaxosSocketClient("wss://ws.paxos.com/", "BTCUSD",
			executions ? PaxosSocketFeeds.Executions : PaxosSocketFeeds.MarketData, new(), 0);

		AreEqual(new Uri("wss://ws.paxos.com/"), typeof(PaxosSocketClient).GetField("_endpoint", _instance).GetValue(client));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task WrappedSnapshotsAreDispatchedWithoutAPartialSnapshotFlag()
	{
		using var client = new PaxosSocketClient("wss://ws.paxos.com/", "BTCUSD", PaxosSocketFeeds.MarketData, new(), 0);
		PaxosBookMessage received = null;
		var errors = new List<Exception>();
		client.BookSnapshotReceived += (data, _) => { received = data; return default; };
		client.Error += (error, _) => { errors.Add(error); return default; };

		await ProcessAsync(client, Book("SNAPSHOT", "2024-01-15T10:30:00.123456789Z"));

		AreEqual(0, errors.Count);
		AreEqual("BTCUSD", received?.Market);
		AreEqual("50000", received.Bids.Single().Price);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task BufferedUpdatesAreAppliedAfterTheSnapshotAtNanosecondPrecision()
	{
		using var client = new PaxosSocketClient("wss://ws.paxos.com/", "BTCUSD", PaxosSocketFeeds.MarketData, new(), 0);
		var sequence = new List<string>();
		var updates = new List<JObject>();
		client.BookSnapshotReceived += (_, _) => { sequence.Add("snapshot"); return default; };
		client.BookUpdateReceived += (data, _) =>
		{
			sequence.Add("update");
			updates.Add(JObject.FromObject(data));
			return default;
		};

		await ProcessAsync(client, Book("UPDATE", "2024-01-15T10:30:00.123456788Z", "2"));
		await ProcessAsync(client, Book("UPDATE", "2024-01-15T10:30:00.123456790Z", "3"));
		AreEqual(0, sequence.Count);
		await ProcessAsync(client, Book("SNAPSHOT", "2024-01-15T10:30:00.123456789Z"));

		AreEqual("snapshot,update", string.Join(',', sequence));
		AreEqual("3", updates.Single()["bids"]?[0]?["amount"]?.Value<string>());
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExecutionsDoNotRequireAnIdentifierAbsentFromTheFeed()
	{
		using var client = new PaxosSocketClient("wss://ws.paxos.com/", "BTCUSD", PaxosSocketFeeds.Executions, new(), 0);
		PaxosPublicExecution received = null;
		client.ExecutionReceived += (data, _) => { received = data; return default; };

		await ProcessAsync(client, """{"channel":"execution_data","payload":{"market":"BTCUSD","price":"50000","amount":"0.5","executed_at":"2024-01-15T10:30:00.123456789Z"}}""");

		AreEqual("BTCUSD", received?.Market);
		AreEqual("0.5", received.Amount);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SubscriptionAcknowledgementsReportChannelErrors(bool success)
	{
		using var client = new PaxosSocketClient("wss://ws.paxos.com/", "BTCUSD", PaxosSocketFeeds.MarketData, new(), 0);
		var errors = new List<Exception>();
		client.Error += (error, _) => { errors.Add(error); return default; };

		await ProcessAsync(client, $$"""{"type":"subscribe","channels":[{"type":"market_data","params":{"market":"BTCUSD"},"success":{{success.ToString().ToLowerInvariant()}},"error":"UNKNOWN_CHANNEL_PARAMS_CONFIGURATION"}]}""");

		AreEqual(success ? 0 : 1, errors.Count);
		if (!success)
			IsTrue(errors.Single().Message.Contains("UNKNOWN_CHANNEL_PARAMS_CONFIGURATION", StringComparison.Ordinal));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AdapterPublishesTheCompleteSnapshotAndBothUpdateSides()
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		AddSubscription(adapter, DataType.MarketDepth);
		var received = new List<QuoteChangeMessage>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is QuoteChangeMessage book)
				received.Add(book);
			return default;
		};
		var snapshot = JsonConvert.DeserializeObject<PaxosBookMessage>(
			"""{"market":"BTCUSD","type":"SNAPSHOT","time":"2024-01-15T10:30:00Z","bids":[{"price":"50000","amount":"1.5"}],"asks":[{"price":"50100","amount":"2"}]}""");
		var update = JsonConvert.DeserializeObject<PaxosBookMessage>(
			"""{"market":"BTCUSD","type":"UPDATE","time":"2024-01-15T10:30:01Z","bids":[{"price":"50000","amount":"0"}],"asks":[{"price":"50101","amount":"3"}]}""");

		await (ValueTask)typeof(PaxosMessageAdapter).GetMethod("OnBookSnapshotAsync", _instance).Invoke(adapter, [snapshot, CancellationToken]);
		await (ValueTask)typeof(PaxosMessageAdapter).GetMethod("OnBookUpdateAsync", _instance).Invoke(adapter, [update, CancellationToken]);

		AreEqual(2, received.Count);
		AreEqual(QuoteChangeStates.SnapshotComplete, received[0].State);
		AreEqual(50000m, received[0].Bids.Single().Price);
		AreEqual(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), received[0].ServerTime);
		AreEqual(QuoteChangeStates.Increment, received[1].State);
		AreEqual(0m, received[1].Bids.Single().Volume);
		AreEqual(50101m, received[1].Asks.Single().Price);
	}

	[TestMethod]
	[DataRow(0, 2)]
	[DataRow(1, 1)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExecutionsWithoutIdsKeepDistinctEventsAndRespectCount(int maximum, int expected)
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		AddSubscription(adapter, DataType.Ticks, maximum);
		var received = new List<ExecutionMessage>();
		var finished = 0;
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is ExecutionMessage execution)
				received.Add(execution);
			if (message is SubscriptionFinishedMessage)
				finished++;
			return default;
		};
		var trade = new PaxosPublicExecution
		{
			Market = "BTCUSD", Price = "50000", Amount = "0.5", ExecutedAt = "2024-01-15T10:30:00.123456789Z",
		};
		var method = typeof(PaxosMessageAdapter).GetMethod("OnPublicExecutionAsync", _instance);

		await (ValueTask)method.Invoke(adapter, [trade, CancellationToken]);
		await (ValueTask)method.Invoke(adapter, [trade, CancellationToken]);

		AreEqual(expected, received.Count);
		AreEqual(maximum == 0 ? 0 : 1, finished);
		foreach (var execution in received)
		{
			AreEqual(50000m, execution.TradePrice);
			AreEqual(0.5m, execution.TradeVolume);
			IsTrue(execution.TradeStringId.IsEmpty());
		}
	}

	private static void AddSubscription(PaxosMessageAdapter adapter, DataType dataType, int maximum = 0)
	{
		var type = typeof(PaxosMessageAdapter).GetNestedType("MarketSubscription", BindingFlags.NonPublic);
		var subscription = Activator.CreateInstance(type, true);
		type.GetProperty("Market").SetValue(subscription, new PaxosMarket { Market = "BTCUSD" });
		type.GetProperty("DataType").SetValue(subscription, dataType);
		type.GetProperty("Depth").SetValue(subscription, 10);
		if (maximum > 0)
			type.GetProperty("Remaining").SetValue(subscription, (long)maximum);
		((IDictionary)typeof(PaxosMessageAdapter).GetField("_marketSubscriptions", _instance).GetValue(adapter)).Add(1L, subscription);
	}

	[TestMethod]
	[DataRow("level1")]
	[DataRow("book")]
	[DataRow("ticks")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AnonymousSubscriptionsReportMissingCredentialsBeforeSendingRequests(string kind)
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		var client = new PaxosRestClient("https://api.example.test/v2", "https://auth.example.test/token", null, null, null);
		var calls = 0;
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ =>
		{
			calls++;
			return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
		})) { BaseAddress = new("https://api.example.test/v2/") });
		typeof(PaxosMessageAdapter).GetField("_restClient", _instance).SetValue(adapter, client);
		((Dictionary<string, PaxosMarket>)typeof(PaxosMessageAdapter).GetField("_markets", _instance).GetValue(adapter))
			.Add("BTCUSD", new() { Market = "BTCUSD" });
		var errors = new List<Exception>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is SubscriptionResponseMessage { Error: not null } reply)
				errors.Add(reply.Error);
			return default;
		};

		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 1,
			IsSubscribe = true,
			SecurityId = new() { SecurityCode = "BTCUSD", BoardCode = BoardCodes.Paxos },
			DataType2 = kind switch { "level1" => DataType.Level1, "book" => DataType.MarketDepth, _ => DataType.Ticks },
		}, CancellationToken);

		AreEqual(0, calls);
		IsTrue(errors.Single().Message.Contains("Client ID", StringComparison.Ordinal));
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
	{
		var clientId = Env("PAXOS_CLIENT_ID");
		var clientSecret = Env("PAXOS_CLIENT_SECRET");
		if (clientId.IsEmpty() || clientSecret.IsEmpty())
			Inconclusive("Set PAXOS_CLIENT_ID and PAXOS_CLIENT_SECRET for an account authorized to read Paxos market data.");

		return new PaxosMessageAdapter(new IncrementalIdGenerator())
		{
			Key = clientId.Secure(),
			Secret = clientSecret.Secure(),
			Scopes = "exchange:read_aggregated_marketdata_stream exchange:historical_prices",
		};
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.Paxos,
	};

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PublicSecurities()
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		adapter.RemoveTransactionalSupport();
		var count = 0;
		await foreach (var security in adapter.ConnectAndDownloadAsync<SecurityMessage>(new SecurityLookupMessage
		{
			SecurityId = TestSecurityId,
			Count = 1,
		}).WithCancellation(CancellationToken))
		{
			AreEqual(TestSecurityId, security.SecurityId);
			count++;
		}
		AreEqual(1, count);
	}
}
