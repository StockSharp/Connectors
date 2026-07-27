namespace StockSharp.InvertirOnline.Native;

sealed class IolToken
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonProperty(".issued")]
    public string Issued { get; set; }

    [JsonProperty(".expires")]
    public string Expires { get; set; }

    [JsonProperty(".refreshexpires")]
    public string RefreshExpires { get; set; }
}

sealed class IolInstrumentGroup
{
    [JsonProperty("instrumento")]
    public string InstrumentType { get; set; }

    [JsonProperty("pais")]
    public string Country { get; set; }
}

sealed class IolInstrumentList
{
    [JsonProperty("titulos")]
    public IolInstrument[] Instruments { get; set; }
}

sealed class IolInstrument
{
    [JsonProperty("simbolo")]
    public string Symbol { get; set; }

    [JsonProperty("puntas")]
    public IolBookLevel Book { get; set; }

    [JsonProperty("ultimoPrecio")]
    public decimal LastPrice { get; set; }

    [JsonProperty("variacionPorcentual")]
    public decimal ChangePercent { get; set; }

    [JsonProperty("apertura")]
    public decimal OpenPrice { get; set; }

    [JsonProperty("maximo")]
    public decimal HighPrice { get; set; }

    [JsonProperty("minimo")]
    public decimal LowPrice { get; set; }

    [JsonProperty("ultimoCierre")]
    public decimal PreviousClose { get; set; }

    [JsonProperty("volumen")]
    public decimal Volume { get; set; }

    [JsonProperty("cantidadOperaciones")]
    public decimal OperationsCount { get; set; }

    [JsonProperty("fecha")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("tipoOpcion")]
    public string OptionType { get; set; }

    [JsonProperty("precioEjercicio")]
    public decimal Strike { get; set; }

    [JsonProperty("fechaVencimiento")]
    public string ExpiryDate { get; set; }

    [JsonProperty("mercado")]
    public string Market { get; set; }

    [JsonProperty("moneda")]
    public string Currency { get; set; }

    [JsonProperty("descripcion")]
    public string Description { get; set; }

    [JsonProperty("plazo")]
    public string Settlement { get; set; }

    [JsonProperty("laminaMinima")]
    public int MinimumLot { get; set; }

    [JsonProperty("lote")]
    public int Lot { get; set; }
}

sealed class IolTitle
{
    [JsonProperty("simbolo")]
    public string Symbol { get; set; }

    [JsonProperty("descripcion")]
    public string Description { get; set; }

    [JsonProperty("pais")]
    public string Country { get; set; }

    [JsonProperty("mercado")]
    public string Market { get; set; }

    [JsonProperty("tipo")]
    public string InstrumentType { get; set; }

    [JsonProperty("plazo")]
    public string Settlement { get; set; }

    [JsonProperty("moneda")]
    public string Currency { get; set; }
}

sealed class IolQuote
{
    [JsonProperty("ultimoPrecio")]
    public decimal LastPrice { get; set; }

    [JsonProperty("variacion")]
    public decimal ChangePercent { get; set; }

    [JsonProperty("apertura")]
    public decimal OpenPrice { get; set; }

    [JsonProperty("maximo")]
    public decimal HighPrice { get; set; }

    [JsonProperty("minimo")]
    public decimal LowPrice { get; set; }

    [JsonProperty("fechaHora")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("tendencia")]
    public string Trend { get; set; }

    [JsonProperty("cierreAnterior")]
    public decimal PreviousClose { get; set; }

    [JsonProperty("montoOperado")]
    public decimal Turnover { get; set; }

    [JsonProperty("volumenNominal")]
    public decimal Volume { get; set; }

    [JsonProperty("precioPromedio")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("moneda")]
    public string Currency { get; set; }

    [JsonProperty("precioAjuste")]
    public decimal SettlementPrice { get; set; }

    [JsonProperty("interesesAbiertos")]
    public decimal OpenInterest { get; set; }

    [JsonProperty("puntas")]
    public IolBookLevel[] Book { get; set; }

