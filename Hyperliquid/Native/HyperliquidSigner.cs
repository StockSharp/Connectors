namespace StockSharp.Hyperliquid.Native;

using System.Buffers.Binary;
using System.Globalization;

using Nethereum.Signer;
using Nethereum.Util;

sealed class HyperliquidSigner
{
	private static readonly byte[] _domainTypeHash = Keccak("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)");
	private static readonly byte[] _agentTypeHash = Keccak("Agent(string source,bytes32 connectionId)");
	private static readonly byte[] _domainNameHash = Keccak("Exchange");
	private static readonly byte[] _domainVersionHash = Keccak("1");
	private static readonly byte[] _chainId = ToUInt256Bytes(1337);
	private static readonly byte[] _zeroAddressPadded = new byte[32];

	private readonly EthECKey _key;
	private readonly bool _isMainnet;

	public HyperliquidSigner(string privateKey, bool isMainnet)
	{
		if (privateKey.IsEmpty())
			throw new ArgumentNullException(nameof(privateKey));

		_key = new EthECKey(privateKey.Trim());
		_isMainnet = isMainnet;
	}

	public string Address => _key.GetPublicAddress().ToLowerInvariant();

	public L1Signature SignL1Action(JObject action, string vaultAddress, long nonce, long? expiresAfter)
	{
		if (action is null)
			throw new ArgumentNullException(nameof(action));

		var actionHash = ComputeActionHash(action, vaultAddress, nonce, expiresAfter);
		var digest = ComputeAgentTypedDataHash(actionHash, _isMainnet);
		var signed = _key.SignAndCalculateV(digest);

		var v = signed.V is { Length: > 0 } ? signed.V[^1] : (byte)27;
		return new(ToRpcHex(signed.R), ToRpcHex(signed.S), v);
	}

	private static byte[] ComputeActionHash(JObject action, string vaultAddress, long nonce, long? expiresAfter)
	{
		var actionBytes = MsgPackWriter.Serialize(action);
		var nonceBytes = ToUInt64Bytes(unchecked((ulong)nonce));

		var size = actionBytes.Length + nonceBytes.Length + 1;

		if (!vaultAddress.IsEmpty())
			size += 20;

		if (expiresAfter is not null)
			size += 1 + sizeof(ulong);

		var data = new byte[size];
		var offset = 0;

		Buffer.BlockCopy(actionBytes, 0, data, offset, actionBytes.Length);
		offset += actionBytes.Length;

		Buffer.BlockCopy(nonceBytes, 0, data, offset, nonceBytes.Length);
		offset += nonceBytes.Length;

		if (vaultAddress.IsEmpty())
		{
			data[offset++] = 0x00;
		}
		else
		{
			data[offset++] = 0x01;
			var addressBytes = ParseAddress(vaultAddress);
			Buffer.BlockCopy(addressBytes, 0, data, offset, addressBytes.Length);
			offset += addressBytes.Length;
		}

		if (expiresAfter is not null)
		{
			data[offset++] = 0x00;
			var expiresBytes = ToUInt64Bytes(unchecked((ulong)expiresAfter.Value));
			Buffer.BlockCopy(expiresBytes, 0, data, offset, expiresBytes.Length);
		}

		return Keccak(data);
	}

	private static byte[] ComputeAgentTypedDataHash(byte[] connectionId, bool isMainnet)
	{
		if (connectionId is null || connectionId.Length != 32)
			throw new ArgumentOutOfRangeException(nameof(connectionId));

		var domainSeparator = Keccak(Concat(
			_domainTypeHash,
			_domainNameHash,
			_domainVersionHash,
			_chainId,
			_zeroAddressPadded));

		var sourceHash = Keccak(isMainnet ? "a" : "b");

		var agentHash = Keccak(Concat(
			_agentTypeHash,
			sourceHash,
			connectionId));

		return Keccak(Concat(
			new byte[] { 0x19, 0x01 },
			domainSeparator,
			agentHash));
	}

	public static string ToWire(decimal value)
	{
		var rounded = decimal.Round(value, 8, MidpointRounding.ToEven);

		if (rounded != value)
			throw new InvalidOperationException($"Value '{value}' cannot be represented with 8 decimals without rounding.");

		if (rounded == 0m)
			return "0";

		var normalized = rounded.ToString("0.########", CultureInfo.InvariantCulture);
		return normalized == "-0" ? "0" : normalized;
	}

	private static byte[] ParseAddress(string address)
	{
		var raw = address.Trim();

		if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			raw = raw[2..];

		if (raw.Length != 40)
			throw new InvalidOperationException($"Address '{address}' is invalid.");

		var bytes = new byte[20];

		for (var i = 0; i < bytes.Length; i++)
		{
			var h = ParseHex(raw[i * 2]);
			var l = ParseHex(raw[i * 2 + 1]);
			bytes[i] = (byte)((h << 4) | l);
		}

		return bytes;
	}

	private static int ParseHex(char c)
		=> c switch
		{
			>= '0' and <= '9' => c - '0',
			>= 'a' and <= 'f' => c - 'a' + 10,
			>= 'A' and <= 'F' => c - 'A' + 10,
			_ => throw new FormatException($"Invalid hex character '{c}'."),
		};

	private static byte[] Keccak(string value)
		=> Keccak((value ?? string.Empty).UTF8());

	private static byte[] Keccak(byte[] value)
		=> Sha3Keccack.Current.CalculateHash(value ?? []);

	private static byte[] ToUInt64Bytes(ulong value)
	{
		Span<byte> bytes = stackalloc byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
		return bytes.ToArray();
	}

	private static byte[] ToUInt256Bytes(ulong value)
	{
		var bytes = new byte[32];
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24), value);
		return bytes;
	}

	private static byte[] Concat(params byte[][] chunks)
	{
		if (chunks is null || chunks.Length == 0)
			return [];

		var length = 0;

		foreach (var chunk in chunks)
			length += chunk?.Length ?? 0;

		var result = new byte[length];
		var offset = 0;

		foreach (var chunk in chunks)
		{
			if (chunk is null || chunk.Length == 0)
				continue;

			Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
			offset += chunk.Length;
		}

		return result;
	}

	private static string ToRpcHex(byte[] bytes)
	{
		if (bytes is null || bytes.Length == 0)
			return "0x0";

		var chars = new char[bytes.Length * 2];
		var offset = 0;

		for (var i = 0; i < bytes.Length; i++)
		{
			var b = bytes[i];
			chars[offset++] = ToHexChar(b >> 4);
			chars[offset++] = ToHexChar(b & 0x0f);
		}

		var normalized = new string(chars).TrimStart('0');
		return "0x" + (normalized.IsEmpty() ? "0" : normalized);
	}

	private static char ToHexChar(int value)
		=> (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
