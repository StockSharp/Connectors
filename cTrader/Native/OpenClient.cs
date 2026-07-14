namespace StockSharp.cTrader.Native;

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Buffers;
using System.Reflection;

using Nito.AsyncEx;

class OpenClient : Disposable
{
	private readonly AsyncLock _writeLock = new();

	private TcpClient _tcpClient;
	private SslStream _sslStream;

    public OpenClient(EndPoint address)
		: this(address.GetHost(), (uint)address.GetPort())
    {
    }

    /// <summary>
    /// Creates an instance of OpenClient which is not connected yet
    /// </summary>
    /// <param name="host">The host name of API endpoint</param>
    /// <param name="port">The host port number</param>
    public OpenClient(string host, uint port)
	{
		Host = host.ThrowIfEmpty(nameof(host));
		Port = port;
	}

	/// <summary>
	/// The API endpoint host that the current client is connected to
	/// </summary>
	public string Host { get; }

	/// <summary>
	/// The API host port that current client is connected to
	/// </summary>
	public uint Port { get; }

	/// <summary>
	/// If client stream is completed without any error then it will return True, otherwise False
	/// </summary>
	public bool IsCompleted { get; private set; }

	/// <summary>
	/// If there was any error (exception) on client stream then this will return True, otherwise False
	/// </summary>
	public bool IsTerminated { get; private set; }

	/// <summary>
	/// This event will be triggered when a message is received from the API
	/// </summary>
	public event Action<ProtoMessage> MessageReceived;

	/// <summary>
	/// Connects to the API based on you specified method
	/// </summary>
	/// <exception cref="ObjectDisposedException">If client is disposed</exception>
	/// <returns>Task</returns>
	public Task Connect(CancellationToken cancellationToken)
	{
		ThrowObjectDisposedExceptionIfDisposed();

		return ConnectTcp(cancellationToken);
	}

	/// <summary>
	/// This method will insert your message on messages queue, it will not send the message instantly
	/// By using this overload of SendMessage method you avoid passing the message payload type
	/// and it gets the payload from message itself
	/// </summary>
	/// <typeparam name="T">Message Type</typeparam>
	/// <param name="message">Message</param>
	/// <param name="clientMsgId">The client message ID (optional)</param>
	/// <param name="cancellationToken"></param>
	/// <exception cref="InvalidOperationException">If getting message payload type fails</exception>
	/// <returns>Task</returns>
	public Task SendMessage<T>(T message, string clientMsgId, CancellationToken cancellationToken)
		where T : IMessage
	{
		var protoMessage = GetMessage(GetPayloadType(message), message, clientMsgId);

		return SendMessage(protoMessage, cancellationToken);
	}

	/// <summary>
	/// This method will insert your message on messages queue, it will not send the message instantly
	/// </summary>
	/// <typeparam name="T">Message Type</typeparam>
	/// <param name="message">Message</param>
	/// <param name="payloadType">Message Payload Type (ProtoPayloadType)</param>
	/// <param name="clientMsgId">The client message ID (optional)</param>
	/// <param name="cancellationToken"></param>
	/// <returns>Task</returns>
	public Task SendMessage<T>(T message, ProtoPayloadType payloadType, string clientMsgId, CancellationToken cancellationToken)
		where T : IMessage
	{
		var protoMessage = GetMessage((uint)payloadType, message, clientMsgId);

		return SendMessage(protoMessage, cancellationToken);
	}

	/// <summary>
	/// This method will insert your message on messages queue, it will not send the message instantly
	/// </summary>
	/// <typeparam name="T">Message Type</typeparam>
	/// <param name="message">Message</param>
	/// <param name="payloadType">Message Payload Type (ProtoOAPayloadType)</param>
	/// <param name="clientMsgId">The client message ID (optional)</param>
	/// <param name="cancellationToken"></param>
	/// <returns>Task</returns>
	public Task SendMessage<T>(T message, ProtoOAPayloadType payloadType, string clientMsgId, CancellationToken cancellationToken)
		where T : IMessage
	{
		var protoMessage = GetMessage((uint)payloadType, message, clientMsgId);

		return SendMessage(protoMessage, cancellationToken);
	}

	private static uint GetPayloadType<T>(T message)
		where T : IMessage
	{
		PropertyInfo property;

		try
		{
			property = message.GetType().GetProperty("PayloadType");
		}
		catch (Exception ex) when (ex is AmbiguousMatchException || ex is ArgumentNullException)
		{
			throw new InvalidOperationException($"Couldn't get the PayloadType of the message {message}", ex);
		}

		return property.GetValue(message).To<uint>();
	}

	private static ProtoMessage GetMessage(uint payloadType, IMessage input, string clientMessageId)
	{
		var message = new ProtoMessage
		{
			PayloadType = payloadType,
			Payload = input.ToByteString(),
		};

		if (!clientMessageId.IsEmpty())
		{
			message.ClientMsgId = clientMessageId;
		}

		return message;
	}

