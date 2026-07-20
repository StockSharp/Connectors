namespace StockSharp.SunIo.Native;

static class SunIoAbiEncoder
{
	private const int _wordLength = 32;
	private const int _swapHeadWords = 8;

	public static string FunctionSelector(string signature)
	{
		var hash = Sha3Keccack.Current.CalculateHash(
			Encoding.ASCII.GetBytes(signature.ThrowIfEmpty(nameof(signature))));
		return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
	}

	public static string EncodeSwap(SunIoRoute route, BigInteger amountIn,
		BigInteger amountOutMinimum, string recipient, long deadline)
	{
		ArgumentNullException.ThrowIfNull(route);
		if (amountIn <= 0)
			throw new ArgumentOutOfRangeException(nameof(amountIn));
		if (amountOutMinimum <= 0)
			throw new ArgumentOutOfRangeException(nameof(amountOutMinimum));
		if (deadline <= 0)
			throw new ArgumentOutOfRangeException(nameof(deadline));
		var tokens = route.Tokens ?? [];
		var versions = route.PoolVersions ?? [];
		var poolFees = route.PoolFees ?? [];
		if (tokens.Length < 2 || versions.Length != tokens.Length - 1 ||
			poolFees.Length < versions.Length)
			throw new InvalidDataException(
				"SUN.io returned an inconsistent routing path.");

		var path = EncodeAddressArray(tokens);
		var versionNames = versions.Select(static value => value.ToWire())
			.ToArray();
		var poolVersions = EncodeStringArray(versionNames);
		var versionLengths = EncodeUnsignedArray(
			CreateVersionLengths(versions));
		var fees = new BigInteger[tokens.Length];
		for (var index = 0; index < versions.Length; index++)
		{
			if (versions[index] != SunIoPoolVersions.V3)
				continue;
			var fee = poolFees[index].ParseInteger("route pool fee");
			if (fee > 0xFFFFFF)
				throw new InvalidDataException(
					"SUN.io returned a pool fee outside uint24.");
			fees[index] = fee;
		}
		var encodedFees = EncodeUnsignedArray(fees);

		var headLength = _swapHeadWords * _wordLength;
		var pathOffset = headLength;
		var versionsOffset = pathOffset + path.Length;
		var versionLengthsOffset = versionsOffset + poolVersions.Length;
		var feesOffset = versionLengthsOffset + versionLengths.Length;
		using var stream = new MemoryStream();
		WriteWord(stream, pathOffset);
		WriteWord(stream, versionsOffset);
		WriteWord(stream, versionLengthsOffset);
		WriteWord(stream, feesOffset);
		WriteWord(stream, amountIn);
		WriteWord(stream, amountOutMinimum);
		WriteAddressWord(stream, recipient);
		WriteWord(stream, deadline);
		stream.Write(path);
		stream.Write(poolVersions);
		stream.Write(versionLengths);
		stream.Write(encodedFees);
		return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
	}

	public static string EncodeAddressParameter(string address)
	{
		using var stream = new MemoryStream();
		WriteAddressWord(stream, address);
		return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
	}

	public static BigInteger DecodeLastUnsignedArrayValue(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if ((value.Length & 1) != 0 ||
			value.Any(static character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(
				"TRON returned invalid ABI output data.");
		var bytes = value.HexToByteArray();
		var offset = ReadWordAsInt(bytes, 0);
		var count = ReadWordAsInt(bytes, offset);
		if (count <= 0)
			throw new InvalidDataException(
				"TRON returned an empty swap output array.");
		var lastOffset = checked(offset + _wordLength +
			(count - 1) * _wordLength);
		if (lastOffset < 0 || lastOffset + _wordLength > bytes.Length)
			throw new InvalidDataException(
				"TRON returned truncated ABI output data.");
		return new BigInteger(bytes.AsSpan(lastOffset, _wordLength), true, true);
	}

	private static BigInteger[] CreateVersionLengths(
		SunIoPoolVersions[] versions)
	{
		var result = new List<BigInteger>();
		for (var index = 0; index < versions.Length;)
		{
			var end = index + 1;
			while (end < versions.Length && versions[end] == versions[index])
				end++;
			var length = end - index;
			result.Add(result.Count == 0 ? length + 1 : length);
			index = end;
		}
		return [.. result];
	}

	private static byte[] EncodeAddressArray(string[] values)
	{
		using var stream = new MemoryStream();
		WriteWord(stream, values.Length);
		foreach (var value in values)
			WriteAddressWord(stream, value);
		return stream.ToArray();
	}

	private static byte[] EncodeUnsignedArray(BigInteger[] values)
	{
		using var stream = new MemoryStream();
		WriteWord(stream, values.Length);
		foreach (var value in values)
			WriteWord(stream, value);
		return stream.ToArray();
	}

	private static byte[] EncodeStringArray(string[] values)
	{
		var encoded = values.Select(EncodeString).ToArray();
		using var stream = new MemoryStream();
		WriteWord(stream, values.Length);
		var offset = values.Length * _wordLength;
		foreach (var value in encoded)
		{
			WriteWord(stream, offset);
			offset += value.Length;
		}
		foreach (var value in encoded)
			stream.Write(value);
		return stream.ToArray();
	}

	private static byte[] EncodeString(string value)
	{
		var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
		var paddedLength = (bytes.Length + _wordLength - 1) /
			_wordLength * _wordLength;
		using var stream = new MemoryStream();
		WriteWord(stream, bytes.Length);
		stream.Write(bytes);
		if (paddedLength > bytes.Length)
			stream.Write(new byte[paddedLength - bytes.Length]);
		return stream.ToArray();
	}

	private static void WriteAddressWord(Stream stream, string address)
	{
		var bytes = SunIoSigner.ToAbiAddress(address);
		stream.Write(new byte[12]);
		stream.Write(bytes);
	}

	private static void WriteWord(Stream stream, long value)
		=> WriteWord(stream, new BigInteger(value));

	private static void WriteWord(Stream stream, BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var bytes = value.ToByteArray(true, true);
		if (bytes.Length > _wordLength)
			throw new OverflowException("SUN.io ABI integer exceeds uint256.");
		stream.Write(new byte[_wordLength - bytes.Length]);
		stream.Write(bytes);
	}

	private static int ReadWordAsInt(byte[] source, int offset)
	{
		if (source is null || offset < 0 ||
			offset + _wordLength > source.Length)
			throw new InvalidDataException(
				"TRON returned truncated ABI output data.");
		var value = new BigInteger(source.AsSpan(offset, _wordLength),
			true, true);
		if (value > int.MaxValue)
			throw new InvalidDataException(
				"TRON returned an oversized ABI array.");
		return (int)value;
	}
}
