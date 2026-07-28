namespace StockSharp.Buda.Native;

sealed class BudaAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public BudaAuthenticator(
		SecureString key,
		SecureString secret)
	{
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string Key => _key;

	public string Sign(
		string method,
		string pathAndQuery,
		string body,
		string nonce)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Buda.com API key and secret are required.");
		return Sign(
			_secret, method, pathAndQuery, body, nonce);
	}

	internal static string Sign(
		string secret,
		string method,
		string pathAndQuery,
		string body,
		string nonce)
	{
		var components = new List<string>
		{
			method.ThrowIfEmpty(nameof(method)).ToUpperInvariant(),
			pathAndQuery.ThrowIfEmpty(nameof(pathAndQuery)),
		};
		if (!body.IsEmpty())
			components.Add(Convert.ToBase64String(
				Encoding.UTF8.GetBytes(body)));
		components.Add(nonce.ThrowIfEmpty(nameof(nonce)));
		using var hash = new HMACSHA384(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hash.ComputeHash(
			Encoding.UTF8.GetBytes(components.Join(" "))))
			.ToLowerInvariant();
	}
}
