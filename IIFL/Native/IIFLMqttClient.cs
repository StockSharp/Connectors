namespace StockSharp.IIFL.Native;

sealed class IIFLMqttClient : IAsyncDisposable
{
	internal const string MarketFeedPrefix =
		"prod/marketfeed/mw/v1/";
	internal const string OpenInterestPrefix =
		"prod/marketfeed/oi/v1/";
	internal const string OrderPrefix =
		"prod/updates/order/v1/";
	internal const string TradePrefix =
		"prod/updates/trade/v1/";

	private readonly string _host;
	private readonly int _port;
	private readonly string _token;
	private readonly string _userId;
	private readonly CancellationTokenSource _source = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private TcpClient _client;
	private SslStream _stream;
	private Task _reader;
	private Task _pinger;
	private ushort _packetId;
	private int _faulted;

	public IIFLMqttClient(string host, int port, string token)
	{
		_host = host.ThrowIfEmpty(nameof(host)).Trim();
		_port = port is > 0 and <= ushort.MaxValue
			? port
			: throw new ArgumentOutOfRangeException(nameof(port));
		_token = token.ThrowIfEmpty(nameof(token));
		_userId = IIFLRestClient.GetUserId(token)
			.ThrowIfEmpty("preferred_username");
	}

	public bool IsConnected
		=> Volatile.Read(ref _faulted) == 0 &&
			_client?.Connected == true && _stream is not null;

