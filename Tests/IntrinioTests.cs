namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Intrinio;
using StockSharp.Intrinio.Native;
using StockSharp.Intrinio.Native.Model;

/// <summary>
/// Tests for the Intrinio connector.
/// </summary>
[TestClass]
public class IntrinioTests : BaseTestClass
{
	[TestMethod]
	public async Task ReceiveMessageCombinesFragmentedFrames()
	{
		using var socket = new ScriptedWebSocket(
			new("ABC", WebSocketMessageType.Binary, false),
			new("DEF", WebSocketMessageType.Binary, true));

		var message = await IntrinioRealtimeConnection.ReceiveMessageAsync(
			socket, CancellationToken);

		AreEqual(WebSocketMessageType.Binary, message.Type);
		AreEqual("ABCDEF", Encoding.ASCII.GetString(message.Data));
	}

	[TestMethod]
	public async Task StartRetriesTransientAuthorizationFailure()
	{
		using var handler = new ScriptedHttpHandler(
			new(HttpStatusCode.ServiceUnavailable),
			new(HttpStatusCode.OK)
			{
				Content = new StringContent("token"),
			});
		using var socket = new ScriptedWebSocket();
		var delays = new List<TimeSpan>();
		var openCount = 0;
		var dependencies = new IntrinioRealtimeConnectionDependencies
		{
			HttpHandler = handler,
			OpenWebSocketAsync = (_, _, _) =>
			{
				openCount++;
				return Task.FromResult<WebSocket>(socket);
			},
			DelayAsync = (delay, _) =>
			{
				delays.Add(delay);
				return Task.CompletedTask;
			},
		};
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);
		connection.Error += static (_, _) => default;

		await connection.StartAsync(CancellationToken);

		AreEqual(2, handler.RequestCount);
		AreEqual(1, openCount);
		AreEqual(1, delays.Count);
		AreEqual(TimeSpan.FromSeconds(10), delays[0]);

