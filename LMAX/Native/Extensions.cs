namespace StockSharp.LMAX.Native;

static class Extensions
{
	public static SecurityTypes? ToSecurityType(this string assetClass)
	{
		if (assetClass.IsEmpty())
			return null;

		return assetClass.ToUpperInvariant() switch
		{
			AssetClasses.Currency => SecurityTypes.Currency,
			AssetClasses.CurrencyFuture => SecurityTypes.Future,
			AssetClasses.Commodity => SecurityTypes.Commodity,
			AssetClasses.Equity => SecurityTypes.Stock,
			AssetClasses.Index => SecurityTypes.Index,
			AssetClasses.Ndf => SecurityTypes.Currency,
			AssetClasses.Rate => SecurityTypes.Index,
			_ => null,
		};
	}

	public static Messages.OrderTypes? ToOrderType(this string orderType)
	{
		if (orderType.IsEmpty())
			return null;

		return orderType switch
		{
			Model.OrderTypes.Market => Messages.OrderTypes.Market,
			Model.OrderTypes.Limit => Messages.OrderTypes.Limit,
			Model.OrderTypes.Stop => Messages.OrderTypes.Conditional,
			Model.OrderTypes.StopLimit => Messages.OrderTypes.Conditional,
			_ => null,
		};
	}

	public static Messages.TimeInForce? ToTimeInForce(this string tif)
	{
		if (tif.IsEmpty())
			return null;

		return tif switch
		{
			Model.TimeInForce.FillOrKill => Messages.TimeInForce.MatchOrCancel,
			Model.TimeInForce.ImmediateOrCancel => Messages.TimeInForce.CancelBalance,
			Model.TimeInForce.GoodForDay => Messages.TimeInForce.PutInQueue,
			Model.TimeInForce.GoodTilCancelled => Messages.TimeInForce.PutInQueue,
			_ => null,
		};
	}

	public static Sides? ToSide(this string side)
	{
		if (side.IsEmpty())
			return null;

		return side switch
		{
			OrderSides.Bid => Sides.Buy,
			OrderSides.Ask => Sides.Sell,
			_ => null,
		};
	}

	// Reverse conversions (StockSharp -> LMAX)

	public static string ToLmax(this Messages.OrderTypes? orderType, bool hasStopPrice)
	{
		return orderType switch
		{
			Messages.OrderTypes.Market => Model.OrderTypes.Market,
			Messages.OrderTypes.Limit => Model.OrderTypes.Limit,
			Messages.OrderTypes.Conditional => hasStopPrice ? Model.OrderTypes.StopLimit : Model.OrderTypes.Stop,
			_ => Model.OrderTypes.Limit,
		};
	}

	public static string ToLmax(this Messages.TimeInForce? tif, DateTime? tillDate)
	{
		return tif switch
		{
			Messages.TimeInForce.MatchOrCancel => Model.TimeInForce.FillOrKill,
			Messages.TimeInForce.CancelBalance => Model.TimeInForce.ImmediateOrCancel,
			Messages.TimeInForce.PutInQueue => tillDate != null ? Model.TimeInForce.GoodForDay : Model.TimeInForce.GoodTilCancelled,
			null => tillDate != null ? Model.TimeInForce.GoodForDay : Model.TimeInForce.GoodTilCancelled,
			_ => Model.TimeInForce.GoodTilCancelled,
		};
	}

	public static string ToLmax(this Sides side)
	{
		return side switch
		{
			Sides.Buy => OrderSides.Bid,
			Sides.Sell => OrderSides.Ask,
			_ => OrderSides.Bid,
		};
	}
}