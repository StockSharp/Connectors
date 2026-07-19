namespace StockSharp.PumpSwap.Native.Model;

sealed class PumpSwapRpcRequest<TParameters>
	where TParameters : PumpSwapRpcParameters
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

sealed class PumpSwapRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public PumpSwapRpcError Error { get; init; }
}

sealed class PumpSwapRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(PumpSwapRpcParametersConverter))]
abstract class PumpSwapRpcParameters
{
}

sealed class PumpSwapRpcEmptyParameters : PumpSwapRpcParameters
{
}

sealed class PumpSwapRpcAddressAccountParameters : PumpSwapRpcParameters
{
	public string Address { get; init; }
	public PumpSwapRpcAccountConfig Config { get; init; }
}

sealed class PumpSwapRpcAddressesAccountParameters : PumpSwapRpcParameters
{
	public string[] Addresses { get; init; }
	public PumpSwapRpcAccountConfig Config { get; init; }
}

sealed class PumpSwapRpcAddressCommitmentParameters : PumpSwapRpcParameters
{
	public string Address { get; init; }
	public PumpSwapRpcCommitmentConfig Config { get; init; }
}

sealed class PumpSwapRpcLatestBlockhashParameters : PumpSwapRpcParameters
{
	public PumpSwapRpcCommitmentConfig Config { get; init; }
}

sealed class PumpSwapRpcSignaturesParameters : PumpSwapRpcParameters
{
	public string Address { get; init; }
	public PumpSwapRpcSignaturesConfig Config { get; init; }
}

sealed class PumpSwapRpcTransactionParameters : PumpSwapRpcParameters
{
	public string Signature { get; init; }
	public PumpSwapRpcTransactionConfig Config { get; init; }
}

sealed class PumpSwapRpcSendTransactionParameters : PumpSwapRpcParameters
{
	public string Transaction { get; init; }
	public PumpSwapRpcSendConfig Config { get; init; }
}

sealed class PumpSwapRpcSignatureStatusesParameters : PumpSwapRpcParameters
{
	public string[] Signatures { get; init; }
	public PumpSwapRpcStatusConfig Config { get; init; }
}

sealed class PumpSwapRpcRecentFeesParameters : PumpSwapRpcParameters
{
	public string[] Addresses { get; init; }
}

sealed class PumpSwapRpcAccountConfig
{
	[JsonProperty("encoding")]
	public PumpSwapEncodings Encoding { get; init; } =
		PumpSwapEncodings.Base64;

	[JsonProperty("commitment")]
	public PumpSwapCommitments Commitment { get; init; } =
		PumpSwapCommitments.Confirmed;
}

sealed class PumpSwapRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public PumpSwapCommitments Commitment { get; init; } =
		PumpSwapCommitments.Confirmed;
}

sealed class PumpSwapRpcSignaturesConfig
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("before")]
	public string Before { get; init; }

	[JsonProperty("until")]
	public string Until { get; init; }

	[JsonProperty("commitment")]
	public PumpSwapCommitments Commitment { get; init; } =
		PumpSwapCommitments.Confirmed;
}

sealed class PumpSwapRpcTransactionConfig
{
	[JsonProperty("encoding")]
	public PumpSwapEncodings Encoding { get; init; } = PumpSwapEncodings.Json;

	[JsonProperty("commitment")]
	public PumpSwapCommitments Commitment { get; init; } =
		PumpSwapCommitments.Confirmed;

	[JsonProperty("maxSupportedTransactionVersion")]
	public int MaximumSupportedTransactionVersion { get; init; }
}

sealed class PumpSwapRpcSendConfig
{
	[JsonProperty("encoding")]
	public PumpSwapEncodings Encoding { get; init; } =
		PumpSwapEncodings.Base64;

	[JsonProperty("skipPreflight")]
	public bool IsPreflightSkipped { get; init; }

	[JsonProperty("preflightCommitment")]
	public PumpSwapCommitments PreflightCommitment { get; init; } =
		PumpSwapCommitments.Confirmed;

	[JsonProperty("maxRetries")]
	public int MaximumRetries { get; init; }
}

sealed class PumpSwapRpcStatusConfig
{
	[JsonProperty("searchTransactionHistory")]
	public bool IsTransactionHistorySearched { get; init; }
}

sealed class PumpSwapRpcContext
{
	[JsonProperty("slot")]
	public long Slot { get; init; }
}

sealed class PumpSwapRpcContextValue<TResult>
{
	[JsonProperty("context")]
	public PumpSwapRpcContext Context { get; init; }

	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class PumpSwapRpcAccount
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

sealed class PumpSwapRpcLatestBlockhash
{
	[JsonProperty("blockhash")]
	public string Blockhash { get; init; }

	[JsonProperty("lastValidBlockHeight")]
	public long LastValidBlockHeight { get; init; }
}

sealed class PumpSwapRpcSignatureInfo
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("err")]
	public PumpSwapRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public PumpSwapCommitments? ConfirmationStatus { get; init; }
}

sealed class PumpSwapRpcTransaction
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("meta")]
	public PumpSwapRpcTransactionMeta Meta { get; init; }
}

sealed class PumpSwapRpcTransactionMeta
{
	[JsonProperty("err")]
	public PumpSwapRpcTransactionError Error { get; init; }

	[JsonProperty("fee")]
	public ulong Fee { get; init; }

	[JsonProperty("logMessages")]
	public string[] LogMessages { get; init; }
}

[JsonConverter(typeof(PumpSwapRpcTransactionErrorConverter))]
sealed class PumpSwapRpcTransactionError
{
}

sealed class PumpSwapRpcTransactionErrorConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(PumpSwapRpcTransactionError);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		reader.Skip();
		return new PumpSwapRpcTransactionError();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class PumpSwapRpcSignatureStatus
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("confirmations")]
	public ulong? Confirmations { get; init; }

	[JsonProperty("err")]
	public PumpSwapRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public PumpSwapCommitments? ConfirmationStatus { get; init; }
}

sealed class PumpSwapRpcRecentFee
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("prioritizationFee")]
	public ulong PrioritizationFee { get; init; }
}

sealed class PumpSwapRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(PumpSwapRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case PumpSwapRpcEmptyParameters:
				break;
			case PumpSwapRpcAddressAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			case PumpSwapRpcAddressesAccountParameters accounts:
				serializer.Serialize(writer, accounts.Addresses);
				serializer.Serialize(writer, accounts.Config);
				break;
			case PumpSwapRpcAddressCommitmentParameters address:
				writer.WriteValue(address.Address);
				serializer.Serialize(writer, address.Config);
				break;
			case PumpSwapRpcLatestBlockhashParameters blockhash:
				serializer.Serialize(writer, blockhash.Config);
				break;
			case PumpSwapRpcSignaturesParameters signatures:
				writer.WriteValue(signatures.Address);
				serializer.Serialize(writer, signatures.Config);
				break;
			case PumpSwapRpcTransactionParameters transaction:
				writer.WriteValue(transaction.Signature);
				serializer.Serialize(writer, transaction.Config);
				break;
			case PumpSwapRpcSendTransactionParameters send:
				writer.WriteValue(send.Transaction);
				serializer.Serialize(writer, send.Config);
				break;
			case PumpSwapRpcSignatureStatusesParameters statuses:
				serializer.Serialize(writer, statuses.Signatures);
				serializer.Serialize(writer, statuses.Config);
				break;
			case PumpSwapRpcRecentFeesParameters fees:
				serializer.Serialize(writer, fees.Addresses);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
