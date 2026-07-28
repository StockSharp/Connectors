namespace StockSharp.Settrade.Native;

sealed class SettradeMqttClient : IAsyncDisposable
{
	private readonly Uri _endpoint;
	private readonly string _authorization;
	private readonly ClientWebSocket _socket = new();
	private readonly CancellationTokenSource _source = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly List<byte> _incoming = [];
	private readonly string _clientId = "stocksharp-" +
		Guid.NewGuid().ToString("N");
	private Task _reader;
	private Task _pinger;
	private ushort _packetId;
	private int _faulted;

	public SettradeMqttClient(string host, string path, string tokenType,
		string token)
	{
		host = host.ThrowIfEmpty(nameof(host)).Trim();
		path = path.ThrowIfEmpty(nameof(path));
		var baseUri = new Uri(host.Contains("://",
			StringComparison.Ordinal) ? host : "wss://" + host);
		_endpoint = new UriBuilder(baseUri)
		{
			Scheme = baseUri.Scheme.Equals("ws",
				StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
			Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
			Path = path,
			Query = string.Empty,
		}.Uri;
		_authorization =
			$"{tokenType.ThrowIfEmpty(nameof(tokenType))} " +
			token.ThrowIfEmpty(nameof(token));
	}

	public bool IsConnected
		=> Volatile.Read(ref _faulted) == 0 &&
			_socket.State == WebSocketState.Open;

	public event Func<string, byte[], CancellationToken, ValueTask>
		MessageReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		_socket.Options.AddSubProtocol("mqtt");
		_socket.Options.SetRequestHeader("Authorization",
			_authorization);
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		await SendPacketAsync(0x10, CreateConnectPayload(),
			cancellationToken);
		var (header, payload) = await ReadPacketAsync(cancellationToken);
		if ((header >> 4) != 2 || payload.Length != 2 ||
			payload[1] != 0)
			throw new InvalidOperationException(
				$"Settrade MQTT connection was rejected " +
				$"({(payload.Length > 1 ? payload[1] : -1)}).");
		Volatile.Write(ref _faulted, 0);
		_reader = ReadLoopAsync(_source.Token);
		_pinger = PingLoopAsync(_source.Token);
	}

	public async ValueTask SubscribeAsync(string topic,
		CancellationToken cancellationToken)
	{
		using var payload = new MemoryStream();
		var packetId = NextPacketId();
		payload.WriteByte((byte)(packetId >> 8));
		payload.WriteByte((byte)packetId);
		WriteString(payload, topic.ThrowIfEmpty(nameof(topic)));
		payload.WriteByte(0);
		await SendPacketAsync(0x82, payload.ToArray(),
			cancellationToken);
	}

	public async ValueTask UnsubscribeAsync(string topic,
		CancellationToken cancellationToken)
	{
		using var payload = new MemoryStream();
		var packetId = NextPacketId();
		payload.WriteByte((byte)(packetId >> 8));
		payload.WriteByte((byte)packetId);
		WriteString(payload, topic.ThrowIfEmpty(nameof(topic)));
		await SendPacketAsync(0xa2, payload.ToArray(),
			cancellationToken);
	}

	private ushort NextPacketId()
	{
		var value = unchecked(++_packetId);
		if (value == 0)
			value = ++_packetId;
		return value;
	}

	private byte[] CreateConnectPayload()
	{
		using var payload = new MemoryStream();
		WriteString(payload, "MQTT");
		payload.WriteByte(4);
		payload.WriteByte(2);
		payload.WriteByte(0);
		payload.WriteByte(30);
		WriteString(payload, _clientId);
		return payload.ToArray();
	}

