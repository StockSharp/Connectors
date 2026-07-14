namespace StockSharp.Aster.Native.Derivatives;

using StockSharp.Aster.Native.Common;

sealed class DerivativesAdapter : BinanceLikeSectionAdapter
{
	public DerivativesAdapter(SecureString key, SecureString secret, string restEndpoint, string wsEndpoint, WorkingTime workingTime)
		: base(key, secret, BoardCodes.AsterDerivatives, SecurityTypes.Future, "Derivatives", restEndpoint, wsEndpoint, "/fapi/v1", workingTime)
	{
	}

	protected override async ValueTask SendSectionPortfolioAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var account = await RestClient.GetDerivativesAccountAsync(cancellationToken);
		var serverTime = account.UpdateTime?.FromUnix(false) ?? CurrentTime;

		foreach (var asset in account.Assets ?? [])
		{
			if (asset?.Asset.IsEmpty() != false)
				continue;

			var walletBalance = asset.WalletBalance.To<decimal?>();
			var availableBalance = asset.AvailableBalance.To<decimal?>();

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = asset.Asset.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, walletBalance, true)
			.TryAdd(PositionChangeTypes.BlockedValue, walletBalance is decimal wb && availableBalance is decimal av ? (wb - av).Max(0m) : null, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, asset.UnrealizedProfit.To<decimal?>(), true),
			cancellationToken);
		}

		foreach (var pos in await RestClient.GetDerivativesPositionRiskAsync(cancellationToken))
		{
			if (pos?.Symbol.IsEmpty() != false)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = pos.Symbol.ToStockSharp(BoardCode),
				ServerTime = serverTime,
				OriginalTransactionId = lookupMsg.TransactionId,
				Side = pos.PositionSide.EqualsIgnoreCase("BOTH") ? null : pos.PositionSide.ToSide(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, pos.PositionAmt.To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, pos.EntryPrice.To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.Leverage, pos.Leverage.To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, pos.GetUnrealizedProfit().To<decimal?>(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, pos.LiquidationPrice.To<decimal?>(), true),
			cancellationToken);
		}
	}
}
