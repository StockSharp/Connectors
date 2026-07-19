namespace StockSharp.ManifestTrade.Native.Model;

sealed class ManifestTradeRpcRequest<TParameters>
	where TParameters : ManifestTradeRpcParameters
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public TParameters Parameters { get; init; }
}

sealed class ManifestTradeRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public ManifestTradeRpcError Error { get; init; }
}

sealed class ManifestTradeRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(ManifestTradeRpcParametersConverter))]
abstract class ManifestTradeRpcParameters
{
}

sealed class ManifestTradeRpcEmptyParameters : ManifestTradeRpcParameters
{
}

sealed class ManifestTradeRpcAddressAccountParameters : ManifestTradeRpcParameters
{
	public string Address { get; init; }
	public ManifestTradeRpcAccountConfig Config { get; init; }
}

sealed class ManifestTradeRpcAddressesAccountParameters : ManifestTradeRpcParameters
{
	public string[] Addresses { get; init; }
	public ManifestTradeRpcAccountConfig Config { get; init; }
}

sealed class ManifestTradeRpcAddressCommitmentParameters : ManifestTradeRpcParameters
{
	public string Address { get; init; }
	public ManifestTradeRpcCommitmentConfig Config { get; init; }
}

sealed class ManifestTradeRpcLatestBlockhashParameters : ManifestTradeRpcParameters
{
	public ManifestTradeRpcCommitmentConfig Config { get; init; }
}

sealed class ManifestTradeRpcSignaturesParameters : ManifestTradeRpcParameters
{
	public string Address { get; init; }
	public ManifestTradeRpcSignaturesConfig Config { get; init; }
}

sealed class ManifestTradeRpcTransactionParameters : ManifestTradeRpcParameters
{
	public string Signature { get; init; }
	public ManifestTradeRpcTransactionConfig Config { get; init; }
}

sealed class ManifestTradeRpcSendTransactionParameters : ManifestTradeRpcParameters
{
	public string Transaction { get; init; }
	public ManifestTradeRpcSendConfig Config { get; init; }
}

sealed class ManifestTradeRpcRecentFeesParameters : ManifestTradeRpcParameters
{
	public string[] Addresses { get; init; }
}

sealed class ManifestTradeRpcAccountConfig
{
	[JsonProperty("encoding")]
	public ManifestTradeEncodings Encoding { get; init; } = ManifestTradeEncodings.Base64;

	[JsonProperty("commitment")]
	public ManifestTradeCommitments Commitment { get; init; } =
		ManifestTradeCommitments.Confirmed;
}

sealed class ManifestTradeRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public ManifestTradeCommitments Commitment { get; init; } =
		ManifestTradeCommitments.Confirmed;
}

sealed class ManifestTradeRpcSignaturesConfig
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("before")]
	public string Before { get; init; }

	[JsonProperty("until")]
	public string Until { get; init; }

	[JsonProperty("commitment")]
	public ManifestTradeCommitments Commitment { get; init; } =
		ManifestTradeCommitments.Confirmed;
}

sealed class ManifestTradeRpcTransactionConfig
{
	[JsonProperty("encoding")]
	public ManifestTradeEncodings Encoding { get; init; } = ManifestTradeEncodings.Json;

	[JsonProperty("commitment")]
	public ManifestTradeCommitments Commitment { get; init; } =
		ManifestTradeCommitments.Confirmed;

	[JsonProperty("maxSupportedTransactionVersion")]
	public int MaximumSupportedTransactionVersion { get; init; }
}

sealed class ManifestTradeRpcSendConfig
{
	[JsonProperty("encoding")]
	public ManifestTradeEncodings Encoding { get; init; } = ManifestTradeEncodings.Base64;

	[JsonProperty("skipPreflight")]
	public bool IsPreflightSkipped { get; init; }

	[JsonProperty("preflightCommitment")]
	public ManifestTradeCommitments PreflightCommitment { get; init; } =
		ManifestTradeCommitments.Confirmed;

	[JsonProperty("maxRetries")]
	public int MaximumRetries { get; init; }
}

sealed class ManifestTradeRpcContext
{
	[JsonProperty("slot")]
	public long Slot { get; init; }
}

sealed class ManifestTradeRpcContextValue<TResult>
{
	[JsonProperty("context")]
	public ManifestTradeRpcContext Context { get; init; }

	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class ManifestTradeRpcAccount
{
	[JsonProperty("data")]
	public string[] Data { get; init; }

	[JsonProperty("executable")]
	public bool IsExecutable { get; init; }

	[JsonProperty("lamports")]
	public ulong Lamports { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("space")]
	public int Space { get; init; }
}

sealed class ManifestTradeRpcLatestBlockhash
{
	[JsonProperty("blockhash")]
	public string Blockhash { get; init; }

	[JsonProperty("lastValidBlockHeight")]
	public long LastValidBlockHeight { get; init; }
}

sealed class ManifestTradeRpcSignatureInfo
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("err")]
	public ManifestTradeRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public ManifestTradeCommitments? ConfirmationStatus { get; init; }
}

sealed class ManifestTradeRpcTransaction
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("meta")]
	public ManifestTradeRpcTransactionMeta Meta { get; init; }
}

sealed class ManifestTradeRpcTransactionMeta
{
	[JsonProperty("err")]
	public ManifestTradeRpcTransactionError Error { get; init; }

	[JsonProperty("fee")]
	public ulong Fee { get; init; }

	[JsonProperty("logMessages")]
	public string[] LogMessages { get; init; }
}

[JsonConverter(typeof(ManifestTradeRpcTransactionErrorConverter))]
sealed class ManifestTradeRpcTransactionError
{
}

sealed class ManifestTradeRpcTransactionErrorConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(ManifestTradeRpcTransactionError);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		reader.Skip();
		return new ManifestTradeRpcTransactionError();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class ManifestTradeRpcRecentFee
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("prioritizationFee")]
	public ulong PrioritizationFee { get; init; }
}

sealed class ManifestTradeRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(ManifestTradeRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case ManifestTradeRpcEmptyParameters:
				break;
			case ManifestTradeRpcAddressAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			case ManifestTradeRpcAddressesAccountParameters accounts:
				serializer.Serialize(writer, accounts.Addresses);
				serializer.Serialize(writer, accounts.Config);
				break;
			case ManifestTradeRpcAddressCommitmentParameters address:
				writer.WriteValue(address.Address);
				serializer.Serialize(writer, address.Config);
				break;
			case ManifestTradeRpcLatestBlockhashParameters blockhash:
				serializer.Serialize(writer, blockhash.Config);
				break;
			case ManifestTradeRpcSignaturesParameters signatures:
				writer.WriteValue(signatures.Address);
				serializer.Serialize(writer, signatures.Config);
				break;
			case ManifestTradeRpcTransactionParameters transaction:
				writer.WriteValue(transaction.Signature);
				serializer.Serialize(writer, transaction.Config);
				break;
			case ManifestTradeRpcSendTransactionParameters send:
				writer.WriteValue(send.Transaction);
				serializer.Serialize(writer, send.Config);
				break;
			case ManifestTradeRpcRecentFeesParameters fees:
				serializer.Serialize(writer, fees.Addresses);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
