namespace StockSharp.InteractiveBrokers.Native;

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

using Ecng.IO;

class IBSocket : BaseLogReceiver
{
	private static readonly byte[] _eol = { 0 };
	private static readonly string _infinity = "Infinity";
	private Stream _stream;
	private TcpClient _client;
	private readonly AllocationArray<byte> _sendBuffer = [];
	private readonly AllocationArray<byte> _readBuffer = new(FileSizes.KB);
	private int _readBufferOffset;

	public IBSocket(InteractiveBrokersMessageAdapter adapter)
	{
		Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		UseV100Plus = Adapter.UseV100Plus;
	}

	public InteractiveBrokersMessageAdapter Adapter { get; }

	public bool UseV100Plus { get; }

	// to get readable name after obfuscation
	public override string Name => nameof(IBSocket);

	public bool IsDisconnected { get; private set; }

	protected override void DisposeManaged()
	{
		base.DisposeManaged();

		IsDisconnected = true;

		if (_client == null)
			return;

		if (_stream != null)
		{
			_stream.Dispose();
			_stream = null;
		}

		_client.Close();
		_client = null;
	}

	/// <summary>
	/// Returns the version of the TWS instance the API application is connected to.
	/// </summary>
	public ServerVersions ServerVersion { get; private set; }

	public bool IsConnected => _client != null;

	private Stream SafeGetStream()
	{
		return _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	public async ValueTask ConnectAsync(EndPoint address, CancellationToken cancellationToken)
	{
		if (address == null)
			throw new ArgumentNullException(nameof(address));

		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		//this.AddInfoLog(nameof(Connect));

		_client = new TcpClient();
		await _client.ConnectAsync(((IPEndPoint)address).Address, ((IPEndPoint)address).Port, cancellationToken).NoWait();

		_stream = _client.GetStream();

		if (Adapter.SslProtocol != SslProtocols.None)
		{
			var host = Adapter.TargetHost;

			if (host.IsEmpty())
				host = address.GetHost();

			_stream = _stream.ToSsl(Adapter.SslProtocol, Adapter.CheckCertificateRevocation, Adapter.ValidateRemoteCertificates, host, Adapter.SslCertificate, Adapter.SslCertificatePassword);
		}

		if (UseV100Plus)
		{
			Send("API");
			var stream = SafeGetStream();
			await stream.WriteAsync(_sendBuffer.Buffer.AsMemory(0, _sendBuffer.Count), cancellationToken).NoWait();
			ClearSendBuffer();

			var version = $"v{(int)ServerVersions.V100}{' '}{(int)Adapter.MaxVersion}";
			this.AddVerboseLog("Send: {0}", version);
			Send(version.ASCII());

			await FlushSendBufferWithLengthAsync(stream, cancellationToken).NoWait();
		}
		else
		{
			Send((int)ServerVersions.V63);
		}

		if (UseV100Plus)
			await ReadBufferAsync(cancellationToken).NoWait();

		ServerVersion = (ServerVersions)await ReadIntAsync(cancellationToken).NoWait();

		const ServerVersions minimumServerVersion = ServerVersions.V38;

		if (ServerVersion < minimumServerVersion)
			throw new InvalidOperationException(LocalizedStrings.MinVersionInvalid.Put((int)ServerVersion, (int)minimumServerVersion));
	}

	public void ClearSendBuffer()
	{
		_sendBuffer.Count = 0;
	}

	public IBSocket Send(string str)
	{
		this.AddVerboseLog("Send: {0}", str);

		if (!str.IsEmpty())
			Send(str.UTF8());

		Send(_eol);
		return this;
	}

	public IBSocket Send(byte[] bytes)
	{
		if (UseV100Plus)
		{
			_sendBuffer.Add(bytes, 0, bytes.Length);
		}
		else
		{
			SafeGetStream().WriteRaw(bytes);
		}
		
		return this;
	}

	public IBSocket Send(int val)
	{
		return Send(val.To<string>());
	}

	public IBSocket Send(long val)
	{
		return Send(val.To<string>());
	}

	public IBSocket Send(decimal val)
	{
		return Send(val.To<string>());
	}

	public IBSocket Send(int? val)
	{
		return Send(val.To<string>());
	}

	public IBSocket Send(decimal? val)
	{
		return Send(val.To<string>());
	}

	public IBSocket Send(bool val)
	{
		return Send(val ? 1 : 0);
	}

	public IBSocket Send(bool? val)
	{
		return val != null ? Send(val.Value) : Send(string.Empty);
	}

	public IBSocket Send(DateTime? dto)
	{
		// https://groups.io/g/twsapi/message/49913
		return Send(dto?.ToString("yyyyMMdd-HH:mm:ss"));
	}

	public async ValueTask SendAsync(CancellationToken cancellationToken)
	{
		if (!UseV100Plus)
			return;

		var count = _sendBuffer.Count;
		if (count == 0)
			throw new InvalidOperationException("count == 0");

		var stream = SafeGetStream();
		await FlushSendBufferWithLengthAsync(stream, cancellationToken).NoWait();
	}

	private async ValueTask FlushSendBufferWithLengthAsync(Stream stream, CancellationToken cancellationToken)
	{
		var count = _sendBuffer.Count;
		if (count == 0)
			throw new InvalidOperationException("count == 0");

		var lenBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(count));
		lenBytes.AsSpan().Reverse();

		await stream.WriteAsync(lenBytes.AsMemory(), cancellationToken).NoWait();
		await stream.WriteAsync(_sendBuffer.Buffer.AsMemory(0, count), cancellationToken).NoWait();

		ClearSendBuffer();
	}