	public event Func<string, byte[], CancellationToken, ValueTask>
		MessageReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		_client = new();
		await _client.ConnectAsync(_host, _port, cancellationToken);
		_stream = new(_client.GetStream(), false);
		await _stream.AuthenticateAsClientAsync(_host);
		var clientId = _userId +
			DateTime.Now.ToString("ddMMyyHHmmss",
				CultureInfo.InvariantCulture);
		await SendPacketAsync(0x10,
			CreateConnectPayload(_userId, _token, clientId),
			cancellationToken);
		var (header, payload) = await ReadPacketAsync(cancellationToken);
		if ((header >> 4) != 2 || payload.Length != 2 ||
			payload[1] != 0)
			throw new InvalidOperationException(
				$"IIFL MQTT connection was rejected " +
					$"({(payload.Length > 1 ? payload[1] : -1)}).");
		Volatile.Write(ref _faulted, 0);
		_reader = ReadLoopAsync(_source.Token);
		_pinger = PingLoopAsync(_source.Token);
	}

	public ValueTask SubscribeMarketFeedAsync(string topic,
		CancellationToken cancellationToken)
		=> SubscribeAsync(BuildTopic(MarketFeedPrefix, topic),
			cancellationToken);

	public ValueTask UnsubscribeMarketFeedAsync(string topic,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(BuildTopic(MarketFeedPrefix, topic),
			cancellationToken);

	public ValueTask SubscribeOpenInterestAsync(string topic,
		CancellationToken cancellationToken)
		=> SubscribeAsync(BuildTopic(OpenInterestPrefix, topic),
			cancellationToken);

	public ValueTask UnsubscribeOpenInterestAsync(string topic,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(BuildTopic(OpenInterestPrefix, topic),
			cancellationToken);

	public ValueTask SubscribeOrdersAsync(
		CancellationToken cancellationToken)
		=> SubscribeAsync(BuildTopic(OrderPrefix,
			_userId.ToLowerInvariant()), cancellationToken);

	public ValueTask UnsubscribeOrdersAsync(
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(BuildTopic(OrderPrefix,
			_userId.ToLowerInvariant()), cancellationToken);

	public ValueTask SubscribeTradesAsync(
		CancellationToken cancellationToken)
		=> SubscribeAsync(BuildTopic(TradePrefix,
			_userId.ToLowerInvariant()), cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(BuildTopic(TradePrefix,
			_userId.ToLowerInvariant()), cancellationToken);

	internal static string BuildTopic(string prefix, string topic)
	{
		prefix.ThrowIfEmpty(nameof(prefix));
		topic = topic.ThrowIfEmpty(nameof(topic)).Trim().ToLowerInvariant();
		if (topic.Any(character =>
			!char.IsAsciiLetterOrDigit(character) && character != '/'))
			throw new ArgumentException(
				"IIFL MQTT topic can contain only lowercase ASCII " +
					"letters, digits, and slashes.",
				nameof(topic));
		return prefix + topic;
	}

	private async ValueTask SubscribeAsync(string topic,
		CancellationToken cancellationToken)
	{
		using var payload = new MemoryStream();
		var packetId = NextPacketId();
		payload.WriteByte((byte)(packetId >> 8));
		payload.WriteByte((byte)packetId);
		WriteString(payload, topic);
		payload.WriteByte(0);
		await SendPacketAsync(0x82, payload.ToArray(),
			cancellationToken);
	}

	private async ValueTask UnsubscribeAsync(string topic,
		CancellationToken cancellationToken)
	{
		using var payload = new MemoryStream();
		var packetId = NextPacketId();
		payload.WriteByte((byte)(packetId >> 8));
		payload.WriteByte((byte)packetId);
		WriteString(payload, topic);
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

	internal static byte[] CreateConnectPayload(string userId,
		string token, string clientId)
	{
		using var payload = new MemoryStream();
		WriteString(payload, "MQTT");
		payload.WriteByte(4);
		payload.WriteByte(0xc2);
		payload.WriteByte(0);
		payload.WriteByte(20);
		WriteString(payload,
			clientId.ThrowIfEmpty(nameof(clientId)));
		WriteString(payload, userId.ThrowIfEmpty(nameof(userId)));
		WriteString(payload,
			$"OPENID~~{token.ThrowIfEmpty(nameof(token))}~");
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
				if ((header >> 4) == 14)
					throw new EndOfStreamException(
						"IIFL MQTT server closed the session.");
				if ((header >> 4) != 3)
					continue;
				var offset = 0;
				var topic = ReadString(payload, ref offset);
				var quality = (header >> 1) & 3;
				ushort packetId = 0;
				if (quality > 0)
				{
					if (offset + 2 > payload.Length)
						throw new InvalidDataException(
							"IIFL MQTT publish packet is truncated.");
					packetId = (ushort)((payload[offset] << 8) |
						payload[offset + 1]);
					offset += 2;
				}
				if (MessageReceived is not null)
					await MessageReceived(topic, payload[offset..],
						cancellationToken);
				if (quality == 1)
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
				await TimeSpan.FromSeconds(10).Delay(cancellationToken);
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
		if (_stream is null)
			throw new InvalidOperationException(
				"IIFL MQTT client is not connected.");
		using var packet = new MemoryStream();
		packet.WriteByte(header);
		WriteRemainingLength(packet, payload.Length);
		packet.Write(payload);
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await _stream.WriteAsync(packet.ToArray(),
				cancellationToken);
			await _stream.FlushAsync(cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask<(byte Header, byte[] Payload)>
		ReadPacketAsync(CancellationToken cancellationToken)
	{
		var header = await ReadByteAsync(cancellationToken);
		var length = await ReadRemainingLengthAsync(cancellationToken);
		return (header,
			await ReadExactlyAsync(length, cancellationToken));
	}

	private async ValueTask<int> ReadRemainingLengthAsync(
		CancellationToken cancellationToken)
	{
		var multiplier = 1;
		var result = 0;
		for (var index = 0; index < 4; index++)
		{
			var value = await ReadByteAsync(cancellationToken);
			result += (value & 0x7f) * multiplier;
			if ((value & 0x80) == 0)
				return result;
			multiplier *= 128;
		}
		throw new InvalidDataException(
			"IIFL MQTT remaining length is invalid.");
	}

	private async ValueTask<byte> ReadByteAsync(
		CancellationToken cancellationToken)
	{
		var buffer = new byte[1];
		var read = await _stream.ReadAsync(buffer, cancellationToken);
		if (read == 0)
			throw new EndOfStreamException();
		return buffer[0];
	}

	private async ValueTask<byte[]> ReadExactlyAsync(int length,
		CancellationToken cancellationToken)
	{
		var result = new byte[length];
		var offset = 0;
		while (offset < length)
		{
			var read = await _stream.ReadAsync(
				result.AsMemory(offset, length - offset),
				cancellationToken);
			if (read == 0)
				throw new EndOfStreamException();
			offset += read;
		}
		return result;
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
				"IIFL MQTT string is truncated.");
		var length = (data[offset] << 8) | data[offset + 1];
		offset += 2;
		if (offset + length > data.Length)
			throw new InvalidDataException(
				"IIFL MQTT string is truncated.");
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
			if (_stream is not null)
				await SendPacketAsync(0xe0, [],
					CancellationToken.None);
		}
		catch
		{
		}
		_stream?.Dispose();
		_client?.Dispose();
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
		_source.Dispose();
		_sendSync.Dispose();
	}
}
