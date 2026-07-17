namespace StockSharp.Trading212;

public partial class Trading212MessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		await EnsureMetadata(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in _instruments.CachedValues.OrderBy(item => item.Ticker))
		{
			var securityType = instrument.Type.ToSecurityType();
			if (securityType == null || securityTypes.Count > 0 && !securityTypes.Contains(securityType.Value))
				continue;

			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				SecurityType = securityType,
				Name = instrument.Name,
				ShortName = instrument.ShortName.IsEmpty(instrument.Ticker),
				Class = GetExchange(instrument.WorkingScheduleId),
				Currency = instrument.CurrencyCode.ToCurrency(),
			};
			if (!message.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}
}
