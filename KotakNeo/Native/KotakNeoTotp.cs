namespace StockSharp.KotakNeo.Native;

static class KotakNeoTotp
{
	public static string Generate(string secret, DateTime utcNow)
	{
		secret.ThrowIfEmpty(nameof(secret));
		var key = DecodeBase32(secret);
		var counter = new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeSeconds() / 30;
		Span<byte> counterBytes = stackalloc byte[8];
		BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

		using var hmac = new HMACSHA1(key);
		var hash = hmac.ComputeHash(counterBytes.ToArray());
		var offset = hash[^1] & 0x0f;
		var code = ((hash[offset] & 0x7f) << 24)
			| (hash[offset + 1] << 16)
			| (hash[offset + 2] << 8)
			| hash[offset + 3];
		return (code % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
	}

	private static byte[] DecodeBase32(string value)
	{
		value = value.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.TrimEnd('=')
			.ToUpperInvariant();

		var output = new List<byte>(value.Length * 5 / 8);
		var buffer = 0;
		var bits = 0;
		foreach (var ch in value)
		{
			var digit = ch switch
			{
				>= 'A' and <= 'Z' => ch - 'A',
				>= '2' and <= '7' => ch - '2' + 26,
				_ => throw new FormatException("The TOTP secret contains an invalid Base32 character."),
			};

			buffer = (buffer << 5) | digit;
			bits += 5;
			if (bits < 8)
				continue;

			bits -= 8;
			output.Add((byte)(buffer >> bits));
			buffer &= (1 << bits) - 1;
		}

		return [.. output];
	}
}
