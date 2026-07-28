namespace StockSharp.CoinSpot.Native;

sealed class CoinSpotAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public CoinSpotAuthenticator(
		SecureString key,
		SecureString secret)
	{
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string Key
		=> _key;

	public string Sign(string body)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"CoinSpot API key and secret are required.");
		return Sign(_secret, body);
	}

	internal static string Sign(string secret, string body)
	{
		using var hash = new HMACSHA512(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hash.ComputeHash(
			Encoding.UTF8.GetBytes(body ?? string.Empty)))
			.ToLowerInvariant();
	}
}
