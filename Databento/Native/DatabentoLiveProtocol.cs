namespace StockSharp.Databento.Native;

internal sealed class DatabentoLiveSubscription
{
	public string Schema { get; init; }
	public string Symbology { get; init; }
	public string Symbol { get; init; }
	public bool IsSnapshot { get; init; }

	public string Key => $"{Schema}\u001f{Symbology}\u001f{Symbol}\u001f{IsSnapshot}";

	public string ToRequest(uint id)
	{
		ValidateControlValue(Schema, nameof(Schema));
		ValidateControlValue(Symbology, nameof(Symbology));
		ValidateControlValue(Symbol, nameof(Symbol), true);
		return $"schema={Schema}|stype_in={Symbology}|symbols={Symbol}|snapshot={(IsSnapshot ? 1 : 0)}|is_last=1|id={id}\n";
	}

	private static void ValidateControlValue(string value, string name, bool rejectComma = false)
	{
		if (value.IsEmpty())
			throw new ArgumentException("A Databento control value cannot be empty.", name);
		if (value.IndexOfAny(['|', '\r', '\n']) >= 0 || rejectComma && value.Contains(','))
			throw new ArgumentException("A Databento control value contains a reserved delimiter.", name);
	}
}

internal sealed class DatabentoAuthenticationResponse
{
	public bool IsSuccess { get; init; }
	public string SessionId { get; init; }
	public string Error { get; init; }

	public static DatabentoAuthenticationResponse Parse(string value)
	{
		var isSuccess = false;
		string sessionId = null;
		string error = null;
		foreach (var item in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = item.IndexOf('=');
			if (separator <= 0)
				continue;
			var name = item[..separator];
			var fieldValue = item[(separator + 1)..];
			switch (name)
			{
				case "success":
					isSuccess = fieldValue == "1";
					break;
				case "session_id":
					sessionId = fieldValue;
					break;
				case "error":
					error = fieldValue;
					break;
			}
		}

		return new()
		{
			IsSuccess = isSuccess,
			SessionId = sessionId,
			Error = error,
		};
	}
}
