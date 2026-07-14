namespace StockSharp.Webull.Native;

using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

sealed class WebullMqttClient : IAsyncDisposable
{
	private readonly string _host;
	private readonly int _port;
	private readonly string _appKey;
	private readonly string _sessionId;
	private readonly CancellationTokenSource _source = new();
	private TcpClient _client;
	private SslStream _stream;
	private Task _reader;
	private Task _pinger;

	public WebullMqttClient(string host, int port, string appKey, string sessionId)
	{
		_host = host;
		_port = port;
		_appKey = appKey;
		_sessionId = sessionId;
	}

	public event Func<string, byte[], CancellationToken, ValueTask> MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		_client = new();
		await _client.ConnectAsync(_host, _port, cancellationToken);
		_stream = new(_client.GetStream(), false);
		await _stream.AuthenticateAsClientAsync(_host);
		await SendConnectAsync(cancellationToken);

		var header = await ReadByteAsync(cancellationToken);
		var length = await ReadRemainingLengthAsync(cancellationToken);
		var payload = await ReadExactlyAsync(length, cancellationToken);
		if (header != 0x20 || payload.Length != 2 || payload[1] != 0)
			throw new InvalidOperationException($"Webull MQTT connection was rejected ({(payload.Length > 1 ? payload[1] : -1)}).");

		_reader = ReadLoopAsync(_source.Token);
		_pinger = PingLoopAsync(_source.Token);
	}

	private async Task SendConnectAsync(CancellationToken cancellationToken)
	{
		using var payload = new MemoryStream();
		WriteString(payload, "MQTT");
		payload.WriteByte(4);
		payload.WriteByte(0xc2);
		payload.WriteByte(0);
		payload.WriteByte(60);
		WriteString(payload, _sessionId);
		WriteString(payload, _appKey);
		WriteString(payload, Guid.NewGuid().ToString("N"));
		await SendPacketAsync(0x10, payload.ToArray(), cancellationToken);
	}

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var header = await ReadByteAsync(cancellationToken);
				var length = await ReadRemainingLengthAsync(cancellationToken);
				var payload = await ReadExactlyAsync(length, cancellationToken);
				if ((header >> 4) != 3)
					continue;

				var offset = 0;
				var topic = ReadString(payload, ref offset);
				var qos = (header >> 1) & 3;
				ushort packetId = 0;
				if (qos > 0)
				{
					packetId = (ushort)((payload[offset] << 8) | payload[offset + 1]);
					offset += 2;
				}

				if (MessageReceived is not null)
					await MessageReceived(topic, payload[offset..], cancellationToken);
				if (qos == 1)
					await SendPacketAsync(0x40, [(byte)(packetId >> 8), (byte)packetId], cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			if (Error is not null)
				await Error(ex, cancellationToken);
		}
	}

	private async Task PingLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await TimeSpan.FromSeconds(30).Delay(cancellationToken);
				await SendPacketAsync(0xc0, [], cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			if (Error is not null)
				await Error(ex, cancellationToken);
		}
	}

	private async Task SendPacketAsync(byte header, byte[] payload, CancellationToken cancellationToken)
	{
		using var packet = new MemoryStream();
		packet.WriteByte(header);
		var length = payload.Length;
		do
		{
			var value = length % 128;
			length /= 128;
			if (length > 0)
				value |= 0x80;
			packet.WriteByte((byte)value);
		}
		while (length > 0);
		packet.Write(payload);
		await _stream.WriteAsync(packet.ToArray(), cancellationToken);
		await _stream.FlushAsync(cancellationToken);
	}

	private async Task<int> ReadRemainingLengthAsync(CancellationToken cancellationToken)
	{
		var multiplier = 1;
		var result = 0;
		for (var i = 0; i < 4; i++)
		{
			var value = await ReadByteAsync(cancellationToken);
			result += (value & 0x7f) * multiplier;
			if ((value & 0x80) == 0)
				return result;
			multiplier *= 128;
		}
		throw new FormatException("Invalid MQTT remaining length.");
	}

	private async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[1];
		var read = await _stream.ReadAsync(buffer, cancellationToken);
		if (read == 0)
			throw new EndOfStreamException();
		return buffer[0];
	}

	private async Task<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
	{
		var result = new byte[length];
		var offset = 0;
		while (offset < length)
		{
			var read = await _stream.ReadAsync(result.AsMemory(offset, length - offset), cancellationToken);
			if (read == 0)
				throw new EndOfStreamException();
			offset += read;
		}
		return result;
	}

	private static void WriteString(Stream stream, string value)
	{
		var bytes = value.UTF8();
		stream.WriteByte((byte)(bytes.Length >> 8));
		stream.WriteByte((byte)bytes.Length);
		stream.Write(bytes);
	}

	private static string ReadString(byte[] data, ref int offset)
	{
		if (offset + 2 > data.Length)
			throw new FormatException("Invalid MQTT string.");
		var length = (data[offset] << 8) | data[offset + 1];
		offset += 2;
		if (offset + length > data.Length)
			throw new FormatException("Invalid MQTT string length.");
		var result = data.UTF8(offset, length);
		offset += length;
		return result;
	}

	public async ValueTask DisposeAsync()
	{
		_source.Cancel();
		try
		{
			if (_stream is not null)
				await SendPacketAsync(0xe0, [], CancellationToken.None);
		}
		catch
		{
		}
		_stream?.Dispose();
		_client?.Dispose();
		_source.Dispose();
	}
}
