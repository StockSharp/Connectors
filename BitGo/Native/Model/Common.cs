namespace StockSharp.BitGo.Native.Model;

sealed class BitGoDataResponse<T>
{
	[JsonProperty("data")]
	public T[] Data { get; set; }
}

sealed class BitGoEmptyResponse
{
}

sealed class BitGoApiError
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorName")]
	public string ErrorName { get; set; }

	[JsonProperty("reqId")]
	public string RequestId { get; set; }

	[JsonProperty("context")]
	public BitGoErrorContext Context { get; set; }

	public string GetMessage()
	{
		var text = Context?.Message;
		if (text.IsEmpty())
			text = Error;
		if (text.IsEmpty())
			text = ErrorName;
		if (!Context?.Field.IsEmpty() == true)
			text = Context.Field + ": " + text;
		if (!RequestId.IsEmpty())
			text += " (request " + RequestId + ")";
		return text;
	}
}

sealed class BitGoErrorContext
{
	[JsonProperty("errorName")]
	public string ErrorName { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BitGoValidationApiError
{
	[JsonProperty("error")]
	public BitGoValidationError Error { get; set; }

	[JsonProperty("errorName")]
	public string ErrorName { get; set; }

	[JsonProperty("reqId")]
	public string RequestId { get; set; }

	public string GetMessage()
	{
		var message = Error?.Message;
		if (message.IsEmpty())
			message = ErrorName;
		if (Error?.Field.IsEmpty() == false)
			message = Error.Field + ": " + message;
		if (Error is not null &&
			(!Error.MinimumQuantity.IsEmpty() || !Error.MaximumQuantity.IsEmpty()))
			message += " (min " + (Error.MinimumQuantity ?? "n/a") +
				", max " + (Error.MaximumQuantity ?? "n/a") + ")";
		if (!RequestId.IsEmpty())
			message += " (request " + RequestId + ")";
		return message;
	}
}

sealed class BitGoValidationError
{
	[JsonProperty("currencySymbol")]
	public string CurrencySymbol { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("minQuantity")]
	public string MinimumQuantity { get; set; }

	[JsonProperty("maxQuantity")]
	public string MaximumQuantity { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }
}

sealed class BitGoAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class BitGoBalance
{
	[JsonProperty("currencyId")]
	public string CurrencyId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("heldBalance")]
	public string HeldBalance { get; set; }

	[JsonProperty("unsettledHeldBalance")]
	public string UnsettledHeldBalance { get; set; }

	[JsonProperty("tradableBalance")]
	public string TradableBalance { get; set; }

	[JsonProperty("withdrawableBalance")]
	public string WithdrawableBalance { get; set; }
}
