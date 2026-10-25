namespace StockSharp.Connectors.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using StockSharp.Messages;

sealed class PastSessionCandleAdapter : HistoricalMessageAdapter
{
	private readonly DateTime _openTime;

	public PastSessionCandleAdapter(DateTime openTime) : base(new IncrementalIdGenerator())
	{
		_openTime = openTime;
		this.AddMarketDataSupport();
		this.AddSupportedCandleTimeFrames([TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)]);
	}

	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.From <= _openTime && message.To >= _openTime)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				DataType = message.DataType2,
				OpenTime = _openTime,
				OpenPrice = 250m,
				HighPrice = 252m,
				LowPrice = 248m,
				ClosePrice = 251m,
				TotalVolume = 100m,
				State = CandleStates.Finished,
			}, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
