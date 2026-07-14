namespace StockSharp.Webull.Native;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Grpc.Core;
using Grpc.Net.Client;

sealed record WebullTradeEvent(WebullEventTypes EventType, WebullSubscribeTypes SubscribeType, string ContentType, string Payload, string RequestId, long Timestamp);

sealed class WebullTradeEventsClient : IAsyncDisposable
{
	private static readonly Marshaller<byte[]> _requestMarshaller = Marshallers.Create(
		(value, context) => context.Complete(value),
		context => context.PayloadAsNewBuffer());
	private static readonly Marshaller<WebullTradeEvent> _responseMarshaller = Marshallers.Create(
		(value, context) => throw new NotSupportedException(),
		context => Deserialize(context.PayloadAsNewBuffer()));
	private static readonly Method<byte[], WebullTradeEvent> _subscribeMethod = new(
		MethodType.ServerStreaming,
		"grpc.trade.event.EventService",
		"Subscribe",
		_requestMarshaller,
		_responseMarshaller);

	private readonly string _address;
	private readonly string _appKey;
	private readonly string _appSecret;
	private readonly string[] _accounts;
	private readonly CancellationTokenSource _source = new();
	private GrpcChannel _channel;
	private Task _reader;

	public WebullTradeEventsClient(string address, string appKey, string appSecret, IEnumerable<string> accounts)
	{
		_address = address;
		_appKey = appKey;
		_appSecret = appSecret;
		_accounts = [.. accounts];
	}

	public event Func<WebullTradeEvent, CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public void Start()
	{
		_channel = GrpcChannel.ForAddress(_address);
		_reader = ReadLoopAsync(_source.Token);
	}

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			var request = SerializeRequest(_accounts);
			var (signature, values) = WebullSigner.SignEvent(request, _appKey, _appSecret);
			var metadata = new Metadata();
			foreach (var pair in values)
				metadata.Add(pair.Key, pair.Value);
			metadata.Add("x-signature", signature);

			using var call = _channel.CreateCallInvoker().AsyncServerStreamingCall(
				_subscribeMethod,
				null,
				new CallOptions(metadata, cancellationToken: cancellationToken),
				request);

			while (await call.ResponseStream.MoveNext(cancellationToken))
			{
				var response = call.ResponseStream.Current;
				switch (response.EventType)
				{
					case WebullEventTypes.AuthError:
					case WebullEventTypes.NumOfConnExceed:
					case WebullEventTypes.SubscribeExpired:
						throw new InvalidOperationException($"Webull trade event stream returned status {response.EventType}: {response.Payload}");
					case WebullEventTypes.Ping:
						break;
					default:
						if (EventReceived is not null)
							await EventReceived(response, cancellationToken);
						break;
				}
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

	private static byte[] SerializeRequest(IEnumerable<string> accounts)
	{
		using var stream = new MemoryStream();
		WriteVarIntField(stream, 1, (ulong)WebullSubscribeTypes.Trade);
		WriteVarIntField(stream, 2, (ulong)DateTime.UtcNow.ToUnix(false));
		foreach (var account in accounts)
			WriteStringField(stream, 5, account);
		return stream.ToArray();
	}

	private static WebullTradeEvent Deserialize(byte[] payload)
	{
		var reader = new WebullProtoDecoder.ProtoReader(payload);
		var eventType = WebullEventTypes.SubscribeSuccess;
		var subscribeType = WebullSubscribeTypes.Trade;
		long timestamp = 0;
		string contentType = null, value = null, requestId = null;
		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: eventType = (WebullEventTypes)checked((int)reader.ReadVarInt()); break;
				case 2: subscribeType = (WebullSubscribeTypes)checked((uint)reader.ReadVarInt()); break;
				case 3: contentType = reader.ReadString(wire); break;
				case 4: value = reader.ReadString(wire); break;
				case 5: requestId = reader.ReadString(wire); break;
				case 6: timestamp = checked((long)reader.ReadVarInt()); break;
				default: reader.Skip(wire); break;
			}
		}
		return new(eventType, subscribeType, contentType, value, requestId, timestamp);
	}

	private static void WriteVarIntField(Stream stream, int field, ulong value)
	{
		WriteVarInt(stream, (ulong)(field << 3));
		WriteVarInt(stream, value);
	}

	private static void WriteStringField(Stream stream, int field, string value)
	{
		var bytes = value.UTF8();
		WriteVarInt(stream, (ulong)((field << 3) | 2));
		WriteVarInt(stream, (ulong)bytes.Length);
		stream.Write(bytes);
	}

	private static void WriteVarInt(Stream stream, ulong value)
	{
		do
		{
			var current = (byte)(value & 0x7f);
			value >>= 7;
			if (value != 0)
				current |= 0x80;
			stream.WriteByte(current);
		}
		while (value != 0);
	}

	public ValueTask DisposeAsync()
	{
		_source.Cancel();
		_channel?.Dispose();
		_source.Dispose();
		return ValueTask.CompletedTask;
	}
}
