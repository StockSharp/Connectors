namespace StockSharp.AngelOne.Native;

static class AngelOneTotp
{
	public static string Generate(SecureString secret, DateTime utcNow)
	{
		var key = DecodeBase32(secret.ThrowIfEmpty(nameof(secret)).UnSecure());
		var counter = (long)(new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeSeconds() / 30);
		Span<byte> counterBytes = stackalloc byte[sizeof(long)];
		BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
		var hash = HMACSHA1.HashData(key, counterBytes);
		var offset = hash[^1] & 0x0f;
		var binary = ((hash[offset] & 0x7f) << 24) |
			(hash[offset + 1] << 16) |
			(hash[offset + 2] << 8) |
			hash[offset + 3];
		return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
	}

	private static byte[] DecodeBase32(string value)
	{
		value = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '=').Select(char.ToUpperInvariant).ToArray());
		if (value.IsEmpty())
			throw new FormatException("Angel One TOTP secret is empty.");

		var output = new List<byte>(value.Length * 5 / 8);
		var buffer = 0;
		var bits = 0;
		foreach (var symbol in value)
		{
			var digit = symbol switch
			{
				>= 'A' and <= 'Z' => symbol - 'A',
				>= '2' and <= '7' => symbol - '2' + 26,
				_ => throw new FormatException($"Invalid Base32 character '{symbol}' in Angel One TOTP secret."),
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
