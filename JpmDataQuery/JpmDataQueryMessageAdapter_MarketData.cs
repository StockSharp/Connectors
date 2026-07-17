namespace StockSharp.JpmDataQuery;

public partial class JpmDataQueryMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var keywords = lookupMsg.SecurityId.SecurityCode;

		await foreach (var instrument in SafeClient().LookupInstruments(
			GroupId, keywords, exactIdentifier: false, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			CacheInstrument(instrument);
			var security = instrument.ToSecurityMessage(
				lookupMsg.TransactionId, GroupId, SecurityType);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var instrument = await ResolveInstrument(mdMsg.SecurityId, cancellationToken);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date;
		var isHistory = mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null;
		var from = mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		var values = new SortedDictionary<DateTime, decimal>();
		await foreach (var seriesInstrument in SafeClient().GetTimeSeries(
			instrument.InstrumentId, Attribute, from, to, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			if (!seriesInstrument.InstrumentId.EqualsIgnoreCase(instrument.InstrumentId))
				continue;

			foreach (var attribute in seriesInstrument.Attributes ?? [])
			{
				if (!attribute.Matches(Attribute))
					continue;

				foreach (var point in attribute.TimeSeries ?? [])
				{
					var time = point.GetTime();
					if (time != null && point.Value != null && time >= from && time <= to)
						values[time.Value] = point.Value.Value;
				}
			}
		}

		if (values.Count == 0)
			throw new InvalidOperationException(
				$"J.P. Morgan DataQuery returned no '{Attribute}' values for '{instrument.InstrumentId}' " +
				$"between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.");

		IEnumerable<KeyValuePair<DateTime, decimal>> selected = values;
		if (mdMsg.Count is > 0 and < int.MaxValue)
			selected = selected.TakeLast((int)mdMsg.Count.Value);
		if (!isHistory)
			selected = selected.TakeLast(1);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		var securityId = mdMsg.SecurityId;
		if (securityId.BoardCode.IsEmpty())
			securityId.BoardCode = _boardCode;
		var field = ValueField.ToStockSharp();
		foreach (var pair in selected)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = pair.Key,
			}.TryAdd(field, pair.Value), cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private static DateTime GetDefaultFrom(DateTime to, long? count)
	{
		if (count is > 0)
		{
			var requested = Math.Min(count.Value, 10000);
			return to.AddDays(-Math.Max(31, requested * 3));
		}

		return to.AddDays(-31);
	}
}
