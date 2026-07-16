namespace StockSharp.SierraChartDtc.Native;

using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

internal sealed class DtcClient : BaseLogReceiver
{
	private readonly EndPoint _address;
	private readonly SslProtocols _sslProtocol;
	private readonly bool _isCertificateValidation;
	private readonly string _targetHost;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private TcpClient _tcpClient;
	private Stream _stream;
	private CancellationTokenSource _receiveCts;
	private Task _receiveTask;
	private Task _heartbeatTask;
	private TaskCompletionSource<DtcEncodingResponse> _encodingCompletion;
	private TaskCompletionSource<DtcLogonResponse> _logonCompletion;
	private TimeSpan _heartbeatInterval;
	private long _lastReceiveTicks;
	private int _errorRaised;

	public DtcClient(EndPoint address, SslProtocols sslProtocol,
		bool isCertificateValidation, string targetHost)
	{
		_address = address ?? throw new ArgumentNullException(nameof(address));
		_sslProtocol = sslProtocol;
		_isCertificateValidation = isCertificateValidation;
		_targetHost = targetHost;
	}

	public override string Name => nameof(DtcClient);

	public event Func<DtcMessage, CancellationToken, ValueTask> MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task<DtcLogonResponse> Connect(string login, string password,
		string tradeAccount, TimeSpan heartbeatInterval, TimeSpan transmissionInterval,
		CancellationToken cancellationToken)
	{
		if (_tcpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var heartbeatSeconds = (int)Math.Ceiling(heartbeatInterval.TotalSeconds);
		if (heartbeatSeconds is < 5 or > 60)
			throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));

		var host = _address.GetHost();
		var port = _address.GetPort();
		if (host.IsEmpty() || port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
			throw new ArgumentException("The DTC endpoint must contain a valid host and port.", nameof(_address));

		_tcpClient = new() { NoDelay = true };
		await _tcpClient.ConnectAsync(host, port, cancellationToken);
		_stream = _tcpClient.GetStream();

		if (_sslProtocol != SslProtocols.None)
		{
			RemoteCertificateValidationCallback validation = _isCertificateValidation
				? null
				: static (_, _, _, _) => true;
			var ssl = new SslStream(_stream, false, validation);
			await ssl.AuthenticateAsClientAsync(new()
			{
				TargetHost = _targetHost.IsEmpty(host),
				EnabledSslProtocols = _sslProtocol,
			}, cancellationToken);
			_stream = ssl;
		}

		_heartbeatInterval = heartbeatInterval;
		_encodingCompletion = NewCompletion<DtcEncodingResponse>();
		_logonCompletion = NewCompletion<DtcLogonResponse>();
		_receiveCts = new();
		Volatile.Write(ref _lastReceiveTicks, DateTime.UtcNow.Ticks);
		_receiveTask = ReceiveLoop(_receiveCts.Token);

		await Send(new DtcEncodingRequest(), cancellationToken);
		var encoding = await _encodingCompletion.Task.WaitAsync(cancellationToken);
		if (encoding.ProtocolVersion <= 0 || encoding.Encoding != DtcEncodings.BinaryWithVariableLengthStrings ||
			!encoding.ProtocolType.IsEmpty() && !encoding.ProtocolType.EqualsIgnoreCase("DTC"))
		{
			throw new InvalidDataException(
				$"The server rejected DTC binary VLS encoding (version={encoding.ProtocolVersion}, encoding={encoding.Encoding}, protocol={encoding.ProtocolType}).");
		}

		const DtcSierraLogonFlags modernMessageFlags =
			DtcSierraLogonFlags.SupportUnbundledTrades |
			DtcSierraLogonFlags.UseMarketDepthUpdatesWithMilliseconds |
			DtcSierraLogonFlags.SupportMarketDepthSnapshotFloat |
			DtcSierraLogonFlags.SupportMillisecondOrderTimestamps |
			DtcSierraLogonFlags.SupportTradeUpdatesWithMicroseconds |
			DtcSierraLogonFlags.SupportBidAskUpdatesWithMicroseconds;
		await Send(new DtcLogonRequest
		{
			UserName = login,
			Password = password,
			TradeAccount = tradeAccount,
			HeartbeatIntervalSeconds = heartbeatSeconds,
			ClientName = "StockSharp",
			Integer1 = (int)modernMessageFlags,
			MarketDataTransmissionInterval = transmissionInterval <= TimeSpan.Zero
				? 0
				: Math.Max(1, (int)Math.Min(int.MaxValue, transmissionInterval.TotalMilliseconds)),
		}, cancellationToken);

		var logon = await _logonCompletion.Task.WaitAsync(cancellationToken);
		if (logon.Result != DtcLogonStatuses.Success)
			throw new InvalidOperationException(logon.ResultText.IsEmpty($"DTC logon failed with status {logon.Result}."));

		_heartbeatTask = HeartbeatLoop(_receiveCts.Token);
		return logon;
	}