	private async Task ReadLoopAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var (header, payload) = await ReadPacketAsync(
					cancellationToken);
				if ((header >> 4) != 3)
					continue;
				var offset = 0;
				var topic = ReadString(payload, ref offset);
				var qos = (header >> 1) & 3;
				ushort packetId = 0;
				if (qos > 0)
				{
					if (offset + 2 > payload.Length)
						throw new InvalidDataException(
							"Settrade MQTT publish packet is truncated.");
					packetId = (ushort)((payload[offset] << 8) |
						payload[offset + 1]);
					offset += 2;
				}
				if (MessageReceived is not null)
					await MessageReceived(topic, payload[offset..],
						cancellationToken);
				if (qos == 1)
					await SendPacketAsync(0x40,
						[(byte)(packetId >> 8), (byte)packetId],
						cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			Volatile.Write(ref _faulted, 1);
			if (Error is not null)
				await Error(error, CancellationToken.None);
		}
	}

	private async Task PingLoopAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await TimeSpan.FromSeconds(20).Delay(cancellationToken);
				await SendPacketAsync(0xc0, [], cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			Volatile.Write(ref _faulted, 1);
			if (Error is not null)
				await Error(error, CancellationToken.None);
		}
	}

	private async ValueTask SendPacketAsync(byte header, byte[] payload,
		CancellationToken cancellationToken)
	{
		using var packet = new MemoryStream();
		packet.WriteByte(header);
		WriteRemainingLength(packet, payload.Length);
		packet.Write(payload);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await _socket.SendAsync(packet.ToArray(),
				WebSocketMessageType.Binary, true, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask<(byte Header, byte[] Payload)> ReadPacketAsync(
		CancellationToken cancellationToken)
	{
		while (true)
		{
			if (TryTakePacket(out var header, out var payload))
				return (header, payload);
			var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
			try
			{
				var result = await _socket.ReceiveAsync(
					buffer.AsMemory(), cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
					throw new EndOfStreamException(
						"Settrade MQTT WebSocket was closed.");
				if (result.MessageType != WebSocketMessageType.Binary)
					continue;
				for (var index = 0; index < result.Count; index++)
					_incoming.Add(buffer[index]);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	private bool TryTakePacket(out byte header, out byte[] payload)
	{
		header = 0;
		payload = null;
		if (_incoming.Count < 2)
			return false;
		var multiplier = 1;
		var length = 0;
		var lengthBytes = 0;
		for (var index = 1; index < _incoming.Count && index <= 4;
			index++)
		{
			var value = _incoming[index];
			length += (value & 0x7f) * multiplier;
			lengthBytes++;
			if ((value & 0x80) == 0)
			{
				var start = 1 + lengthBytes;
				if (_incoming.Count < start + length)
					return false;
				header = _incoming[0];
				payload = _incoming.GetRange(start, length).ToArray();
				_incoming.RemoveRange(0, start + length);
				return true;
			}
			multiplier *= 128;
		}
		if (lengthBytes >= 4)
			throw new InvalidDataException(
				"Settrade MQTT remaining length is invalid.");
		return false;
	}

	private static void WriteRemainingLength(Stream stream, int length)
	{
		do
		{
			var value = length % 128;
			length /= 128;
			if (length > 0)
				value |= 0x80;
			stream.WriteByte((byte)value);
		}
		while (length > 0);
	}

	private static void WriteString(Stream stream, string value)
	{
		var bytes = Encoding.UTF8.GetBytes(value);
		if (bytes.Length > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value));
		stream.WriteByte((byte)(bytes.Length >> 8));
		stream.WriteByte((byte)bytes.Length);
		stream.Write(bytes);
	}

	private static string ReadString(byte[] data, ref int offset)
	{
		if (offset + 2 > data.Length)
			throw new InvalidDataException(
				"Settrade MQTT string is truncated.");
		var length = (data[offset] << 8) | data[offset + 1];
		offset += 2;
		if (offset + length > data.Length)
			throw new InvalidDataException(
				"Settrade MQTT string is truncated.");
		var value = Encoding.UTF8.GetString(data, offset, length);
		offset += length;
		return value;
	}

	public async ValueTask DisposeAsync()
	{
		Volatile.Write(ref _faulted, 1);
		_source.Cancel();
		try
		{
			if (_socket.State == WebSocketState.Open)
			{
				await SendPacketAsync(0xe0, [], CancellationToken.None);
				await _socket.CloseAsync(
					WebSocketCloseStatus.NormalClosure,
					"Disconnect", CancellationToken.None);
			}
		}
		catch
		{
		}
		try
		{
			if (_reader is not null)
				await _reader;
			if (_pinger is not null)
				await _pinger;
		}
		catch
		{
		}
		_socket.Dispose();
		_source.Dispose();
		_sendSync.Dispose();
	}
}