    [JsonProperty("cantidadOperaciones")]
    public int OperationsCount { get; set; }

    [JsonProperty("descripcionTitulo")]
    public string Description { get; set; }

    [JsonProperty("plazo")]
    public string Settlement { get; set; }

    [JsonProperty("laminaMinima")]
    public int MinimumLot { get; set; }

    [JsonProperty("lote")]
    public int Lot { get; set; }

    [JsonProperty("simbolo")]
    public string Symbol { get; set; }

    [JsonProperty("pais")]
    public string Country { get; set; }

    [JsonProperty("mercado")]
    public string Market { get; set; }

    [JsonProperty("tipo")]
    public string InstrumentType { get; set; }

    [JsonProperty("cantidadMinima")]
    public int MinimumQuantity { get; set; }

    [JsonProperty("puntosVariacion")]
    public decimal PriceStep { get; set; }
}

sealed class IolBookLevel
{
    [JsonProperty("cantidadCompra")]
    public decimal BidVolume { get; set; }

    [JsonProperty("precioCompra")]
    public decimal BidPrice { get; set; }

    [JsonProperty("precioVenta")]
    public decimal AskPrice { get; set; }

    [JsonProperty("cantidadVenta")]
    public decimal AskVolume { get; set; }
}

sealed class IolAccountState
{
    [JsonProperty("cuentas")]
    public IolAccount[] Accounts { get; set; }

    [JsonProperty("totalEnPesos")]
    public decimal TotalInPesos { get; set; }
}

sealed class IolAccount
{
    [JsonProperty("numero")]
    public string Number { get; set; }

    [JsonProperty("tipo")]
    public string Type { get; set; }

    [JsonProperty("moneda")]
    public string Currency { get; set; }

    [JsonProperty("disponible")]
    public decimal Available { get; set; }

    [JsonProperty("comprometido")]
    public decimal Blocked { get; set; }

    [JsonProperty("saldo")]
    public decimal Balance { get; set; }

    [JsonProperty("titulosValorizados")]
    public decimal SecuritiesValue { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }

    [JsonProperty("margenDescubierto")]
    public decimal Margin { get; set; }

    [JsonProperty("saldos")]
    public IolBalance[] Settlements { get; set; }

    [JsonProperty("estado")]
    public string State { get; set; }
}

sealed class IolBalance
{
    [JsonProperty("liquidacion")]
    public string Settlement { get; set; }

    [JsonProperty("saldo")]
    public decimal Balance { get; set; }

    [JsonProperty("comprometido")]
    public decimal Blocked { get; set; }

    [JsonProperty("disponible")]
    public decimal Available { get; set; }

    [JsonProperty("disponibleOperar")]
    public decimal AvailableToTrade { get; set; }
}

sealed class IolPortfolio
{
    [JsonProperty("pais")]
    public string Country { get; set; }

    [JsonProperty("activos")]
    public IolPosition[] Positions { get; set; }
}

sealed class IolPosition
{
    [JsonProperty("cantidad")]
    public decimal Quantity { get; set; }

    [JsonProperty("comprometido")]
    public decimal Blocked { get; set; }

    [JsonProperty("puntosVariacion")]
    public decimal PriceStep { get; set; }

    [JsonProperty("variacionDiaria")]
    public decimal DailyChange { get; set; }

    [JsonProperty("ultimoPrecio")]
    public decimal LastPrice { get; set; }

    [JsonProperty("ppc")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("gananciaPorcentaje")]
    public decimal ProfitPercent { get; set; }

    [JsonProperty("gananciaDinero")]
    public decimal Profit { get; set; }

    [JsonProperty("valorizado")]
    public decimal MarketValue { get; set; }

    [JsonProperty("titulo")]
    public IolTitle Title { get; set; }
}

sealed class IolOperation
{
    [JsonProperty("numero")]
    public long Number { get; set; }

    [JsonProperty("fechaOrden")]
    public DateTimeOffset OrderDate { get; set; }

