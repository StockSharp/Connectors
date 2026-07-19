namespace StockSharp.Coinhako.Native.Model;

sealed class CoinhakoLockedBalance
{
    [JsonProperty("orders")]
    public decimal Orders { get; set; }

    [JsonProperty("alternative_products")]
    public decimal AlternativeProducts { get; set; }
}

sealed class CoinhakoBalance
{
    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("regular")]
    public decimal Available { get; set; }

    [JsonProperty("locked")]
    public decimal Locked { get; set; }

    [JsonProperty("locked_balance")]
    public CoinhakoLockedBalance LockedBalance { get; set; }
}

sealed class CoinhakoBalanceQuery : CoinhakoQueryParameters
{
    public string Currency { get; init; }

    public override void Append(CoinhakoQueryWriter writer)
        => writer.Add("currency", Currency);
}

sealed class CoinhakoOrderQuoteRequest
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public CoinhakoSides Side { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("payment_method")]
    public string PaymentMethod { get; init; }
}

sealed class CoinhakoOrderQuote
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public CoinhakoSides Side { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("payment_method")]
    public string PaymentMethod { get; set; }

    [JsonProperty("quote_id")]
    public string QuoteId { get; set; }

    [JsonProperty("locked_price")]
    public decimal LockedPrice { get; set; }

    [JsonProperty("expires_at")]
    public long ExpiresAt { get; set; }
}

sealed class CoinhakoOrderRequest
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public CoinhakoSides Side { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }

    [JsonProperty("order_quote_id", NullValueHandling = NullValueHandling.Ignore)]
    public string OrderQuoteId { get; init; }

    [JsonProperty("execution_type")]
    public CoinhakoExecutionTypes ExecutionType { get; init; }

    [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
    public string Price { get; init; }

    [JsonProperty("expires_at", NullValueHandling = NullValueHandling.Ignore)]
    public long? ExpiresAt { get; init; }
}

sealed class CoinhakoOrder
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("order_receipt_id")]
    public string ReceiptId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public CoinhakoSides Side { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("base_currency")]
    public string BaseCurrency { get; set; }

    [JsonProperty("counter_currency")]
    public string CounterCurrency { get; set; }

    [JsonProperty("payment_method")]
    public string PaymentMethod { get; set; }

    [JsonProperty("status")]
    public CoinhakoOrderStatuses Status { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; set; }

    [JsonProperty("created_at")]
    public long CreatedAt { get; set; }

    [JsonProperty("fee")]
    public decimal? Fee { get; set; }

    [JsonProperty("fee_currency")]
    public string FeeCurrency { get; set; }

    [JsonProperty("net_amount")]
    public decimal? NetAmount { get; set; }

    [JsonProperty("received_currency")]
    public string ReceivedCurrency { get; set; }

    [JsonProperty("execution_type")]
    public CoinhakoExecutionTypes ExecutionType { get; set; }

    [JsonProperty("expires_at")]
    public long? ExpiresAt { get; set; }

    [JsonProperty("cancel_reason")]
    public string CancelReason { get; set; }
}

sealed class CoinhakoOrdersQuery : CoinhakoQueryParameters
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public long? FromTime { get; init; }
    public long? ToTime { get; init; }
    public string BaseCurrency { get; init; }
    public string CounterCurrency { get; init; }
    public CoinhakoOrderStatuses[] Statuses { get; init; }
    public CoinhakoExecutionTypes[] ExecutionTypes { get; init; }
    public CoinhakoSides? Side { get; init; }

    public override void Append(CoinhakoQueryWriter writer)
    {
        writer.Add("page[number]", PageNumber);
        writer.Add("page[size]", PageSize);
        writer.Add("from_time", FromTime);
        writer.Add("to_time", ToTime);
        writer.Add("base_currency", BaseCurrency);
        writer.Add("counter_currency", CounterCurrency);
        foreach (var status in Statuses ?? [])
            writer.Add("status[]", status.ToApi());
        foreach (var type in ExecutionTypes ?? [])
            writer.Add("execution_type[]", type.ToApi());
        if (Side is { } side)
            writer.Add("type[]", side.ToApi());
    }
}
