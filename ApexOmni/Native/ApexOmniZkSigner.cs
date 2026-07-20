using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace StockSharp.ApexOmni.Native;

sealed class ApexOmniZkSigner : IDisposable
{
	private static int _compatibilityValidated;
	private readonly Lock _sync = new();
	private nint _handle;

	public ApexOmniZkSigner(string seeds)
	{
		seeds = seeds.ThrowIfEmpty(nameof(seeds)).Trim();
		if (seeds.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			seeds = seeds[2..];
		byte[] seed;
		try
		{
			seed = Convert.FromHexString(seeds);
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"ApeX Omni seeds must be a hexadecimal string.",
				nameof(seeds), error);
		}
		if (seed.Length == 0)
			throw new ArgumentException("ApeX Omni seeds cannot be empty.",
				nameof(seeds));
		EnsureCompatible();
		var input = CreateSequenceBuffer(seed);
		var status = default(ApexOmniZkLinkNative.CallStatus);
		_handle = ApexOmniZkLinkNative.CreateSigner(input, ref status);
		CheckStatus(ref status, "create zkLink signer");
		if (_handle == nint.Zero)
			throw new InvalidOperationException(
				"The ApeX Omni zkLink SDK returned an empty signer.");
	}

	public string Sign(ApexOmniSignRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.AccountId.IsEmpty())
			throw new ArgumentException("ApeX Omni account ID is required.",
				nameof(request));
		if (!BigInteger.TryParse(request.AccountId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var rawAccountId) || rawAccountId < 0)
			throw new InvalidDataException(
				$"Invalid ApeX Omni account ID '{request.AccountId}'.");
		if (!ushort.TryParse(request.PairId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var pairId))
			throw new InvalidDataException(
				$"Invalid ApeX Omni L2 pair ID '{request.PairId}'.");
		if (request.Decimals is < 0 or > 28)
			throw new InvalidDataException(
				$"Unsupported ApeX Omni asset precision {request.Decimals}.");

		var clientHash = SHA256.HashData(Encoding.UTF8.GetBytes(
			request.ClientId.ThrowIfEmpty(nameof(request.ClientId))));
		var nonceValue = new BigInteger(clientHash, true, true);
		var uintMaximum = new BigInteger(uint.MaxValue);
		var slotValue = (nonceValue % ulong.MaxValue) / uintMaximum;
		if (slotValue > uint.MaxValue)
			throw new InvalidOperationException(
				"The ApeX Omni client ID produced an out-of-range slot ID; " +
				"submit the order with another transaction ID.");
		var accountId = (uint)(rawAccountId % uintMaximum);
		var slotId = (uint)slotValue;
		var nonce = (uint)(nonceValue % uintMaximum);
		var takerFee = ToFeeByte(request.TakerFeeRate, "taker fee rate");
		var makerFee = ToFeeByte(request.MakerFeeRate, "maker fee rate");
		var size = Scale(request.Size, request.Decimals);
		var price = Scale(request.Price, request.Decimals);

