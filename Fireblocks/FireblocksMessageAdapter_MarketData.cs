namespace StockSharp.Fireblocks;

public partial class FireblocksMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.Fireblocks))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		FireblocksAsset[] assets;
		if (!requestedCode.IsEmpty())
		{
			var asset = await RestClient.TryGetAssetAsync(requestedCode,
				cancellationToken);
			if (asset is null && IsKnownVaultAsset(requestedCode))
				asset = new()
				{
					LegacyId = requestedCode,
					DisplayName = requestedCode,
					DisplaySymbol = requestedCode,
				};
			assets = asset is null ? [] : [asset];
		}
		else
		{
			var count = lookupMsg.Count ?? SecurityLookupLimit;
			if (count <= 0)
				assets = [];
			else
			{
				var skip = Math.Max(0, lookupMsg.Skip ?? 0);
				var requested = Math.Min(SecurityLookupLimit,
					count >= int.MaxValue - skip
						? SecurityLookupLimit
						: checked((int)(count + skip)));
				assets = await RestClient.GetAssetsAsync(Math.Max(1, requested),
					cancellationToken);
			}
		}
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var toSkip = Math.Max(0, lookupMsg.Skip ?? 0);
		foreach (var asset in assets
			.Where(static asset => !asset.LegacyId.IsEmpty())
			.GroupBy(static asset => asset.LegacyId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static asset => asset.LegacyId,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = CreateSecurity(asset, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (toSkip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(FireblocksAsset asset,
		long originalTransactionId)
	{
		ArgumentNullException.ThrowIfNull(asset);
		var decimals = asset.Onchain?.Decimals ?? asset.Decimals;
		return new()
		{
			SecurityId = new()
			{
				SecurityCode = asset.LegacyId,
				BoardCode = BoardCodes.Fireblocks,
				Native = asset.Id,
			},
			Name = asset.DisplayName.IsEmpty()
				? asset.Onchain?.Name ?? asset.LegacyId
				: asset.DisplayName,
			ShortName = asset.DisplaySymbol.IsEmpty()
				? asset.Onchain?.Symbol ?? asset.LegacyId
				: asset.DisplaySymbol,
			SecurityType = asset.AssetClass == FireblocksAssetClasses.Fiat
				? SecurityTypes.Currency
				: SecurityTypes.CryptoCurrency,
			VolumeStep = GetQuantityStep(decimals),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static decimal? GetQuantityStep(int? decimals)
	{
		if (decimals is not int count || count is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < count; index++)
			step /= 10m;
		return step;
	}
}