	public async Task Send(DtcMessage message, CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var data = DtcProtocol.Encode(message);
		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			await stream.WriteAsync(data, cancellationToken);
			await stream.FlushAsync(cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public async Task Disconnect(CancellationToken cancellationToken)
	{
		if (_tcpClient == null)
			return;
		try
		{
			if (_stream != null)
				await Send(new DtcLogoff { Reason = "Client disconnect" }, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
		}
		finally
		{
			CloseConnection();
		}
	}

	private async Task ReceiveLoop(CancellationToken cancellationToken)
	{
		try
		{
			var header = new byte[4];
			while (!cancellationToken.IsCancellationRequested)
			{
				await ReadExactly(header, cancellationToken);
				var size = BinaryPrimitives.ReadUInt16LittleEndian(header);
				if (size < header.Length)
					throw new InvalidDataException($"The DTC server sent invalid message size {size}.");

				var data = new byte[size];
				header.CopyTo(data, 0);
				if (size > header.Length)
					await ReadExactly(data.AsMemory(header.Length), cancellationToken);

				Volatile.Write(ref _lastReceiveTicks, DateTime.UtcNow.Ticks);
				var message = DtcProtocol.Decode(data);
				if (message is DtcEncodingResponse encoding)
					_encodingCompletion?.TrySetResult(encoding);
				else if (message is DtcLogonResponse logon)
					_logonCompletion?.TrySetResult(logon);
				else if (message is DtcHeartbeat)
					continue;
				else if (message is DtcLogoff logoff)
					throw new IOException(logoff.Reason.IsEmpty("The DTC server closed the connection."));
				else if (MessageReceived != null)
					await MessageReceived(message, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_encodingCompletion?.TrySetException(ex);
			_logonCompletion?.TrySetException(ex);
			await RaiseError(ex);
		}
	}

	private async Task HeartbeatLoop(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(_heartbeatInterval, cancellationToken);
				var lastReceive = new DateTime(Volatile.Read(ref _lastReceiveTicks), DateTimeKind.Utc);
				if (DateTime.UtcNow - lastReceive > _heartbeatInterval + _heartbeatInterval + _heartbeatInterval)
					throw new TimeoutException("The DTC server did not send data or a heartbeat within three heartbeat intervals.");
				await Send(new DtcHeartbeat { CurrentTime = DateTime.UtcNow }, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			await RaiseError(ex);
		}
	}

	private async Task ReadExactly(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
			if (read == 0)
				throw new EndOfStreamException("The DTC server closed the network stream.");
			offset += read;
		}
	}

	private Task ReadExactly(byte[] buffer, CancellationToken cancellationToken)
		=> ReadExactly(buffer.AsMemory(), cancellationToken);

	private async Task RaiseError(Exception error)
	{
		if (Interlocked.Exchange(ref _errorRaised, 1) != 0)
			return;
		if (Error != null)
			await Error(error, CancellationToken.None);
	}

	private static TaskCompletionSource<T> NewCompletion<T>()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private void CloseConnection()
	{
		_receiveCts?.Cancel();
		_receiveCts?.Dispose();
		_receiveCts = null;
		_stream?.Dispose();
		_stream = null;
		_tcpClient?.Dispose();
		_tcpClient = null;
		_receiveTask = null;
		_heartbeatTask = null;
		_encodingCompletion?.TrySetCanceled();
		_logonCompletion?.TrySetCanceled();
		_encodingCompletion = null;
		_logonCompletion = null;
	}

	protected override void DisposeManaged()
	{
		CloseConnection();
		_writeLock.Dispose();
		base.DisposeManaged();
	}
}