    [JsonProperty("tipo")]
    public string Side { get; set; }

    [JsonProperty("estado")]
    public string State { get; set; }

    [JsonProperty("mercado")]
    public string Market { get; set; }

    [JsonProperty("simbolo")]
    public string Symbol { get; set; }

    [JsonProperty("cantidad")]
    public decimal Quantity { get; set; }

    [JsonProperty("monto")]
    public decimal Amount { get; set; }

    [JsonProperty("modalidad")]
    public string OrderType { get; set; }

    [JsonProperty("precio")]
    public decimal Price { get; set; }

    [JsonProperty("fechaOperada")]
    public DateTimeOffset ExecutionDate { get; set; }

    [JsonProperty("cantidadOperada")]
    public decimal ExecutedQuantity { get; set; }

    [JsonProperty("precioOperado")]
    public decimal ExecutionPrice { get; set; }

    [JsonProperty("montoOperado")]
    public decimal ExecutedAmount { get; set; }

    [JsonProperty("plazo")]
    public string Settlement { get; set; }
}

sealed class IolOperationDetail
{
    [JsonProperty("numero")]
    public long Number { get; set; }

    [JsonProperty("mercado")]
    public string Market { get; set; }

    [JsonProperty("simbolo")]
    public string Symbol { get; set; }

    [JsonProperty("moneda")]
    public string Currency { get; set; }

    [JsonProperty("tipo")]
    public string Side { get; set; }

    [JsonProperty("fechaAlta")]
    public DateTimeOffset OrderDate { get; set; }

    [JsonProperty("validez")]
    public DateTimeOffset Validity { get; set; }

    [JsonProperty("fechaOperado")]
    public DateTimeOffset ExecutionDate { get; set; }

    [JsonProperty("estadoActual")]
    public string State { get; set; }

    [JsonProperty("operaciones")]
    public IolFill[] Fills { get; set; }

    [JsonProperty("precio")]
    public decimal Price { get; set; }

    [JsonProperty("cantidad")]
    public decimal Quantity { get; set; }

    [JsonProperty("monto")]
    public decimal Amount { get; set; }

    [JsonProperty("modalidad")]
    public string OrderType { get; set; }

    [JsonProperty("plazo")]
    public string Settlement { get; set; }

    public IolOperation ToSummary()
        => new()
        {
            Number = Number,
            Market = Market,
            Symbol = Symbol,
            Side = Side,
            State = State,
            OrderDate = OrderDate,
            Quantity = Quantity,
            Amount = Amount,
            OrderType = OrderType,
            Price = Price,
            ExecutionDate = ExecutionDate,
            ExecutedQuantity = (Fills ?? [])
                .Where(item => item != null)
                .Sum(item => item.Quantity),
            ExecutionPrice = WeightedPrice(Fills),
            ExecutedAmount = (Fills ?? [])
                .Where(item => item != null)
                .Sum(
                item => item.Quantity * item.Price),
            Settlement = Settlement,
        };

    private static decimal WeightedPrice(IEnumerable<IolFill> fills)
    {
        var values = (fills ?? [])
            .Where(item => item?.Quantity > 0)
            .ToArray();
        var quantity = values.Sum(item => item.Quantity);
        return quantity <= 0
            ? 0
            : values.Sum(item => item.Quantity * item.Price) / quantity;
    }
}

sealed class IolFill
{
    [JsonProperty("fecha")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("cantidad")]
    public decimal Quantity { get; set; }

    [JsonProperty("precio")]
    public decimal Price { get; set; }
}

sealed class IolApiResponse
{
    [JsonProperty("ok")]
    public bool Ok { get; set; }

    [JsonProperty("messages")]
    public IolApiMessage[] Messages { get; set; }
}

sealed class IolApiMessage
{
    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }
}

sealed class IolOrderPlacement
{
    [JsonProperty("numeroOperacion")]
    public long OperationNumber { get; set; }
}

readonly record struct IolPlacementResult(
    long OperationNumber,
    bool Accepted,
    string Message);