		var builder = CreateContractBuilder(accountId, slotId, nonce, pairId,
			size, price, request.IsBuy, takerFee, makerFee);
		using (_sync.EnterScope())
		{
			var handle = _handle != nint.Zero
				? _handle
				: throw new ObjectDisposedException(nameof(ApexOmniZkSigner));
			var status = default(ApexOmniZkLinkNative.CallStatus);
			var contract = ApexOmniZkLinkNative.CreateContract(builder,
				ref status);
			CheckStatus(ref status, "create contract payload");
			if (contract == nint.Zero)
				throw new InvalidOperationException(
					"The ApeX Omni zkLink SDK returned an empty contract.");
			try
			{
				status = default;
				var output = ApexOmniZkLinkNative.GetContractBytes(contract,
					ref status);
				CheckStatus(ref status, "encode contract payload");
				var contractBytes = ReadSequenceBuffer(output,
					"contract payload");
				var message = CreateSequenceBuffer(contractBytes);
				status = default;
				var signature = ApexOmniZkLinkNative.Sign(handle, message,
					ref status);
				CheckStatus(ref status, "sign contract payload");
				return ReadSignature(signature);
			}
			finally
			{
				status = default;
				ApexOmniZkLinkNative.FreeContract(contract, ref status);
				CheckStatus(ref status, "free contract payload");
			}
		}
	}

	public static string CalculateLimitFee(decimal size, decimal price,
		decimal takerFeeRate)
	{
		if (size <= 0 || price <= 0 || takerFeeRate < 0)
			throw new ArgumentOutOfRangeException(nameof(size),
				"Order size and price must be positive and fee cannot be negative.");
		var fee = checked(size * price * takerFeeRate);
		return (Math.Ceiling(fee * 1_000_000m) / 1_000_000m).ToWire();
	}

	private static byte ToFeeByte(decimal rate, string field)
	{
		if (rate < 0)
			throw new InvalidDataException(
				$"ApeX Omni returned a negative {field}.");
		var scaled = Math.Ceiling(rate * 10_000m);
		if (scaled > byte.MaxValue)
			throw new InvalidDataException(
				$"ApeX Omni {field} is out of range.");
		return (byte)scaled;
	}

	private static string Scale(decimal value, int decimals)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"ApeX Omni signed values must be positive.");
		var text = value.ToWire();
		var separator = text.IndexOf('.');
		var whole = separator < 0 ? text : text[..separator];
		var fraction = separator < 0 ? string.Empty : text[(separator + 1)..];
		if (fraction.Length > decimals)
			fraction = fraction[..decimals];
		else if (fraction.Length < decimals)
			fraction = fraction.PadRight(decimals, '0');
		var combined = (whole + fraction).TrimStart('0');
		return combined.IsEmpty() ? "0" : combined;
	}

	private static ApexOmniZkLinkNative.RustBuffer CreateContractBuilder(
		uint accountId, uint slotId, uint nonce, ushort pairId, string size,
		string price, bool isBuy, byte takerFee, byte makerFee)
	{
		using var stream = new MemoryStream();
		WriteUInt32(stream, accountId);
		stream.WriteByte(0);
		WriteUInt32(stream, slotId);
		WriteUInt32(stream, nonce);
		WriteUInt16(stream, pairId);
		WriteString(stream, size);
		WriteString(stream, price);
		stream.WriteByte(isBuy ? (byte)1 : (byte)0);
		stream.WriteByte(takerFee);
		stream.WriteByte(makerFee);
		stream.WriteByte(0);
		return CreateBuffer(stream.ToArray());
	}

	private static ApexOmniZkLinkNative.RustBuffer CreateSequenceBuffer(
		byte[] data)
	{
		using var stream = new MemoryStream();
		WriteInt32(stream, data.Length);
		stream.Write(data, 0, data.Length);
		return CreateBuffer(stream.ToArray());
	}

	private static ApexOmniZkLinkNative.RustBuffer CreateBuffer(byte[] data)
	{
		var status = default(ApexOmniZkLinkNative.CallStatus);
		var buffer = ApexOmniZkLinkNative.Allocate(data.Length, ref status);
		CheckStatus(ref status, "allocate native buffer");
		if (buffer.Data == nint.Zero || buffer.Capacity < data.Length)
		{
			FreeBuffer(buffer);
			throw new InvalidOperationException(
				"The ApeX Omni zkLink SDK failed to allocate a native buffer.");
		}
		Marshal.Copy(data, 0, buffer.Data, data.Length);
		buffer.Length = data.Length;
		return buffer;
	}

	private static byte[] ReadSequenceBuffer(
		ApexOmniZkLinkNative.RustBuffer buffer, string operation)
	{
		var data = CopyAndFree(buffer);
		if (data.Length < sizeof(int))
			throw new InvalidDataException(
				$"ApeX Omni zkLink {operation} is truncated.");
		var length = BinaryPrimitives.ReadInt32BigEndian(data);
		if (length < 0 || length != data.Length - sizeof(int))
			throw new InvalidDataException(
				$"ApeX Omni zkLink {operation} has an invalid length.");
		return data[sizeof(int)..];
	}

	private static string ReadSignature(
		ApexOmniZkLinkNative.RustBuffer buffer)
	{
		var data = CopyAndFree(buffer);
		var offset = 0;
		_ = ReadString(data, ref offset, "public key");
		var signature = ReadString(data, ref offset, "signature");
		if (offset != data.Length || signature.IsEmpty())
			throw new InvalidDataException(
				"ApeX Omni zkLink returned an invalid signature.");
		return signature;
	}

	private static string ReadString(byte[] data, ref int offset, string field)
	{
		if (offset > data.Length - sizeof(int))
			throw new InvalidDataException(
				$"ApeX Omni zkLink {field} is truncated.");
		var length = BinaryPrimitives.ReadInt32BigEndian(
			data.AsSpan(offset, sizeof(int)));
		offset += sizeof(int);
		if (length < 0 || offset > data.Length - length)
			throw new InvalidDataException(
				$"ApeX Omni zkLink {field} has an invalid length.");
		var value = Encoding.UTF8.GetString(data, offset, length);
		offset += length;
		return value;
	}

	private static byte[] CopyAndFree(ApexOmniZkLinkNative.RustBuffer buffer)
	{
		try
		{
			if (buffer.Length < 0 || buffer.Length > buffer.Capacity ||
				(buffer.Length > 0 && buffer.Data == nint.Zero))
				throw new InvalidDataException(
					"ApeX Omni zkLink returned an invalid native buffer.");
			var data = new byte[buffer.Length];
			if (data.Length > 0)
				Marshal.Copy(buffer.Data, data, 0, data.Length);
			return data;
		}
		finally
		{
			FreeBuffer(buffer);
		}
	}

	private static void FreeBuffer(ApexOmniZkLinkNative.RustBuffer buffer)
	{
		if (buffer.Data == nint.Zero && buffer.Capacity == 0)
			return;
		var status = default(ApexOmniZkLinkNative.CallStatus);
		ApexOmniZkLinkNative.Free(buffer, ref status);
		CheckStatus(ref status, "free native buffer");
	}

	private static void CheckStatus(ref ApexOmniZkLinkNative.CallStatus status,
		string operation)
	{
		if (status.Code == 0)
			return;
		var code = status.Code;
		var error = status.ErrorBuffer;
		status.ErrorBuffer = default;
		string message;
		try
		{
			var data = error.Data == nint.Zero && error.Capacity == 0
				? []
				: CopyAndFree(error);
			if (code == 1 && data.Length >= sizeof(int))
			{
				var offset = sizeof(int);
				var variant = BinaryPrimitives.ReadInt32BigEndian(data);
				message = $"signer error {variant}: " +
					ReadString(data, ref offset, "signer error");
			}
			else
				message = data.Length == 0
					? "unknown native error"
					: Encoding.UTF8.GetString(data);
		}
		catch (Exception errorParsingException)
		{
			message = "malformed native error: " +
				errorParsingException.Message;
		}
		throw new InvalidOperationException(
			$"Unable to {operation}: {message}");
	}

	private static void EnsureCompatible()
	{
		if (Volatile.Read(ref _compatibilityValidated) != 0)
			return;
		if (ApexOmniZkLinkNative.ContractConstructorChecksum() != 32968 ||
			ApexOmniZkLinkNative.ContractBytesChecksum() != 6953 ||
			ApexOmniZkLinkNative.SignerConstructorChecksum() != 47514 ||
			ApexOmniZkLinkNative.SignChecksum() != 46475)
			throw new InvalidOperationException(
				"The installed ApeX Omni zkLink native library is incompatible " +
				"with this connector.");
		Volatile.Write(ref _compatibilityValidated, 1);
	}

	private static void WriteInt32(Stream stream, int value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(int)];
		BinaryPrimitives.WriteInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt32(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt16(Stream stream, ushort value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(ushort)];
		BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteString(Stream stream, string value)
	{
		var data = Encoding.UTF8.GetBytes(value.ThrowIfEmpty(nameof(value)));
		WriteInt32(stream, data.Length);
		stream.Write(data, 0, data.Length);
	}

	public void Dispose()
	{
		using (_sync.EnterScope())
		{
			var handle = _handle;
			_handle = nint.Zero;
			if (handle == nint.Zero)
				return;
			var status = default(ApexOmniZkLinkNative.CallStatus);
			ApexOmniZkLinkNative.FreeSigner(handle, ref status);
			CheckStatus(ref status, "free zkLink signer");
		}
	}
}

sealed class ApexOmniSignRequest
{
	public string AccountId { get; init; }
	public string PairId { get; init; }
	public int Decimals { get; init; }
	public string ClientId { get; init; }
	public decimal Size { get; init; }
	public decimal Price { get; init; }
	public bool IsBuy { get; init; }
	public decimal TakerFeeRate { get; init; }
	public decimal MakerFeeRate { get; init; }
}