		await connection.StopAsync();
	}

	[TestMethod]
	public async Task StartFailsFastForFatalAuthorizationFailure()
	{
		using var handler = new ScriptedHttpHandler(
			new HttpResponseMessage(HttpStatusCode.Unauthorized));
		var delays = new List<TimeSpan>();
		var openCount = 0;
		var dependencies = new IntrinioRealtimeConnectionDependencies
		{
			HttpHandler = handler,
			OpenWebSocketAsync = (_, _, _) =>
			{
				openCount++;
				return Task.FromResult<WebSocket>(new ScriptedWebSocket());
			},
			DelayAsync = (delay, _) =>
			{
				delays.Add(delay);
				return Task.CompletedTask;
			},
		};
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);

		var error = await ThrowsExactlyAsync<HttpRequestException>(
			() => connection.StartAsync(CancellationToken));

		AreEqual(HttpStatusCode.Unauthorized, error.StatusCode);
		AreEqual(1, handler.RequestCount);
		AreEqual(0, openCount);
		AreEqual(0, delays.Count);
	}

	[TestMethod]
	public async Task StartFailsFastForFatalWebSocketHandshake()
	{
		using var handler = CreateSuccessfulAuthHandler(1);
		var delays = new List<TimeSpan>();
		var openCount = 0;
		var dependencies = new IntrinioRealtimeConnectionDependencies
		{
			HttpHandler = handler,
			OpenWebSocketAsync = (_, _, _) =>
			{
				openCount++;
				return Task.FromException<WebSocket>(new WebSocketException(
					WebSocketError.Faulted, "Forbidden.",
					new HttpRequestException("Forbidden.", null,
						HttpStatusCode.Forbidden)));
			},
			DelayAsync = (delay, _) =>
			{
				delays.Add(delay);
				return Task.CompletedTask;
			},
		};
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);

		await ThrowsExactlyAsync<WebSocketException>(
			() => connection.StartAsync(CancellationToken));

		AreEqual(1, handler.RequestCount);
		AreEqual(1, openCount);
		AreEqual(0, delays.Count);
	}

	[TestMethod]
	public async Task StartLimitsUnknownWebSocketHandshakeRetries()
	{
		using var handler = CreateSuccessfulAuthHandler(4);
		var delays = new List<TimeSpan>();
		var openCount = 0;
		var dependencies = new IntrinioRealtimeConnectionDependencies
		{
			HttpHandler = handler,
			OpenWebSocketAsync = (_, _, _) =>
			{
				openCount++;
				return Task.FromException<WebSocket>(
					new WebSocketException("Handshake failed."));
			},
			DelayAsync = (delay, _) =>
			{
				delays.Add(delay);
				return Task.CompletedTask;
			},
		};
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);
		connection.Error += static (_, _) => default;

		await ThrowsExactlyAsync<WebSocketException>(
			() => connection.StartAsync(CancellationToken));

		AreEqual(4, handler.RequestCount);
		AreEqual(4, openCount);
		IsTrue(delays.SequenceEqual(
		[
			TimeSpan.FromSeconds(10),
			TimeSpan.FromSeconds(30),
			TimeSpan.FromMinutes(1),
		]));
	}

	[TestMethod]
	public async Task ReconnectReauthenticatesAndRejoinsChannels()
	{
		using var handler = CreateSuccessfulAuthHandler(2);
		using var firstSocket = new ScriptedWebSocket();
		using var secondSocket = new ScriptedWebSocket();
		var sockets = new Queue<WebSocket>([firstSocket, secondSocket]);
		var delays = new List<TimeSpan>();
		var dependencies = CreateDependencies(handler, sockets, delays);
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);
		connection.Error += static (_, _) => default;

		await connection.StartAsync(CancellationToken);
		await connection.JoinAsync("AAPL", CancellationToken);
		firstSocket.FailReceive(new WebSocketException("Disconnected."));

		await secondSocket.ReceiveStarted.WaitAsync(
			TimeSpan.FromSeconds(5), CancellationToken);

		var expectedJoin = IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", false);
		IsTrue(firstSocket.SentMessages.Single().SequenceEqual(expectedJoin));
		IsTrue(secondSocket.SentMessages.Single().SequenceEqual(expectedJoin));
		AreEqual(2, handler.RequestCount);
		AreEqual(1, delays.Count);
		AreEqual(TimeSpan.FromSeconds(10), delays[0]);

		await connection.StopAsync();
	}

	[TestMethod]
	public async Task CancelledJoinIsNotRestoredOnReconnect()
	{
		using var handler = CreateSuccessfulAuthHandler(2);
		using var firstSocket = new ScriptedWebSocket
		{
			NextSendError = new OperationCanceledException(CancellationToken),
		};
		using var secondSocket = new ScriptedWebSocket();
		var sockets = new Queue<WebSocket>([firstSocket, secondSocket]);
		var dependencies = CreateDependencies(handler, sockets, []);
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);
		connection.Error += static (_, _) => default;

		await connection.StartAsync(CancellationToken);
		await ThrowsExactlyAsync<OperationCanceledException>(
			() => connection.JoinAsync("AAPL", CancellationToken));
		await secondSocket.ReceiveStarted.WaitAsync(
			TimeSpan.FromSeconds(5), CancellationToken);

		AreEqual(0, secondSocket.SentMessages.Length);

		await connection.StopAsync();
	}

	[TestMethod]
	public async Task CancelledLeaveIsRestoredOnReconnect()
	{
		using var handler = CreateSuccessfulAuthHandler(2);
		using var firstSocket = new ScriptedWebSocket();
		using var secondSocket = new ScriptedWebSocket();
		var sockets = new Queue<WebSocket>([firstSocket, secondSocket]);
		var dependencies = CreateDependencies(handler, sockets, []);
		using var connection = new IntrinioRealtimeConnection(
			"key", IntrinioEquityProviders.Iex, 1, 1, dependencies);
		connection.Error += static (_, _) => default;

		await connection.StartAsync(CancellationToken);
		await connection.JoinAsync("AAPL", CancellationToken);
		firstSocket.NextSendError = new OperationCanceledException(CancellationToken);
		await ThrowsExactlyAsync<OperationCanceledException>(
			() => connection.LeaveAsync("AAPL", CancellationToken));
		await secondSocket.ReceiveStarted.WaitAsync(
			TimeSpan.FromSeconds(5), CancellationToken);

		var expectedJoin = IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", false);
		IsTrue(secondSocket.SentMessages.Single().SequenceEqual(expectedJoin));

		await connection.StopAsync();
	}

	private static ScriptedHttpHandler CreateSuccessfulAuthHandler(int count)
		=> new([.. Enumerable.Range(0, count).Select(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("token"),
			})]);

	private static IntrinioRealtimeConnectionDependencies CreateDependencies(
		HttpMessageHandler handler, Queue<WebSocket> sockets, List<TimeSpan> delays)
		=> new()
		{
			HttpHandler = handler,
			OpenWebSocketAsync = (_, _, _) => Task.FromResult(sockets.Dequeue()),
			DelayAsync = (delay, _) =>
			{
				delays.Add(delay);
				return Task.CompletedTask;
			},
		};

	private sealed class ScriptedHttpHandler(params HttpResponseMessage[] responses)
		: HttpMessageHandler
	{
		private readonly Queue<HttpResponseMessage> _responses = new(responses);

		public int RequestCount { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestCount++;
			if (_responses.Count == 0)
				throw new InvalidOperationException("No HTTP response was scripted.");
			return Task.FromResult(_responses.Dequeue());
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var response in _responses)
					response.Dispose();
				_responses.Clear();
			}
			base.Dispose(disposing);
		}
	}

	private readonly record struct ReceiveStep(
		string Data,
		WebSocketMessageType Type,
		bool EndOfMessage);

	private sealed class ScriptedWebSocket(params ReceiveStep[] steps) : WebSocket
	{
		private readonly Lock _sync = new();
		private readonly Queue<ReceiveStep> _steps = new(steps);
		private readonly List<byte[]> _sentMessages = [];
		private readonly TaskCompletionSource _receiveStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<Exception> _receiveError =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private WebSocketState _state = WebSocketState.Open;
		private WebSocketCloseStatus? _closeStatus;
		private string _closeStatusDescription;

		public Exception NextSendError { get; set; }
		public Task ReceiveStarted => _receiveStarted.Task;
		public byte[][] SentMessages
		{
			get
			{
				lock (_sync)
					return [.. _sentMessages];
			}
		}

		public override WebSocketCloseStatus? CloseStatus => _closeStatus;
		public override string CloseStatusDescription => _closeStatusDescription;
		public override WebSocketState State => _state;
		public override string SubProtocol => null;

		public override void Abort()
		{
			_state = WebSocketState.Aborted;
			_receiveError.TrySetResult(new WebSocketException("Aborted."));
		}

		public override Task CloseAsync(WebSocketCloseStatus closeStatus,
			string statusDescription, CancellationToken cancellationToken)
		{
			_closeStatus = closeStatus;
			_closeStatusDescription = statusDescription;
			_state = WebSocketState.Closed;
			return Task.CompletedTask;
		}

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus,
			string statusDescription, CancellationToken cancellationToken)
		{
			_closeStatus = closeStatus;
			_closeStatusDescription = statusDescription;
			_state = WebSocketState.CloseSent;
			return Task.CompletedTask;
		}

		public override void Dispose()
		{
			_state = WebSocketState.Closed;
			_receiveError.TrySetResult(new ObjectDisposedException(
				nameof(ScriptedWebSocket)));
		}

		public void FailReceive(Exception error)
			=> _receiveError.TrySetResult(error);

		public override async Task<WebSocketReceiveResult> ReceiveAsync(
			ArraySegment<byte> buffer, CancellationToken cancellationToken)
		{
			if (_steps.Count == 0)
			{
				_receiveStarted.TrySetResult();
				throw await _receiveError.Task.WaitAsync(cancellationToken);
			}

			var step = _steps.Dequeue();
			var data = Encoding.ASCII.GetBytes(step.Data);
			data.CopyTo(buffer.Array, buffer.Offset);
			return new(data.Length, step.Type, step.EndOfMessage);
		}

		public override Task SendAsync(ArraySegment<byte> buffer,
			WebSocketMessageType messageType, bool endOfMessage,
			CancellationToken cancellationToken)
		{
			if (NextSendError is { } error)
			{
				NextSendError = null;
				return Task.FromException(error);
			}

			lock (_sync)
				_sentMessages.Add(buffer.ToArray());
			return Task.CompletedTask;
		}
	}

	private const string EquityTradeHex =
		"0020044141504C0658000000484164000000BCCC853DFE9C9717393000000140";
	private const string EquityAskHex =
		"011C044141504C03510000004C41C80000002491C963FE9C97170152";
	private const string EquityBidHex =
		"021B044141504C0845000000444196000000E0FC686AFE9C971700";
	private const string OptionTradeHex =
		"124141504C5F323630383231433132332E343500000000040240E201000700000015CD853DFE9C971763000000000000006CE2010008E201004D4900000013183051000000000000";
	private const string OptionQuoteHex =
		"124141504C5F323630383231433132332E34350000000103393000000B000000343000000C000000B1680871FE9C971700000000";
	private const string OptionRefreshHex =
		"124141504C5F323630383231433132332E34350000000202D2040000D2040000B0040000140500004C0400000000000000000000";

	[TestMethod]
	public void EndpointBuildersMatchOfficialSdk()
	{
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Realtime, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Iex, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-delayed-sip.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.DelayedSip, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-nasdaq-basic.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.NasdaqBasic, "key").AbsoluteUri);
		AreEqual(
			"https://cboe-one.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.CboeOne, "key").AbsoluteUri);
		AreEqual(
			"https://equities-edge.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.EquitiesEdge, "key").AbsoluteUri);

		AreEqual(
			"wss://realtime-delayed-sip.intrinio.com/socket/websocket?vsn=1.0.0&token=token&delayed=true",
			IntrinioRealtimeProtocol.GetEquityWebSocketUri(
				IntrinioEquityProviders.DelayedSip, "token").AbsoluteUri);
		AreEqual(
			"wss://equities-edge.intrinio.com/socket/websocket?vsn=1.0.0&token=token",
			IntrinioRealtimeProtocol.GetEquityWebSocketUri(
				IntrinioEquityProviders.EquitiesEdge, "token").AbsoluteUri);

		AreEqual(
			"https://realtime-options.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				IntrinioOptionProviders.Opra, "key").AbsoluteUri);
		AreEqual(
			"https://options-edge.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				IntrinioOptionProviders.OptionsEdge, "key").AbsoluteUri);
		AreEqual(
			"wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=token&delayed=true",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.Opra, "token", true).AbsoluteUri);
		AreEqual(
			"wss://options-edge.intrinio.com/socket/websocket?vsn=1.0.0&token=token",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.OptionsEdge, "token", false).AbsoluteUri);
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=a%2B%20%2F%3F",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Iex, "a+ /?").AbsoluteUri);
		AreEqual(
			"wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=a%2B%20%2F%3F",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.Opra, "a+ /?", false).AbsoluteUri);

		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				(IntrinioEquityProviders)int.MaxValue, "key"));
		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				(IntrinioOptionProviders)int.MaxValue, "key"));
	}

	[TestMethod]
	public void HeaderBuildersSelectV2Formats()
	{
		var equityAuth = IntrinioRealtimeProtocol.GetEquityAuthHeaders();
		AreEqual(2, equityAuth.Count);
		AreEqual("StockSharp.Intrinio/1.0",
			equityAuth["Client-Information"]);
		AreEqual("v2", equityAuth["UseNewEquitiesFormat"]);

		var equitySocket = IntrinioRealtimeProtocol.GetEquityWebSocketHeaders();
		AreEqual(1, equitySocket.Count);
		AreEqual("v2", equitySocket["UseNewEquitiesFormat"]);

		var optionsAuth = IntrinioRealtimeProtocol.GetOptionsAuthHeaders(true);
		AreEqual(3, optionsAuth.Count);
		AreEqual("StockSharp.Intrinio/1.0",
			optionsAuth["Client-Information"]);
		AreEqual("v2", optionsAuth["UseNewOptionsFormat"]);
		AreEqual("true", optionsAuth["delay"]);

		var optionsSocket =
			IntrinioRealtimeProtocol.GetOptionsWebSocketHeaders(false);
		AreEqual(1, optionsSocket.Count);
		AreEqual("v2", optionsSocket["UseNewOptionsFormat"]);
		IsTrue(!optionsSocket.ContainsKey("delay"));
	}

	[TestMethod]
	public void SubscriptionMessagesMatchOfficialSdk()
	{
		AreBytesEqual("4A004141504C",
			IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", false));
		AreBytesEqual("4A014141504C",
			IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", true));
		AreBytesEqual("4C4141504C",
			IntrinioRealtimeProtocol.EncodeEquityLeave("AAPL"));
		AreBytesEqual("4A002446495245484F5345",
			IntrinioRealtimeProtocol.EncodeEquityJoin("lobby", false));
		AreBytesEqual("4C2446495245484F5345",
			IntrinioRealtimeProtocol.EncodeEquityLeave("lobby"));

		AreBytesEqual("4A074141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"AAPL__260821C00123450", false));
		AreBytesEqual("4A054141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"AAPL260821C00123450", true));
		AreBytesEqual("4C004141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsLeave(
				"AAPL__260821C00123450"));
		AreBytesEqual("4A072446495245484F5345",
			IntrinioRealtimeProtocol.EncodeOptionsJoin("lobby", false));
		AreBytesEqual("4C2446495245484F5345",
			IntrinioRealtimeProtocol.EncodeOptionsLeave("lobby"));
		AreBytesEqual("4A074142435F323630383231503132332E303035",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"ABC___260821P00123005", false));
		AreBytesEqual("4A074141504C",
			IntrinioRealtimeProtocol.EncodeOptionsJoin("AAPL", false));

		ThrowsExactly<ArgumentException>(() =>
			IntrinioRealtimeProtocol.EncodeEquityJoin("", false));
		ThrowsExactly<ArgumentException>(() =>
			IntrinioRealtimeProtocol.EncodeOptionsJoin("A\u0080PL", false));
	}

	[TestMethod]
	public void DecodesEquityGoldenBatch()
	{
		var events = IntrinioRealtimeProtocol.DecodeEquity(Batch(
			Hex(EquityTradeHex), Hex(EquityAskHex), Hex(EquityBidHex)));

		AreEqual(3, events.Count);

		var tradeEvent = events[0];
		AreEqual(IntrinioDecodedEventTypes.EquityTrade, tradeEvent.Type);
		AreEqual("AAPL", tradeEvent.Symbol);
		IsTrue(!tradeEvent.IsOption);
		var trade = tradeEvent.EquityTrade;
		AreEqual("AAPL", trade.Symbol);
		AreEqual(12.5d, trade.Price);
		AreEqual(100u, trade.Size);
		AreEqual(12345ul, trade.TotalVolume);
		AreEqual(Utc(1_700_000_000, 1_234_567), trade.Timestamp);
		AreEqual((byte)6, trade.SubProvider);
		AreEqual('X', trade.MarketCenter);
		AreEqual("@", trade.Condition);

		var askEvent = events[1];
		AreEqual(IntrinioDecodedEventTypes.EquityQuote, askEvent.Type);
		var ask = askEvent.EquityQuote;
		AreEqual(IntrinioEquityQuoteTypes.Ask, ask.Type);
		AreEqual("AAPL", ask.Symbol);
		AreEqual(12.75d, ask.Price);
		AreEqual(200u, ask.Size);
		AreEqual(Utc(1_700_000_000, 7_654_321), ask.Timestamp);
		AreEqual((byte)3, ask.SubProvider);
		AreEqual('Q', ask.MarketCenter);
		AreEqual("R", ask.Condition);

		var bid = events[2].EquityQuote;
		AreEqual(IntrinioEquityQuoteTypes.Bid, bid.Type);
		AreEqual(12.25d, bid.Price);
		AreEqual(150u, bid.Size);
		AreEqual(Utc(1_700_000_000, 8_765_432), bid.Timestamp);
		AreEqual((byte)8, bid.SubProvider);
		AreEqual('E', bid.MarketCenter);
		AreEqual("", bid.Condition);
	}

	[TestMethod]
	public void DecodesOptionsGoldenBatch()
	{
		var events = IntrinioRealtimeProtocol.DecodeOptions(Batch(
			Hex(OptionTradeHex), Hex(OptionQuoteHex), Hex(OptionRefreshHex)));

		AreEqual(3, events.Count);

		var tradeEvent = events[0];
		AreEqual(IntrinioDecodedEventTypes.OptionTrade, tradeEvent.Type);
		AreEqual("AAPL__260821C00123450", tradeEvent.Symbol);
		IsTrue(tradeEvent.IsOption);
		var trade = tradeEvent.OptionTrade;
		AreEqual("AAPL__260821C00123450", trade.Contract);
		AreEqual(12.3456d, trade.Price);
		AreEqual(7u, trade.Size);
		AreEqual(1_700_000_000.1234567d, trade.Timestamp);
		AreEqual(99ul, trade.TotalVolume);
		AreEqual(12.35d, trade.AskPriceAtExecution);
		AreEqual(12.34d, trade.BidPriceAtExecution);
		AreEqual(187.65d, trade.UnderlyingPriceAtExecution);
		IsTrue(trade.Qualifiers.SequenceEqual(
			new byte[] { 0, 19, 24, 48 }));
		AreEqual('Q', trade.Exchange);

		var quote = events[1].OptionQuote;
		AreEqual("AAPL__260821C00123450", quote.Contract);
		AreEqual(12.345d, quote.AskPrice);
		AreEqual(11u, quote.AskSize);
		AreEqual(12.34d, quote.BidPrice);
		AreEqual(12u, quote.BidSize);
		AreEqual(1_700_000_000.9876544d, quote.Timestamp);

		var refresh = events[2].OptionRefresh;
		AreEqual("AAPL__260821C00123450", refresh.Contract);
		AreEqual(1234u, refresh.OpenInterest);
		AreEqual(12.34d, refresh.OpenPrice);
		AreEqual(12d, refresh.ClosePrice);
		AreEqual(13d, refresh.HighPrice);
		AreEqual(11d, refresh.LowPrice);
	}

	[TestMethod]
	public void OptionsDecoderPreservesMissingPriceSentinels()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		Hex("FFFFFF7F").CopyTo(quoteBytes, 24);
		Hex("00000080").CopyTo(quoteBytes, 32);

		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;

		IsTrue(double.IsNaN(quote.AskPrice));
		IsTrue(double.IsNaN(quote.BidPrice));
	}

	[TestMethod]
	public void OptionsDecoderSupportsOfficialPriceScaleTable()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		quoteBytes[23] = 10;
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(24, 4), 512);
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(32, 4), 1024);
		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		AreEqual(1d, quote.AskPrice);
		AreEqual(2d, quote.BidPrice);

		quoteBytes[23] = 11;
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(24, 4), 1);
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(32, 4), -1);
		quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		IsTrue(double.IsPositiveInfinity(quote.AskPrice));
		IsTrue(double.IsNegativeInfinity(quote.BidPrice));

		quoteBytes[23] = 15;
		quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		IsTrue(double.IsNaN(quote.AskPrice));
		IsTrue(double.IsNaN(quote.BidPrice));
	}

	[TestMethod]
	public void DecodersPreserveForwardCompatibleMetadata()
	{
		var equityBytes = Hex(EquityTradeHex);
		equityBytes[7] = 9;
		BinaryPrimitives.WriteUInt16LittleEndian(
			equityBytes.AsSpan(8, 2), '\u03a9');
		var equity = IntrinioRealtimeProtocol.DecodeEquity(
			Batch(equityBytes))[0].EquityTrade;
		AreEqual((byte)9, equity.SubProvider);
		AreEqual('\u03a9', equity.MarketCenter);

		var optionBytes = Hex(OptionTradeHex);
		optionBytes[65] = 0xff;
		var option = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(optionBytes))[0].OptionTrade;
		AreEqual('\u00ff', option.Exchange);
	}

	[TestMethod]
	public void OptionContractDecoderTruncatesFourthFractionalDigit()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		SetOptionContract(quoteBytes, "ABC_260821P123.0057");

		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;

		AreEqual("ABC___260821P00123005", quote.Contract);
	}

	[TestMethod]
	public void OptionsDecoderSkipsKnownUnusualActivityChunks()
	{
		var unusualActivity = new byte[74];
		var contract = Encoding.ASCII.GetBytes("AAPL_260821C123.45");
		unusualActivity[0] = checked((byte)contract.Length);
		contract.CopyTo(unusualActivity, 1);
		unusualActivity[22] = 3;

		var events = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(unusualActivity, Hex(OptionQuoteHex)));

		AreEqual(1, events.Count);
		AreEqual(IntrinioDecodedEventTypes.OptionQuote, events[0].Type);
	}

	[TestMethod]
	public void RejectsMalformedEquityBatches()
	{
		ExpectInvalidEquity([]);
		ExpectInvalidEquity([1]);
		ExpectInvalidEquity([0, 0]);
		ExpectInvalidEquity([1, 3, 2]);

		var wrongLength = Hex(EquityTradeHex);
		wrongLength[1]--;
		ExpectInvalidEquity(Batch(wrongLength));

		var invalidSymbolLength = Hex(EquityTradeHex);
		invalidSymbolLength[2] = 30;
		ExpectInvalidEquity(Batch(invalidSymbolLength));

		var nonAsciiSymbol = Hex(EquityTradeHex);
		nonAsciiSymbol[3] = 0x80;
		ExpectInvalidEquity(Batch(nonAsciiSymbol));

	}

	[TestMethod]
	public void RejectsMalformedOptionsBatches()
	{
		ExpectInvalidOptions([]);
		ExpectInvalidOptions([1]);
		ExpectInvalidOptions([0, 0]);

		var truncated = Hex(OptionTradeHex)[..^1];
		ExpectInvalidOptions(Batch(truncated));

		var invalidContractLength = Hex(OptionQuoteHex);
		invalidContractLength[0] = 22;
		ExpectInvalidOptions(Batch(invalidContractLength));

		var malformedContract = Hex(OptionQuoteHex);
		malformedContract[5] = (byte)'X';
		ExpectInvalidOptions(Batch(malformedContract));

		var invalidPriceType = Hex(OptionQuoteHex);
		invalidPriceType[23] = 16;
		ExpectInvalidOptions(Batch(invalidPriceType));

		var unknownType = Hex(OptionQuoteHex);
		unknownType[22] = 7;
		ExpectInvalidOptions(Batch(unknownType));
	}

	private void ExpectInvalidEquity(byte[] bytes)
		=> ThrowsExactly<InvalidDataException>(() =>
			IntrinioRealtimeProtocol.DecodeEquity(bytes));

	private void ExpectInvalidOptions(byte[] bytes)
		=> ThrowsExactly<InvalidDataException>(() =>
			IntrinioRealtimeProtocol.DecodeOptions(bytes));

	private void AreBytesEqual(string expectedHex, byte[] actual)
		=> IsTrue(Hex(expectedHex).SequenceEqual(actual));

	private static byte[] Batch(params byte[][] chunks)
	{
		var result = new byte[1 + chunks.Sum(chunk => chunk.Length)];
		result[0] = checked((byte)chunks.Length);
		var offset = 1;
		foreach (var chunk in chunks)
		{
			chunk.CopyTo(result, offset);
			offset += chunk.Length;
		}
		return result;
	}

	private static byte[] Hex(string value)
		=> Convert.FromHexString(value);

	private static void SetOptionContract(byte[] chunk, string contract)
	{
		Array.Clear(chunk, 1, 21);
		var bytes = Encoding.ASCII.GetBytes(contract);
		chunk[0] = checked((byte)bytes.Length);
		bytes.CopyTo(chunk, 1);
	}

	private static DateTime Utc(long seconds, long ticks)
		=> DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(ticks);

	[TestMethod]
	public void DeserializesSnakeCaseAndNormalizesTimestampsToUtc()
	{
		const string json =
			"""{"last_price":12.5,"last_time":"2026-07-29T10:11:12-04:00","ignored":true}""";

		var value = IntrinioRestClient.Deserialize<IntrinioRealtimeStockPrice>(json);

		AreEqual(12.5m, value.LastPrice);
		AreEqual(new DateTime(2026, 7, 29, 14, 11, 12, DateTimeKind.Utc),
			value.LastTime);
	}

	[TestMethod]
	public void BuildsEscapedQueryFromProtocolNames()
	{
		using var client = new IntrinioRestClient(
			new("https://api-v2.intrinio.com/"), "a+ /?");
		var uri = client.BuildUri("securities/AAPL", new IntrinioQuoteRequest
		{
			IsActiveOnly = true,
			Source = "delayed sip",
		});

		AreEqual(
			"https://api-v2.intrinio.com/securities/AAPL?api_key=a%2B%20%2F%3F&active_only=true&source=delayed%20sip",
			uri.AbsoluteUri);
	}

	[TestMethod]
	public void RejectsMalformedJson()
		=> ThrowsExactly<JsonException>(() =>
			IntrinioRestClient.Deserialize<IntrinioRealtimeStockPrice>("{"));

	[TestMethod]
	public void IndexesSubscriptionsByFeedAndTransaction()
	{
		var index = new IntrinioSubscriptionIndex();
		var firstEquity = Create(1, "AAPL", false);
		var secondEquity = Create(2, "aapl", false);
		var option = Create(3, "AAPL", true);

		IsTrue(index.TryAdd(firstEquity, out var isFirst));
		IsTrue(isFirst);
		IsTrue(index.TryAdd(secondEquity, out isFirst));
		IsTrue(!isFirst);
		IsTrue(index.TryAdd(option, out isFirst));
		IsTrue(isFirst);
		IsTrue(!index.TryAdd(Create(1, "MSFT", false), out _));

		AreEqual(2, index.Match(false, "AaPl").Length);
		AreEqual(1, index.Match(true, "aapl").Length);
		AreEqual(0, index.Match(false, "MSFT").Length);
		IsTrue(index.TryGet(1, out var found, out var isLast));
		AreEqual(firstEquity, found);
		IsTrue(!isLast);
		IsTrue(index.TryGet(3, out found, out isLast));
		AreEqual(option, found);
		IsTrue(isLast);

		IsTrue(index.TryRemove(1, out var removed, out isLast));
		AreEqual(firstEquity, removed);
		IsTrue(!isLast);
		AreEqual(1, index.Match(false, "AAPL").Length);

		IsTrue(index.TryRemove(2, out removed, out isLast));
		AreEqual(secondEquity, removed);
		IsTrue(isLast);
		AreEqual(0, index.Match(false, "AAPL").Length);
	}

	[TestMethod]
	public async Task DeactivationWaitsForInFlightDelivery()
	{
		var subscription = Create(1, "AAPL", false);

		IsTrue(subscription.TryEnterDelivery());
		var deactivation = subscription.DeactivateAsync();
		IsTrue(!deactivation.IsCompleted);

		subscription.ExitDelivery();
		await deactivation;

		IsTrue(!subscription.TryEnterDelivery());
		subscription.Activate();
		IsTrue(subscription.TryEnterDelivery());
		subscription.ExitDelivery();
	}

	private static IntrinioStreamSubscription Create(long transactionId,
		string symbol, bool isOption)
		=> new()
		{
			TransactionId = transactionId,
			Symbol = symbol,
			IsOption = isOption,
		};
}
