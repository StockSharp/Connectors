namespace StockSharp.BitStamp.Native;

static class Extensions
{
	public static TimeFrameCandleMessage ToCandleMessage(this Model.Ohlc candle, MarketDataMessage subscription, DateTime snapshotTime)
		=> new()
		{
			OriginalTransactionId = subscription.TransactionId,
			OpenTime = candle.Time,
			OpenPrice = (decimal)candle.Open,
			HighPrice = (decimal)candle.High,
			LowPrice = (decimal)candle.Low,
			ClosePrice = (decimal)candle.Close,
			TotalVolume = (decimal)candle.Volume,
			State = candle.Time + subscription.GetTimeFrame() <= snapshotTime ? CandleStates.Finished : CandleStates.Active,
		};

	public static Sides ToSide(this int type)
	{
		return type == 0 ? Sides.Buy : Sides.Sell;
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode?.Remove("/").ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency, bool format = true)
	{
		if (format)
		{
			if (currency.Length > 3 && !currency.Contains('/'))
				currency = currency.Insert(3, "/");

			currency = currency.ToUpperInvariant();
		}

		return new SecurityId
		{
			SecurityCode = currency,
			BoardCode = BoardCodes.BitStamp,
		};
	}

	public static DateTime ToDto(this string value, string format = "yyyy-MM-dd HH:mm:ss")
	{
		return value.ToDateTime(format).UtcKind();
	}
}