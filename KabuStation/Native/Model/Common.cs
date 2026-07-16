namespace StockSharp.KabuStation.Native.Model;

internal sealed class KabuStationApiException : InvalidOperationException
{
	public KabuStationApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

internal sealed class KabuStationTokenRequest
{
	[JsonProperty("APIPassword")]
	public string ApiPassword { get; set; }
}

internal sealed class KabuStationTokenResponse
{
	[JsonProperty("ResultCode")]
	public int ResultCode { get; set; }

	[JsonProperty("Token")]
	public string Token { get; set; }
}

internal sealed class KabuStationErrorResponse
{
	[JsonProperty("Code")]
	public int? Code { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }
}

internal sealed class KabuStationOrderResult
{
	[JsonProperty("Result")]
	public int Result { get; set; }

	[JsonProperty("OrderId")]
	public string OrderId { get; set; }
}

internal sealed class KabuStationRegisteredSymbol
{
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
}

internal sealed class KabuStationRegistrationRequest
{
	[JsonProperty("Symbols")]
	public KabuStationRegisteredSymbol[] Symbols { get; set; }
}

internal sealed class KabuStationRegistrationResponse
{
	[JsonProperty("RegistList")]
	public KabuStationRegisteredSymbol[] RegisteredSymbols { get; set; }
}

internal sealed class KabuStationSecurityInfo
{
	private const string _prefix = "KABUSTATION";

	public string Symbol { get; init; }
	public int Exchange { get; init; }
	public int NativeSecurityType { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public string BoardCode { get; init; }

	public string ToNative()
		=> string.Join('|', _prefix, Uri.EscapeDataString(Symbol ?? string.Empty), Exchange,
			NativeSecurityType, (int)SecurityType);

	public static bool TryParse(object native, out KabuStationSecurityInfo info)
	{
		info = null;
		if (native is not string value)
			return false;

		var parts = value.Split('|');
		if (parts.Length != 5 || !parts[0].Equals(_prefix, StringComparison.OrdinalIgnoreCase) ||
			!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exchange) ||
			!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nativeType) ||
			!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var securityType))
			return false;

		info = new()
		{
			Symbol = Uri.UnescapeDataString(parts[1]),
			Exchange = exchange,
			NativeSecurityType = nativeType,
			SecurityType = (SecurityTypes)securityType,
			BoardCode = KabuStationExtensions.ToBoardCode(exchange),
		};
		return true;
	}
}
