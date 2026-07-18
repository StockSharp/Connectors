namespace StockSharp.ActivFinancial;

public partial class ActivFinancialMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var query = ActivFinancialExtensions.BuildLookupQuery(lookupMsg, out var isExactNative);
		var exactKey = default(ActivSecurityKey);
		var dataSourceId = isExactNative &&
			ActivSecurityKey.TryParse(lookupMsg.SecurityId.Native as string, out exactKey)
				? exactKey.DataSourceId : (int)DataSource;
		var records = await SafeClient().Lookup(dataSourceId, query, 0,
			MaxLookupResults, FallbackTimeZoneId, cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var record in records)
		{
			if (left <= 0)
				break;
			if (record?.Symbol.IsEmpty() != false)
				continue;
			var security = record.ToSecurityMessage(lookupMsg.TransactionId,
				FallbackTimeZoneId);
			var native = security.SecurityId.Native as string;
			if (native.IsEmpty() || !emitted.Add(native) ||
				isExactNative && (!ActivSecurityKey.TryParse(native, out var candidate) ||
					candidate.DataSourceId != exactKey.DataSourceId ||
					candidate.SymbologyId != exactKey.SymbologyId ||
					!candidate.Symbol.EqualsIgnoreCase(exactKey.Symbol)) ||
				!MatchesLookup(security, lookupMsg, types))
			{
				continue;
			}
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
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
			if (RemoveLiveSubscription(mdMsg.OriginalTransactionId))
				await SafeClient().Unsubscribe(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException("ACTIV One API does not expose historical Level1 events.");

		var key = mdMsg.SecurityId.GetActivKey(DataSource, Symbology, FallbackTimeZoneId);
		var securityId = mdMsg.SecurityId.NormalizeActiv(key);
		var remaining = mdMsg.Count;
		var snapshots = await SafeClient().Snapshot(key.DataSourceId, key.SymbologyId,
			key.Symbol, key.TimeZoneId, cancellationToken);
		var snapshot = snapshots.FirstOrDefault();
		if (snapshot != null)
		{
			key = snapshot.ToKey(key.TimeZoneId);
			securityId = securityId.NormalizeActiv(key,
				ActivFinancialExtensions.ToBoardCode(snapshot.Mic));
			var output = CreateLevel1(mdMsg.TransactionId, securityId, snapshot);
			if (output != null)
			{
				await SendOutMessageAsync(output, cancellationToken);
				if (remaining is > 0)
					remaining--;
			}
		}

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = DataType.Level1,
		};
		AddLiveSubscription(subscription);
		try
		{
			await SafeClient().Subscribe(mdMsg.TransactionId,
				ActivGatewayDataKinds.Level1, key.DataSourceId, key.SymbologyId,
				key.Symbol, ToGatewayCount(remaining), key.TimeZoneId, cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (RemoveLiveSubscription(mdMsg.OriginalTransactionId))
				await SafeClient().Unsubscribe(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetActivKey(DataSource, Symbology, FallbackTimeZoneId);
		var securityId = mdMsg.SecurityId.NormalizeActiv(key);
		var remaining = mdMsg.Count;
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			var limit = GetHistoryLimit(remaining);
			var records = await SafeClient().GetTicks(key.DataSourceId, key.SymbologyId,
				key.Symbol, mdMsg.From, mdMsg.To, limit, key.TimeZoneId, cancellationToken);
			foreach (var record in records)
			{
				var output = CreateTick(mdMsg.TransactionId, securityId, record);
				if (output == null)
					continue;
				await SendOutMessageAsync(output, cancellationToken);
				if (remaining is > 0 && --remaining == 0)
					break;
			}
		}

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = DataType.Ticks,
		};
		AddLiveSubscription(subscription);
		try
		{
			await SafeClient().Subscribe(mdMsg.TransactionId,
				ActivGatewayDataKinds.Ticks, key.DataSourceId, key.SymbologyId,
				key.Symbol, ToGatewayCount(remaining), key.TimeZoneId, cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!ActivFinancialExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"ACTIV does not support {timeFrame} candles.");
		var minutes = checked((int)timeFrame.TotalMinutes);
		var key = mdMsg.SecurityId.GetActivKey(DataSource, Symbology, FallbackTimeZoneId);
		var securityId = mdMsg.SecurityId.NormalizeActiv(key);
		var remaining = mdMsg.Count;
		var records = await SafeClient().GetBars(key.DataSourceId, key.SymbologyId,
			key.Symbol, mdMsg.From, mdMsg.To, GetHistoryLimit(remaining), minutes,
			key.TimeZoneId, cancellationToken);
		foreach (var record in records)
		{
			if (record?.OpenPrice == null || record.HighPrice == null ||
				record.LowPrice == null || record.ClosePrice == null)
			{
				continue;
			}
			var openTime = record.GetEventTime(key.TimeZoneId, DateTime.UtcNow);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = record.OpenPrice.Value,
				HighPrice = record.HighPrice.Value,
				LowPrice = record.LowPrice.Value,
				ClosePrice = record.ClosePrice.Value,
				TotalVolume = ActivFinancialExtensions.NonNegative(record.CumulativeVolume) ?? 0,
				OpenInterest = ActivFinancialExtensions.NonNegative(record.OpenInterest),
				State = CandleStates.Finished,
				SeqNum = record.UpdateId ?? 0,
			}, cancellationToken);
			if (remaining is > 0 && --remaining == 0)
				break;
		}
		await Complete(mdMsg, cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		ActivGatewayRecord record)
	{
		if (record == null)
			return null;
		var now = DateTime.UtcNow;
		var serverTime = record.GetEventTime(FallbackTimeZoneId, now);
		var isTradeCancellation = record.EventType == 5;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			SeqNum = record.UpdateId ?? 0,
		}
		.TryAdd(Level1Fields.BestBidPrice,
			ActivFinancialExtensions.Positive(record.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume,
			ActivFinancialExtensions.NonNegative(record.BidSize))
		.TryAdd(Level1Fields.BestAskPrice,
			ActivFinancialExtensions.Positive(record.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume,
			ActivFinancialExtensions.NonNegative(record.AskSize))
		.TryAdd(Level1Fields.LastTradePrice, isTradeCancellation ? null :
			ActivFinancialExtensions.Positive(record.TradePrice))
		.TryAdd(Level1Fields.LastTradeVolume, isTradeCancellation ? null :
			ActivFinancialExtensions.NonNegative(record.TradeSize))
		.TryAdd(Level1Fields.OpenPrice,
			ActivFinancialExtensions.Positive(record.OpenPrice))
		.TryAdd(Level1Fields.HighPrice,
			ActivFinancialExtensions.Positive(record.HighPrice))
		.TryAdd(Level1Fields.LowPrice,
			ActivFinancialExtensions.Positive(record.LowPrice))
		.TryAdd(Level1Fields.ClosePrice,
			ActivFinancialExtensions.Positive(record.ClosePrice ?? record.PreviousClose))
		.TryAdd(Level1Fields.SettlementPrice,
			ActivFinancialExtensions.Positive(record.SettlementPrice))
		.TryAdd(Level1Fields.Volume,
			ActivFinancialExtensions.NonNegative(record.CumulativeVolume))
		.TryAdd(Level1Fields.OpenInterest,
			ActivFinancialExtensions.NonNegative(record.OpenInterest))
		.TryAdd(Level1Fields.Change,
			record.PercentChange ?? record.NetChange)
		.TryAdd(Level1Fields.State, ToSecurityState(record.TradingStatus));
		if (record.BidPrice is > 0)
			message.TryAdd(Level1Fields.BestBidTime,
				record.BidTime.ToUtc(record.TimeZone.IsEmpty(FallbackTimeZoneId), now) ?? serverTime);
		if (record.AskPrice is > 0)
			message.TryAdd(Level1Fields.BestAskTime,
				record.AskTime.ToUtc(record.TimeZone.IsEmpty(FallbackTimeZoneId), now) ?? serverTime);
		if (!isTradeCancellation && record.TradePrice is > 0)
			message.TryAdd(Level1Fields.LastTradeTime,
				record.GetTradeTime(FallbackTimeZoneId, now) ?? serverTime);
		return message.Changes.Count == 0 ? null : message;
	}

	private ExecutionMessage CreateTick(long transactionId, SecurityId securityId,
		ActivGatewayRecord record)
	{
		if (record == null || !IsTrade(record))
			return null;
		var price = ActivFinancialExtensions.Positive(record.TickPrice ?? record.TradePrice);
		var volume = ActivFinancialExtensions.NonNegative(record.TickSize ?? record.TradeSize);
		if (price == null)
			return null;
		var serverTime = record.GetEventTime(FallbackTimeZoneId, DateTime.UtcNow);
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = serverTime,
			TradeStringId = record.TradeId,
			TradePrice = price,
			TradeVolume = volume,
			IsCancellation = record.TickType == 3 || record.EventType == 5,
			SeqNum = record.UpdateId ?? 0,
		};
	}

	private static bool IsTrade(ActivGatewayRecord record)
		=> record.TickType is >= 1 and <= 5 || record.EventType is 1 or 4 or 5 or 6;

	private static SecurityStates? ToSecurityState(string value)
	{
		if (value.IsEmpty())
			return null;
		var normalized = new string(value.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant).ToArray());
		if (normalized.Contains("halt", StringComparison.Ordinal) ||
			normalized.Contains("suspend", StringComparison.Ordinal) ||
			normalized is "closed" or "stopped" or "nottrading")
		{
			return SecurityStates.Stoped;
		}
		if (normalized.Contains("trad", StringComparison.Ordinal) ||
			normalized is "open" or "continuous")
		{
			return SecurityStates.Trading;
		}
		return null;
	}

	private static bool MatchesLookup(SecurityMessage security,
		SecurityLookupMessage lookup, HashSet<SecurityTypes> types)
	{
		if (types.Count > 0 && security.SecurityType != null &&
			!types.Contains(security.SecurityType.Value))
		{
			return false;
		}
		var requestedBoard = lookup.SecurityId.BoardCode;
		if (!requestedBoard.IsEmpty() &&
			!requestedBoard.EqualsIgnoreCase(ActivFinancialExtensions.BoardCode) &&
			!requestedBoard.EqualsIgnoreCase(security.SecurityId.BoardCode))
		{
			return false;
		}
		var criteria = (SecurityLookupMessage)lookup.Clone();
		criteria.SecurityId = default;
		return security.IsMatch(criteria);
	}

	private int GetHistoryLimit(long? requested)
		=> requested == null ? MaxHistoryResults :
			(int)Math.Min(MaxHistoryResults, Math.Max(1, requested.Value));

	private static int? ToGatewayCount(long? count)
		=> count == null ? null : (int)Math.Min(int.MaxValue, Math.Max(1, count.Value));

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
