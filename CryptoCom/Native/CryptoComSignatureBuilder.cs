namespace StockSharp.CryptoCom.Native;

interface ICryptoComPrivateParams
{
	void WriteSignature(CryptoComSignatureBuilder builder);
}

sealed class CryptoComSignatureBuilder
{
	private readonly StringBuilder _builder = new();

	public void Add(string name, string value)
	{
		if (name.IsEmpty() || value is null)
			return;
		_builder.Append(name).Append(value);
	}

	public void Add(string name, int? value)
	{
		if (value is int number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public void Add(string name, CryptoComSides? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComOrderTypes? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComTimeInForces? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComTriggerPriceTypesNative? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComSpotMarginModes? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComCancelOrderTypes? value)
	{
		if (value is { } item)
			Add(name, item.ToWire());
	}

	public void Add(string name, CryptoComExecutionInstructions[] values)
	{
		if (values is not { Length: > 0 })
			return;
		_builder.Append(name);
		foreach (var value in values)
			_builder.Append(value.ToWire());
	}

	public override string ToString() => _builder.ToString();
}

sealed class CryptoComSigner : IDisposable
{
	private readonly HMACSHA256 _hasher;
	private readonly Lock _sync = new();

	public CryptoComSigner(SecureString secret)
	{
		if (secret.IsEmpty())
			throw new ArgumentNullException(nameof(secret));
		_hasher = new HMACSHA256(secret.UnSecure().UTF8());
	}

	public string Sign(string method, long id, string apiKey, ICryptoComPrivateParams parameters, long nonce)
	{
		var builder = new CryptoComSignatureBuilder();
		parameters?.WriteSignature(builder);
		var payload = method
			+ id.ToString(CultureInfo.InvariantCulture)
			+ apiKey
			+ builder.ToString()
			+ nonce.ToString(CultureInfo.InvariantCulture);

		using (_sync.EnterScope())
			return _hasher.ComputeHash(payload.UTF8()).Digest().ToLowerInvariant();
	}

	public void Dispose() => _hasher.Dispose();
}

sealed class CryptoComPrivateRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("api_key")]
	public string ApiKey { get; init; }

	[JsonProperty("sig")]
	public string Signature { get; set; }

	[JsonProperty("nonce")]
	public long Nonce { get; init; }

	[JsonProperty("params")]
	public ICryptoComPrivateParams Parameters { get; init; }
}

sealed class CryptoComCreateOrderParams : ICryptoComPrivateParams
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("side")]
	public CryptoComSides Side { get; init; }

	[JsonProperty("type")]
	public CryptoComOrderTypes Type { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("notional")]
	public string Notional { get; init; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; init; }

	[JsonProperty("time_in_force")]
	public CryptoComTimeInForces? TimeInForce { get; init; }

	[JsonProperty("exec_inst")]
	public CryptoComExecutionInstructions[] ExecutionInstructions { get; init; }

	[JsonProperty("spot_margin")]
	public CryptoComSpotMarginModes? SpotMargin { get; init; }

	[JsonProperty("isolated_margin_amount")]
	public string IsolatedMarginAmount { get; init; }

	[JsonProperty("isolation_id")]
	public string IsolationId { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("ref_price")]
	public string ReferencePrice { get; init; }

	[JsonProperty("ref_price_type")]
	public CryptoComTriggerPriceTypesNative? ReferencePriceType { get; init; }

	[JsonProperty("attach_isolation_id")]
	public string AttachIsolationId { get; init; }

	public void WriteSignature(CryptoComSignatureBuilder builder)
	{
		builder.Add("attach_isolation_id", AttachIsolationId);
		builder.Add("client_oid", ClientOrderId);
		builder.Add("exec_inst", ExecutionInstructions);
		builder.Add("instrument_name", InstrumentName);
		builder.Add("isolated_margin_amount", IsolatedMarginAmount);
		builder.Add("isolation_id", IsolationId);
		builder.Add("leverage", Leverage);
		builder.Add("notional", Notional);
		builder.Add("price", Price);
		builder.Add("quantity", Quantity);
		builder.Add("ref_price", ReferencePrice);
		builder.Add("ref_price_type", ReferencePriceType);
		builder.Add("side", Side);
		builder.Add("spot_margin", SpotMargin);
		builder.Add("time_in_force", TimeInForce);
		builder.Add("type", Type);
	}
}

sealed class CryptoComAmendOrderParams : ICryptoComPrivateParams
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("orig_client_oid")]
	public string OriginalClientOrderId { get; init; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; init; }

	[JsonProperty("new_price")]
	public string NewPrice { get; init; }

	[JsonProperty("new_quantity")]
	public string NewQuantity { get; init; }

	public void WriteSignature(CryptoComSignatureBuilder builder)
	{
		builder.Add("client_oid", ClientOrderId);
		builder.Add("new_price", NewPrice);
		builder.Add("new_quantity", NewQuantity);
		builder.Add("order_id", OrderId);
		builder.Add("orig_client_oid", OriginalClientOrderId);
	}
}

sealed class CryptoComOrderIdentityParams : ICryptoComPrivateParams
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; init; }

	public void WriteSignature(CryptoComSignatureBuilder builder)
	{
		builder.Add("client_oid", ClientOrderId);
		builder.Add("order_id", OrderId);
	}
}

sealed class CryptoComInstrumentParams : ICryptoComPrivateParams
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }
	public void WriteSignature(CryptoComSignatureBuilder builder)
		=> builder.Add("instrument_name", InstrumentName);
}

sealed class CryptoComCancelAllParams : ICryptoComPrivateParams
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("type")]
	public CryptoComCancelOrderTypes? Type { get; init; }

	public void WriteSignature(CryptoComSignatureBuilder builder)
	{
		builder.Add("instrument_name", InstrumentName);
		builder.Add("type", Type);
	}
}

sealed class CryptoComHistoryParams : ICryptoComPrivateParams
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("start_time")]
	public string StartTime { get; init; }

	[JsonProperty("end_time")]
	public string EndTime { get; init; }

	[JsonProperty("limit")]
	public int? Limit { get; init; }

	[JsonProperty("isolation_id")]
	public string IsolationId { get; init; }

	public void WriteSignature(CryptoComSignatureBuilder builder)
	{
		builder.Add("end_time", EndTime);
		builder.Add("instrument_name", InstrumentName);
		builder.Add("isolation_id", IsolationId);
		builder.Add("limit", Limit);
		builder.Add("start_time", StartTime);
	}
}
