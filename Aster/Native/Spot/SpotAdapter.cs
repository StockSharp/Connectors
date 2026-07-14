namespace StockSharp.Aster.Native.Spot;

using StockSharp.Aster.Native.Common;

sealed class SpotAdapter : BinanceLikeSectionAdapter
{
	public SpotAdapter(SecureString key, SecureString secret, string restEndpoint, string wsEndpoint, WorkingTime workingTime)
		: base(key, secret, BoardCodes.AsterSpot, SecurityTypes.CryptoCurrency, "Spot", restEndpoint, wsEndpoint, "/api/v1", workingTime)
	{
	}

	protected override async ValueTask SendSectionPortfolioAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var account = await RestClient.GetSpotAccountAsync(cancellationToken);
		var serverTime = account.UpdateTime?.FromUnix(false) ?? CurrentTime;

		foreach (var balance in account.Balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Asset.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Free.To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked.To<decimal?>(), true),
			cancellationToken);
		}
	}
}