	/// <summary>
	/// This method will insert your message on messages queue, it will not send the message instantly
	/// </summary>
	/// <param name="message">Message</param>
	/// <param name="cancellationToken"></param>
	/// <returns>Task</returns>
	public Task SendMessage(ProtoMessage message, CancellationToken cancellationToken)
	{
		ThrowObjectDisposedExceptionIfDisposed();

		var messageByte = message.ToByteArray();

		return WriteTcp(messageByte, cancellationToken);
	}

	/// <summary>
	/// Disposes the client, releases all used resources, and stops all running operations
	/// </summary>
	protected override void DisposeManaged()
	{
		base.DisposeManaged();

		_sslStream?.Dispose();
		_tcpClient?.Dispose();

		if (!IsTerminated) OnCompleted();
	}

	/// <summary>
	/// Connects to API by using a TCP client
	/// </summary>
	/// <returns>Task</returns>
	private async Task ConnectTcp(CancellationToken cancellationToken)
	{
		_tcpClient = new() { LingerState = new(true, 10) };

		await _tcpClient.ConnectAsync(Host, (int)Port, cancellationToken);

		_sslStream = new(_tcpClient.GetStream(), false);

		await _sslStream.AuthenticateAsClientAsync(new() { TargetHost = Host }, cancellationToken: cancellationToken);

		_ = Task.Run(() => ReadTcp(cancellationToken), cancellationToken);
	}

	/// <summary>
	/// This method will read the TCP stream for incoming messages
	/// </summary>
	/// <param name="cancellationToken">The cancellation token that will be used on ReadAsync calls</param>
	/// <returns>Task</returns>
	private async Task ReadTcp(CancellationToken cancellationToken)
	{
		var dataLength = new byte[4];
		byte[] data = null;

		try
		{
			while (!IsDisposed)
			{
				var readBytes = 0;

				do
				{
					var count = dataLength.Length - readBytes;

					readBytes += await _sslStream.ReadAsync(dataLength.AsMemory(readBytes, count), cancellationToken).NoWait();

					if (readBytes == 0)
						throw new InvalidOperationException("Remote host closed the connection");
				}
				while (readBytes < dataLength.Length);

				var length = GetLength(dataLength);

				if (length <= 0)
					continue;

				data = ArrayPool<byte>.Shared.Rent(length);

				readBytes = 0;

				do
				{
					var count = length - readBytes;

					readBytes += await _sslStream.ReadAsync(data.AsMemory(readBytes, count), cancellationToken).NoWait();

					if (readBytes == 0)
						throw new InvalidOperationException("Remote host closed the connection");
				}
				while (readBytes < length);

				var message = ProtoMessage.Parser.ParseFrom(data, 0, length);

				ArrayPool<byte>.Shared.Return(data);

				MessageReceived?.Invoke(message);
			}
		}
		catch (Exception ex)
		{
			if (data is not null)
				ArrayPool<byte>.Shared.Return(data);

			OnError(ex);
		}
	}

	/// <summary>
	/// Returns the length of a received message without causing extra allocation
	/// </summary>
	/// <param name="lengthBytes">The byte array of received length data</param>
	/// <returns>int</returns>
	private static int GetLength(byte[] lengthBytes)
	{
		var lengthSpan = lengthBytes.AsSpan();

		lengthSpan.Reverse();

		return BitConverter.ToInt32(lengthSpan);
	}

	/// <summary>
	/// Writes the message bytes to TCP stream
	/// </summary>
	/// <param name="messageByte"></param>
	/// <param name="cancellationToken">The cancellation token that will be used on calling stream methods</param>
	/// <returns>Task</returns>
	private async Task WriteTcp(byte[] messageByte, CancellationToken cancellationToken)
	{
		var data = BitConverter.GetBytes(messageByte.Length).Reverse().Concat(messageByte).ToArray();

		using var _ = await _writeLock.LockAsync(cancellationToken);

		await _sslStream.WriteAsync(data, cancellationToken).NoWait();
		await _sslStream.FlushAsync(cancellationToken).NoWait();
	}

	/// <summary>
	/// Disposes the client and then calls each observer OnError after an exception thrown
	/// </summary>
	/// <param name="exception">Exception</param>
	private void OnError(Exception exception)
	{
		if (IsTerminated)
			return;

		IsTerminated = true;

		Dispose();
	}

	/// <summary>
	/// Completes each observer by calling their OnCompleted method
	/// </summary>
	private void OnCompleted()
	{
		IsCompleted = true;
	}

	/// <summary>
	/// Throws ObjectDisposedException if the client was disposed
	/// </summary>
	private void ThrowObjectDisposedExceptionIfDisposed()
	{
		if (IsDisposed)
			throw new ObjectDisposedException(nameof(OpenClient));
	}
}