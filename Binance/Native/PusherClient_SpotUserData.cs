namespace StockSharp.Binance.Native;

using System.IO;
using System.Net.WebSockets;
using System.Text;

partial class PusherClient
{
	public async Task SubscribeSpotAccountAsync(HttpClient httpClient, CancellationToken cancellationToken)
	{
		var key = (BinanceSections.Spot, (string)null);
		SpotUserDataClient client;
		using (_authClients.EnterScope())
		{
			ObjectDisposedException.ThrowIf(IsDisposeStarted, this);
			if (_authClients.ContainsKey(key))
				throw new InvalidOperationException("The Binance Spot account stream is already connected.");
			client = new(this, httpClient);
			_authClients.Add(key, client);
		}

		try
		{
			await client.Connect(cancellationToken);
			ObjectDisposedException.ThrowIf(client.IsDisposeStarted, client);
		}
		catch
		{
			using (_authClients.EnterScope())
				if (_authClients.TryGetValue(key, out var current) && ReferenceEquals(current, client))
					_authClients.Remove(key);
			client.Dispose();
			throw;
		}
	}

	private sealed class SpotUserDataClient : AuthPusherClient
	{
		private readonly HttpClient _httpClient;
		private ClientWebSocket _socket;
		private long _requestId;
		private long _subscriptionId;

		public SpotUserDataClient(PusherClient parent, HttpClient httpClient)
			: base(parent, BinanceSections.Spot, null, "ws-api/v3", ((BinanceMessageAdapter)parent.Parent).IsDemo
				? "wss://ws-api.testnet.binance.vision" : "wss://ws-api.binance.com")
		{
			_httpClient = httpClient;
			Socket.InitAsync += (socket, _) =>
			{
				if (IsDisposeStarted)
				{
					socket.Dispose();
					throw new ObjectDisposedException(nameof(SpotUserDataClient));
				}
				_socket = socket;
				return default;
			};
			// Complete the signed handshake before the receive loop starts on each new socket.
			Socket.PostConnect += SubscribeAsync;
		}

		private async ValueTask SubscribeAsync(bool _, CancellationToken cancellationToken)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(15));
			var socket = _socket;
			try
			{
				var id = ++_requestId;
				var request = new
				{
					id,
					method = "userDataStream.subscribe.signature",
					@params = _httpClient.CreateSpotSubscriptionParameters(),
				};
				// Send bytes so the API key and signature stay out of the WebSocket log.
				await Socket.SendAsync(request.ToJson().UTF8(), WebSocketMessageType.Text, timeout.Token);

				var earlyEvents = new List<JObject>();
				var reply = await ReadMessageAsync(socket, timeout.Token);
				while (reply["event"] is JObject)
				{
					if (earlyEvents.Count == 32)
						throw new InvalidDataException("Binance Spot sent too many events before confirming its subscription.");
					earlyEvents.Add(reply);
					reply = await ReadMessageAsync(socket, timeout.Token);
				}

				var status = reply.Value<int?>("status");
				if (status != 200)
				{
					var code = (reply["error"] as JObject)?.Value<int?>("code");
					throw new InvalidOperationException($"Binance Spot account subscription failed (status {status}, code {code}).");
				}
				if (reply.Value<long?>("id") != id || (reply["result"] as JObject)?.Value<long?>("subscriptionId") is not long subscriptionId || subscriptionId < 0)
					throw new InvalidDataException("Binance Spot did not confirm the account subscription.");

				_subscriptionId = subscriptionId;
				foreach (var message in earlyEvents)
					await ProcessEventAsync(message, cancellationToken);
			}
			catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
			{
				socket.Dispose();
				throw new TimeoutException("Timed out waiting for the Binance Spot account subscription.", ex);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		private static async ValueTask<JObject> ReadMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
		{
			using var body = new MemoryStream();
			var buffer = new byte[4096];
			ValueWebSocketReceiveResult frame;
			do
			{
				frame = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
				if (frame.MessageType != WebSocketMessageType.Text || body.Length + frame.Count > 1024 * 1024)
					throw new InvalidDataException("Binance Spot returned an invalid subscription frame.");
				body.Write(buffer, 0, frame.Count);
			}
			while (!frame.EndOfMessage);

			return JObject.Parse(Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length));
		}

		protected override ValueTask OnParse(WebSocketMessage msg, CancellationToken cancellationToken)
			=> ProcessEventAsync(msg.AsObject<JObject>(), cancellationToken);

		private ValueTask ProcessEventAsync(JObject message, CancellationToken cancellationToken)
		{
			if (message["event"] is not JObject data)
				return default;

			var type = data.Value<string>("e");
			var isCurrentSubscription = message.Value<long?>("subscriptionId") == _subscriptionId;
			if (type == "serverShutdown" || (isCurrentSubscription && type == "eventStreamTerminated"))
			{
				_socket.Abort();
				return default;
			}

			return isCurrentSubscription ? ProcessAccountEventAsync(data, cancellationToken) : default;
		}
	}
}