	public async ValueTask ReadBufferAsync(CancellationToken cancellationToken)
	{
		var stream = SafeGetStream();
		const int intSize = sizeof(int);
		var lenBuf = new byte[intSize];

		var read = await stream.ReadAsync(lenBuf.AsMemory(), cancellationToken).NoWait();

		if (read != intSize)
			throw new EndOfStreamException();

		var msgSize = IPAddress.NetworkToHostOrder(lenBuf.ChangeOrder(intSize, BitConverter.IsLittleEndian).To<int>());
		if (msgSize > 0x00FFFFFF)
			throw new InvalidOperationException();
		_readBuffer.Count = msgSize;

		var readMsg = await stream.ReadAsync(_readBuffer.Buffer.AsMemory(0, msgSize), cancellationToken).NoWait();

		if (readMsg != msgSize)
			throw new EndOfStreamException();

		_readBufferOffset = 0;
	}

	public async ValueTask<string> ReadStringAsync(CancellationToken cancellationToken)
	{
		var stream = SafeGetStream();

		var buf = new StringBuilder();
		
		while (true)
		{
			if (UseV100Plus)
			{
				var b = _readBuffer[_readBufferOffset];

				if (b == 0)
					break;

				buf.Append((char)b);
				_readBufferOffset++;
			}
			else
			{
				var one = new byte[1];

				var read = await stream.ReadAsync(one.AsMemory(), cancellationToken).NoWait();

				if (read != 1)
					throw new EndOfStreamException();

				if (one[0] == 0)
					break;

				buf.Append((char)one[0]);
			}
		}

		var str = buf.ToString();
		this.AddVerboseLog("Read: {0}", str);
		return str;
	}

	public async ValueTask<bool> ReadBoolAsync(CancellationToken cancellationToken)
	{
		var val = await ReadIntAsync(cancellationToken).NoWait();
		return val != 0;
	}

	public async ValueTask<int> ReadIntAsync(CancellationToken cancellationToken)
	{
		var v = await ReadNullIntAsync(cancellationToken).NoWait();
		return v ?? 0;
	}

	public async ValueTask<int?> ReadNullIntAsync(CancellationToken cancellationToken)
	{
		var str = await ReadStringAsync(cancellationToken).NoWait();
		return str.IsEmpty() ? null : str.To<int>();
	}

	public async ValueTask<long> ReadLongAsync(CancellationToken cancellationToken)
	{
		var str = await ReadStringAsync(cancellationToken).NoWait();
		return str.IsEmpty() ? 0 : str.To<long>();
	}

	public async ValueTask<decimal> ReadDecimalAsync(CancellationToken cancellationToken)
	{
		var v = await ReadNullDecimalAsync(cancellationToken).NoWait();
		return v ?? 0;
	}

	public async ValueTask<decimal?> ReadNullDecimalAsync(CancellationToken cancellationToken)
	{
		var str = await ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty() ||
			str.Equals("9223372036854775807") ||
			str.Equals("2147483647") ||
			str.Equals("1.7976931348623157E308") ||
			str == _infinity)
			return null;

		var value = str.To<double>();

		if ((value - double.MaxValue).Abs() < double.Epsilon)
			return null;

		return (decimal)value;
	}

	public async ValueTask<char> ReadCharAsync(CancellationToken cancellationToken)
	{
		var str = await ReadStringAsync(cancellationToken).NoWait();
		return str.IsEmpty() ? '\0' : str[0];
	}
}