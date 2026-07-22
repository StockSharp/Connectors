namespace StockSharp.Glassnode;

public partial class GlassnodeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		_ = PriceTimeFrame.ToInterval();

		var client = new GlassnodeRestClient(ApiEndpoint, Token, RequestInterval)
		{
			Parent = this,
		};
		_rest = client;
		try
		{
			var assets = await client.GetAssetsAsync(cancellationToken);
			CacheAssets(assets);
			if (GetAssets().Length == 0)
				throw new InvalidDataException(
					"Glassnode returned an empty asset catalogue.");
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(message, cancellationToken);
	}

	private GlassnodeRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		_rest?.Dispose();
		_rest = null;
		using (_sync.EnterScope())
			_assets.Clear();
	}

	private void CacheAssets(IEnumerable<GlassnodeAsset> assets)
	{
		using (_sync.EnterScope())
			foreach (var asset in assets.Where(IsValidAsset))
				_assets[asset.Id] = asset;
	}

	private GlassnodeAsset[] GetAssets()
	{
		using (_sync.EnterScope())
			return [.. _assets.Values];
	}

	private GlassnodeAsset ResolveAsset(SecurityId securityId)
	{
		var native = securityId.Native as string;
		var code = native.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode)).Trim();
		var separator = code.IndexOf('/');
		if (separator > 0)
			code = code[..separator];
		code = GlassnodeExtensions.NormalizeAssetId(code);

		using (_sync.EnterScope())
		{
			if (_assets.TryGetValue(code, out var exact))
				return exact;
			var matches = _assets.Values.Where(asset =>
				asset.Symbol.EqualsIgnoreCase(code)).Take(2).ToArray();
			if (matches.Length == 1)
				return matches[0];
		}
		throw new InvalidOperationException(
			$"Glassnode asset '{code}' is unknown or ambiguous. Use security lookup to preserve its API ID.");
	}

	private static bool IsValidAsset(GlassnodeAsset asset)
		=> asset is not null && !asset.Id.IsEmpty() &&
			!asset.Symbol.IsEmpty() && !asset.Name.IsEmpty();
}
