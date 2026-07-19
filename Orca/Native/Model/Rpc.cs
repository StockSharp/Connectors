namespace StockSharp.Orca.Native.Model;

sealed class OrcaRpcRequest<TParameters>
	where TParameters : OrcaRpcParameters
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

sealed class OrcaRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public OrcaRpcError Error { get; init; }
}

sealed class OrcaRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(OrcaRpcParametersConverter))]
abstract class OrcaRpcParameters
{
}

sealed class OrcaRpcEmptyParameters : OrcaRpcParameters
{
}

sealed class OrcaRpcAddressAccountParameters : OrcaRpcParameters
{
	public string Address { get; init; }
	public OrcaRpcAccountConfig Config { get; init; }
}

sealed class OrcaRpcAddressesAccountParameters : OrcaRpcParameters
{
	public string[] Addresses { get; init; }
	public OrcaRpcAccountConfig Config { get; init; }
}

sealed class OrcaRpcAddressCommitmentParameters : OrcaRpcParameters
{
	public string Address { get; init; }
	public OrcaRpcCommitmentConfig Config { get; init; }
}

sealed class OrcaRpcLatestBlockhashParameters : OrcaRpcParameters
{
	public OrcaRpcCommitmentConfig Config { get; init; }
}

sealed class OrcaRpcSignaturesParameters : OrcaRpcParameters
{
	public string Address { get; init; }
	public OrcaRpcSignaturesConfig Config { get; init; }
}

sealed class OrcaRpcTransactionParameters : OrcaRpcParameters
{
	public string Signature { get; init; }
	public OrcaRpcTransactionConfig Config { get; init; }
}

sealed class OrcaRpcSendTransactionParameters : OrcaRpcParameters
{
	public string Transaction { get; init; }
	public OrcaRpcSendConfig Config { get; init; }
}

sealed class OrcaRpcRecentFeesParameters : OrcaRpcParameters
{
	public string[] Addresses { get; init; }
}

sealed class OrcaRpcAccountConfig
{
	[JsonProperty("encoding")]
	public OrcaEncodings Encoding { get; init; } = OrcaEncodings.Base64;

	[JsonProperty("commitment")]
	public OrcaCommitments Commitment { get; init; } =
		OrcaCommitments.Confirmed;
}

sealed class OrcaRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public OrcaCommitments Commitment { get; init; } =
		OrcaCommitments.Confirmed;
}

sealed class OrcaRpcSignaturesConfig
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("before")]
	public string Before { get; init; }

	[JsonProperty("until")]
	public string Until { get; init; }

	[JsonProperty("commitment")]
	public OrcaCommitments Commitment { get; init; } =
		OrcaCommitments.Confirmed;
}

sealed class OrcaRpcTransactionConfig
{
	[JsonProperty("encoding")]
	public OrcaEncodings Encoding { get; init; } = OrcaEncodings.Json;

	[JsonProperty("commitment")]
	public OrcaCommitments Commitment { get; init; } =
		OrcaCommitments.Confirmed;

	[JsonProperty("maxSupportedTransactionVersion")]
	public int MaximumSupportedTransactionVersion { get; init; }
}

sealed class OrcaRpcSendConfig
{
	[JsonProperty("encoding")]
	public OrcaEncodings Encoding { get; init; } = OrcaEncodings.Base64;

	[JsonProperty("skipPreflight")]
	public bool IsPreflightSkipped { get; init; }

	[JsonProperty("preflightCommitment")]
	public OrcaCommitments PreflightCommitment { get; init; } =
		OrcaCommitments.Confirmed;

	[JsonProperty("maxRetries")]
	public int MaximumRetries { get; init; }
}

sealed class OrcaRpcContext
{
	[JsonProperty("slot")]
	public long Slot { get; init; }
}

sealed class OrcaRpcContextValue<TResult>
{
	[JsonProperty("context")]
	public OrcaRpcContext Context { get; init; }

	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class OrcaRpcAccount
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

sealed class OrcaRpcLatestBlockhash
{
	[JsonProperty("blockhash")]
	public string Blockhash { get; init; }

	[JsonProperty("lastValidBlockHeight")]
	public long LastValidBlockHeight { get; init; }
}

sealed class OrcaRpcSignatureInfo
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("err")]
	public OrcaRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public OrcaCommitments? ConfirmationStatus { get; init; }
}

sealed class OrcaRpcTransaction
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("meta")]
	public OrcaRpcTransactionMeta Meta { get; init; }
}

sealed class OrcaRpcTransactionMeta
{
	[JsonProperty("err")]
	public OrcaRpcTransactionError Error { get; init; }

	[JsonProperty("fee")]
	public ulong Fee { get; init; }

	[JsonProperty("logMessages")]
	public string[] LogMessages { get; init; }
}

[JsonConverter(typeof(OrcaRpcTransactionErrorConverter))]
sealed class OrcaRpcTransactionError
{
}

sealed class OrcaRpcTransactionErrorConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(OrcaRpcTransactionError);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		reader.Skip();
		return new OrcaRpcTransactionError();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class OrcaRpcRecentFee
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("prioritizationFee")]
	public ulong PrioritizationFee { get; init; }
}

sealed class OrcaRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(OrcaRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case OrcaRpcEmptyParameters:
				break;
			case OrcaRpcAddressAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			case OrcaRpcAddressesAccountParameters accounts:
				serializer.Serialize(writer, accounts.Addresses);
				serializer.Serialize(writer, accounts.Config);
				break;
			case OrcaRpcAddressCommitmentParameters address:
				writer.WriteValue(address.Address);
				serializer.Serialize(writer, address.Config);
				break;
			case OrcaRpcLatestBlockhashParameters blockhash:
				serializer.Serialize(writer, blockhash.Config);
				break;
			case OrcaRpcSignaturesParameters signatures:
				writer.WriteValue(signatures.Address);
				serializer.Serialize(writer, signatures.Config);
				break;
			case OrcaRpcTransactionParameters transaction:
				writer.WriteValue(transaction.Signature);
				serializer.Serialize(writer, transaction.Config);
				break;
			case OrcaRpcSendTransactionParameters send:
				writer.WriteValue(send.Transaction);
				serializer.Serialize(writer, send.Config);
				break;
			case OrcaRpcRecentFeesParameters fees:
				serializer.Serialize(writer, fees.Addresses);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
