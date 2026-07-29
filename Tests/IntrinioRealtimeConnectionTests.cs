namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Intrinio;
using StockSharp.Intrinio.Native;

[TestClass]
public class IntrinioRealtimeConnectionTests : BaseTestClass
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
}
