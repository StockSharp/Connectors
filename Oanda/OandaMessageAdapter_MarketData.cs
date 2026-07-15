namespace StockSharp.Oanda;

partial class OandaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var instruments = await _restClient.GetInstrumentsAsync(GetDefaultAccount(),
			lookupMsg.SecurityId == default
				? []
				: [lookupMsg.SecurityId.ToOanda()], cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in instruments)
		{
			var secMsg = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.Name.ToStockSharp(),
				SecurityType = instrument.Type.ToSecurityType(),
				Name = instrument.DisplayName,
				Decimals = instrument.DisplayPrecision,
			};

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var instrument = mdMsg.SecurityId.ToOanda();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_streamigClient.SubscribePricesStreaming(GetDefaultAccount(), instrument);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_streamigClient.UnSubscribePricesStreaming(GetDefaultAccount(), instrument);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var instrument = secId.ToOanda();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var from = mdMsg.From;

			string priceType;
			switch (mdMsg.BuildField)
			{
				case Level1Fields.BestBidPrice:
					priceType = "B";
					break;
				case Level1Fields.BestAskPrice:
					priceType = "A";
					break;
				default:
					priceType = "M";
					break;
			}

			while (true)
			{
				var candles = await _restClient.GetCandlesAsync(instrument,
					mdMsg.GetTimeFrame().ToOanda(), priceType,
					mdMsg.Count, from?.ToUnixStr(), null, cancellationToken);

				var count = 0;

				foreach (var candle in candles)
				{
					count++;

					var time = candle.Time.FromUnixStr().Value;
					var price = candle.Mid ?? candle.Bid ?? candle.Ask;

					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						OpenTime = time,
						SecurityId = secId,
						OpenPrice = (decimal)price.Open,
						HighPrice = (decimal)price.High,
						LowPrice = (decimal)price.Low,
						ClosePrice = (decimal)price.Close,
						TotalVolume = (decimal)candle.Volume,
						State = candle.Complete ? CandleStates.Finished : CandleStates.Active
					}, cancellationToken);

					from = time;
				}

				if (mdMsg.Count == null && count == 500)
					continue;

				break;
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewPricing(StreamingPricingResponse response, CancellationToken cancellationToken)
	{
		var secId = response.Instrument.ToStockSharp();
		var time = response.Time.FromUnixStr() ?? CurrentTime;

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = time,
			Bids = response.Bids.Select(q => q.ToQuoteChange()).ToArray(),
			Asks = response.Asks.Select(q => q.ToQuoteChange()).ToArray(),
		}, cancellationToken);

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = time,
			SecurityId = secId,
		}.Add(Level1Fields.State, response.Tradeable ? SecurityStates.Trading : SecurityStates.Stoped), cancellationToken);
	}
}
