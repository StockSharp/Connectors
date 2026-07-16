namespace StockSharp.Groww.Native.Model;

internal sealed class GrowwEnvelope<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("payload")]
	public T Payload { get; set; }

	[JsonProperty("error")]
	public GrowwError Error { get; set; }
}

internal sealed class GrowwError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("displayMessage")]
	public string DisplayMessage { get; set; }
}

internal sealed class GrowwAccessTokenRequest
{
	[JsonProperty("key_type")]
	public string KeyType { get; set; }

	[JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
	public string Checksum { get; set; }

	[JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
	public long? Timestamp { get; set; }

	[JsonProperty("totp", NullValueHandling = NullValueHandling.Ignore)]
	public string Totp { get; set; }
}

internal sealed class GrowwAccessTokenResponse
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("tokenRefId")]
	public string TokenReferenceId { get; set; }

	[JsonProperty("sessionName")]
	public string SessionName { get; set; }

	[JsonProperty("expiry")]
	public DateTime? Expiry { get; set; }

	[JsonProperty("isActive")]
	public bool? IsActive { get; set; }
}

internal sealed class GrowwSocketTokenRequest
{
	[JsonProperty("socketKey")]
	public string SocketKey { get; set; }
}

internal sealed class GrowwSocketToken
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("subscriptionId")]
	public string SubscriptionId { get; set; }
}

internal sealed class GrowwProfile
{
	[JsonProperty("vendor_user_id")]
	public string VendorUserId { get; set; }

	[JsonProperty("ucc")]
	public string Ucc { get; set; }

	[JsonProperty("nse_enabled")]
	public bool IsNseEnabled { get; set; }

	[JsonProperty("bse_enabled")]
	public bool IsBseEnabled { get; set; }

	[JsonProperty("ddpi_enabled")]
	public bool IsDdpiEnabled { get; set; }

	[JsonProperty("active_segments")]
	public string[] ActiveSegments { get; set; }
}

internal sealed class GrowwMargin
{
	[JsonProperty("clear_cash")]
	public decimal? ClearCash { get; set; }

	[JsonProperty("net_margin_used")]
	public decimal? NetMarginUsed { get; set; }

	[JsonProperty("brokerage_and_charges")]
	public decimal? BrokerageAndCharges { get; set; }

	[JsonProperty("collateral_used")]
	public decimal? CollateralUsed { get; set; }

	[JsonProperty("collateral_available")]
	public decimal? CollateralAvailable { get; set; }

	[JsonProperty("adhoc_margin")]
	public decimal? AdhocMargin { get; set; }
}

internal sealed class GrowwApiException : InvalidOperationException
{
	public GrowwApiException(HttpStatusCode statusCode, string code, string message)
		: base(code.IsEmpty() ? message : $"Groww API {code}: {message}")
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public string Code { get; }
}
