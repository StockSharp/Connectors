namespace StockSharp.BtcTurk.Native.Model;

sealed class BtcTurkOrderRequest
{
	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("newOrderClientId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("orderMethod")]
	public BtcTurkOrderMethods Method { get; init; }

	[JsonProperty("orderType")]
	public BtcTurkSides Side { get; init; }

	[JsonProperty("pairSymbol")]
	public string PairSymbol { get; init; }
}

sealed class BtcTurkOrdersQuery : IBtcTurkQuery
{
	public string PairSymbol { get; init; }
	public long? OrderId { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public int Page { get; init; }
	public int Count { get; init; }

	public BtcTurkParameter[] GetParameters()
	{
		var result = new List<BtcTurkParameter>();
		Add(result, "pairSymbol", PairSymbol);
		if (OrderId is long orderId)
			Add(result, "orderId",
				orderId.ToString(CultureInfo.InvariantCulture));
		if (From is DateTime from)
			Add(result, "startTime",
				from.ToUnixMilliseconds().ToString(
					CultureInfo.InvariantCulture));
		if (To is DateTime to)
			Add(result, "endTime",
				to.ToUnixMilliseconds().ToString(
					CultureInfo.InvariantCulture));
		if (Page > 0)
			Add(result, "page", Page.ToString(CultureInfo.InvariantCulture));
		if (Count > 0)
			Add(result, "limit", Count.Min(1000)
				.ToString(CultureInfo.InvariantCulture));
		return [.. result];
	}

	private static void Add(ICollection<BtcTurkParameter> result,
		string name, string value)
	{
		if (!value.IsEmpty())
			result.Add(new(name, value));
	}
}

sealed class BtcTurkTradesQuery : IBtcTurkQuery
{
	public long? OrderId { get; init; }
	public string PairSymbol { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }

	public BtcTurkParameter[] GetParameters()
	{
		if (OrderId is long orderId)
			return
			[
				new("orderId",
					orderId.ToString(CultureInfo.InvariantCulture)),
			];

		var result = new List<BtcTurkParameter>();
		if (!PairSymbol.IsEmpty())
			result.Add(new("pairSymbol", PairSymbol));
		if (From is DateTime from)
			result.Add(new("startDate", from.ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)));
		if (To is DateTime to)
			result.Add(new("endDate", to.ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BtcTurkCancelQuery : IBtcTurkQuery
{
	public long OrderId { get; init; }

	public BtcTurkParameter[] GetParameters()
		=>
		[
			new("id", OrderId.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BtcTurkOpenOrders
{
	[JsonProperty("asks")]
	public BtcTurkOrder[] Asks { get; set; }

	[JsonProperty("bids")]
	public BtcTurkOrder[] Bids { get; set; }

	[JsonIgnore]
	public BtcTurkOrder[] Orders
		=> [.. Asks ?? [], .. Bids ?? []];
}

sealed class BtcTurkOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public string PriceText { get; set; }

	[JsonProperty("amount")]
	public string AmountText { get; set; }

	[JsonProperty("quantity")]
	public string QuantityText { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPriceText { get; set; }

	[JsonProperty("leftAmount")]
	public string LeftAmountText { get; set; }

	[JsonProperty("pairSymbol")]
	public string PairSymbol { get; set; }

	[JsonProperty("pairSymbolNormalized")]
	public string PairSymbolNormalized { get; set; }

	[JsonProperty("type")]
	public BtcTurkSides Side { get; set; }

	[JsonProperty("method")]
	public BtcTurkOrderMethods Method { get; set; }

	[JsonProperty("orderClientId")]
	public string OrderClientId { get; set; }

	[JsonProperty("newOrderClientId")]
	public string NewOrderClientId { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("datetime")]
	public long DateTimeValue { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("status")]
	public BtcTurkOrderStatuses Status { get; set; }

	[JsonIgnore]
	public decimal Price => BtcTurkExtensions.ParseDecimal(PriceText);

	[JsonIgnore]
	public decimal Amount => BtcTurkExtensions.ParseDecimal(
		QuantityText.IsEmpty(AmountText));

	[JsonIgnore]
	public decimal StopPrice
		=> BtcTurkExtensions.ParseDecimal(StopPriceText);

	[JsonIgnore]
	public decimal LeftAmount
		=> LeftAmountText.IsEmpty()
			? Status is BtcTurkOrderStatuses.Closed or
				BtcTurkOrderStatuses.Canceled or
				BtcTurkOrderStatuses.Expired or
				BtcTurkOrderStatuses.Rejected
					? 0m
					: Amount
			: BtcTurkExtensions.ParseDecimal(LeftAmountText);

	[JsonIgnore]
	public string ClientOrderId => OrderClientId.IsEmpty(NewOrderClientId);

	[JsonIgnore]
	public long Timestamp => UpdateTime > 0
		? UpdateTime
		: Time > 0
			? Time
			: DateTimeValue;
}

sealed class BtcTurkUserTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("amount")]
	public string AmountText { get; set; }

	[JsonProperty("preciseAmount")]
	public decimal? PreciseAmount { get; set; }

	[JsonProperty("fee")]
	public string FeeText { get; set; }

	[JsonProperty("tax")]
	public string TaxText { get; set; }

	[JsonProperty("price")]
	public string PriceText { get; set; }

	[JsonProperty("numeratorSymbol")]
	public string Numerator { get; set; }

	[JsonProperty("denominatorSymbol")]
	public string Denominator { get; set; }

	[JsonProperty("orderType")]
	public BtcTurkSides Side { get; set; }

	[JsonProperty("orderClientId")]
	public string ClientOrderId { get; set; }

	[JsonIgnore]
	public decimal Amount
		=> (PreciseAmount ??
			BtcTurkExtensions.ParseDecimal(AmountText)).Abs();

	[JsonIgnore]
	public decimal Price => BtcTurkExtensions.ParseDecimal(PriceText);

	[JsonIgnore]
	public decimal Fee
		=> (BtcTurkExtensions.ParseDecimal(FeeText) +
			BtcTurkExtensions.ParseDecimal(TaxText)).Abs();

	[JsonIgnore]
	public string SecurityCode
		=> BtcTurkExtensions.CreateSecurityCode(Numerator, Denominator);
}

sealed class BtcTurkBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("assetname")]
	public string AssetName { get; set; }

	[JsonProperty("balance")]
	public string BalanceText { get; set; }

	[JsonProperty("locked")]
	public string LockedText { get; set; }

	[JsonProperty("free")]
	public string FreeText { get; set; }

	[JsonProperty("orderFund")]
	public string OrderFundText { get; set; }

	[JsonProperty("requestFund")]
	public string RequestFundText { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public decimal Balance
		=> BtcTurkExtensions.ParseDecimal(BalanceText);

	[JsonIgnore]
	public decimal Locked
		=> BtcTurkExtensions.ParseDecimal(LockedText);
}
