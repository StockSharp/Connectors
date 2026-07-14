namespace StockSharp.Hyperliquid.Native;

class ExchangeClient : BaseLogReceiver
{
	private readonly Uri _exchangeEndpoint;

	public ExchangeClient(string exchangeEndpoint)
	{
		if (exchangeEndpoint.IsEmpty())
			throw new ArgumentNullException(nameof(exchangeEndpoint));

		_exchangeEndpoint = exchangeEndpoint.To<Uri>();
	}

	public override string Name => nameof(Hyperliquid) + "_" + nameof(ExchangeClient);

	public Task<JObject> PostActionAsync(JObject action, L1Signature signature, long nonce, string vaultAddress, long? expiresAfter, CancellationToken cancellationToken)
	{
		if (action is null)
			throw new ArgumentNullException(nameof(action));

		var payload = new JObject
		{
			["action"] = action,
			["nonce"] = nonce,
			["signature"] = new JObject
			{
				["r"] = signature.R,
				["s"] = signature.S,
				["v"] = signature.V,
			},
		};

		if (!vaultAddress.IsEmpty())
			payload["vaultAddress"] = vaultAddress.ToLowerInvariant();

		if (expiresAfter is not null)
			payload["expiresAfter"] = expiresAfter.Value;

		var request = new RestRequest((string)null, Method.Post);
		request.AddStringBody(payload.ToString(Formatting.None), DataFormat.Json);

		return request.InvokeAsync<JObject>(_exchangeEndpoint, this, this.AddVerboseLog, cancellationToken);
	}
}
