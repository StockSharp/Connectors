namespace StockSharp.Schwab;

partial class SchwabMessageAdapter
{
	private ValueTask OnStreamerError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnLevelOneReceived(string symbol, LevelOneContent data, CancellationToken cancellationToken)
	{
		foreach (var pair in _level1Subscriptions.ToArray().Where(p => p.Value.SecurityCode.EqualsIgnoreCase(symbol)))
		{
			var milliseconds = data.QuoteTime ?? data.TradeTime;
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = pair.Key,
				SecurityId = pair.Value,
				ServerTime = milliseconds is null ? DateTime.UtcNow : milliseconds.Value.FromUnix(false),
			}
			.TryAdd(Level1Fields.BestBidPrice, data.BidPrice)
			.TryAdd(Level1Fields.BestAskPrice, data.AskPrice)
			.TryAdd(Level1Fields.LastTradePrice, data.LastPrice)
			.TryAdd(Level1Fields.BestBidVolume, data.BidSize)
			.TryAdd(Level1Fields.BestAskVolume, data.AskSize)
			.TryAdd(Level1Fields.Volume, data.Volume)
			.TryAdd(Level1Fields.LastTradeVolume, data.LastSize)
			.TryAdd(Level1Fields.HighPrice, data.HighPrice)
			.TryAdd(Level1Fields.LowPrice, data.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, data.ClosePrice)
			.TryAdd(Level1Fields.OpenPrice, data.OpenPrice)
			.TryAdd(Level1Fields.Change, data.Change), cancellationToken);
		}
	}

	private async ValueTask OnBookReceived(string service, string symbol, BookContent data, CancellationToken cancellationToken)
	{
		foreach (var pair in _depthSubscriptions.ToArray().Where(p => p.Value.service == service && p.Value.securityId.SecurityCode.EqualsIgnoreCase(symbol)))
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = pair.Key,
				SecurityId = pair.Value.securityId,
				ServerTime = data.Timestamp is long milliseconds ? milliseconds.FromUnix(false) : DateTime.UtcNow,
				Bids = (data.Bids ?? []).Select(level => new QuoteChange(level.Price, level.Volume)).ToArray(),
				Asks = (data.Asks ?? []).Select(level => new QuoteChange(level.Price, level.Volume)).ToArray(),
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}
}
