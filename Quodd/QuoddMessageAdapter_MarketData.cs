namespace StockSharp.Quodd;

public partial class QuoddMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name)?.Trim();
		if (value.IsEmpty() && lookupMsg.UnderlyingSecurityId != default)
		{
			value = (lookupMsg.UnderlyingSecurityId.Native as string)
				.IsEmpty(lookupMsg.UnderlyingSecurityId.SecurityCode)?.Trim();
		}

		if (value.IsEmpty())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var matchLookup = lookupMsg;
		async ValueTask Emit(SecurityMessage security)
		{
			if (security == null || left <= 0 || !security.IsMatch(matchLookup, securityTypes) ||
				!sent.Add($"{security.SecurityId.BoardCode}:{security.SecurityId.Native}"))
				return;
			if (skip > 0)
			{
				skip--;
				return;
			}
			if (lookupMsg.OnlySecurityId)
			{
				security = new()
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = security.SecurityId,
				};
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		var optionsRequested = lookupMsg.SecurityId.BoardCode
			.EqualsIgnoreCase(QuoddExtensions.OptionsBoardCode) ||
			lookupMsg.SecurityType == SecurityTypes.Option || lookupMsg.OptionType != null ||
			lookupMsg.Strike != null || lookupMsg.ExpiryDate != null ||
			lookupMsg.UnderlyingSecurityId != default ||
			securityTypes.Count > 0 && securityTypes.All(type => type == SecurityTypes.Option);

		if (optionsRequested)
		{
			var underlying = (lookupMsg.UnderlyingSecurityId.Native as string)
				.IsEmpty(lookupMsg.UnderlyingSecurityId.SecurityCode).IsEmpty(value)?.Trim();
			var isExactOptionTicker = lookupMsg.UnderlyingSecurityId == default &&
				lookupMsg.OptionType == null && lookupMsg.Strike == null &&
				lookupMsg.ExpiryDate == null && value.Any(char.IsDigit);
			if (isExactOptionTicker)
			{
				foreach (var snap in await SafeClient().GetSnaps([value],
					QuoddAssetTypes.Options, cancellationToken))
				{
					await Emit(snap.ToSecurityMessage(QuoddAssetTypes.Options, null,
						lookupMsg.TransactionId));
				}
			}
			else
			{
				matchLookup = (SecurityLookupMessage)lookupMsg.Clone();
				matchLookup.SecurityId = default;
				matchLookup.Name = null;
				matchLookup.ShortName = null;
				if (matchLookup.UnderlyingSecurityId == default)
				{
					matchLookup.UnderlyingSecurityId = new()
					{
						SecurityCode = underlying,
						BoardCode = QuoddExtensions.BoardCode,
						Native = underlying,
					};
				}

				foreach (var option in await SafeClient().GetOptionTickers(underlying,
					lookupMsg.OptionType, lookupMsg.ExpiryDate, cancellationToken))
				{
					var security = option.ToSecurityMessage(lookupMsg.TransactionId);
					if (!lookupMsg.IncludeExpired && security.ExpiryDate?.Date < DateTime.UtcNow.Date)
						continue;
					await Emit(security);
					if (left <= 0)
						break;
				}
			}
		}
		else
		{
			var snaps = await SafeClient().GetSnaps([value], QuoddAssetTypes.Equities,
				cancellationToken);
			TickerInfoMessage info = null;
			if (IsTickerInfoEnabled)
			{
				var response = await SafeClient().GetTickerInfo([value], cancellationToken);
				info = response.Data.FirstOrDefault(item => item.Ticker.EqualsIgnoreCase(value));
				foreach (var error in response.Errors)
				{
					if (!error.Error.IsEmpty() || !error.Message.IsEmpty())
						this.AddWarningLog($"QUODD Ticker Info '{error.Ticker}': " +
							error.Error.IsEmpty(error.Message));
				}
			}

			foreach (var snap in snaps)
				await Emit(snap.ToSecurityMessage(QuoddAssetTypes.Equities, info,
					lookupMsg.TransactionId));
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			RemoveLiveSubscription(mdMsg.OriginalTransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException("QUODD Snap does not expose historical Level1 events.");

		var assetType = mdMsg.SecurityId.GetQuoddAssetType();
		var ticker = mdMsg.SecurityId.GetQuoddTicker();
		var securityId = mdMsg.SecurityId.NormalizeQuodd(ticker, assetType);
		var remaining = mdMsg.Count;
		var snap = (await SafeClient().GetSnaps([ticker], assetType, cancellationToken))
			.FirstOrDefault(item => item.Ticker.EqualsIgnoreCase(ticker));
		if (snap == null)
			throw new QuoddApiException($"QUODD returned no snapshot for '{ticker}'.");
		if (!snap.Error.IsEmpty())
			throw new QuoddApiException($"QUODD snapshot error for '{ticker}': {snap.Error}");

		var snapshot = snap.ToLevel1Message(mdMsg.TransactionId, securityId);
		if (snapshot.Changes.Count > 0)
		{
			await SendOutMessageAsync(snapshot, cancellationToken);
			if (remaining is > 0)
				remaining--;
		}

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		AddLiveSubscription(mdMsg, securityId, ticker, assetType, remaining);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}
}
