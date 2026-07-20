namespace StockSharp.Meteora.Native.Model;

sealed class MeteoraRpcRequest<TParameters>
	where TParameters : MeteoraRpcParameters
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

sealed class MeteoraRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public MeteoraRpcError Error { get; init; }
}

sealed class MeteoraRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(MeteoraRpcParametersConverter))]
abstract class MeteoraRpcParameters
{
}

sealed class MeteoraRpcEmptyParameters : MeteoraRpcParameters
{
}

sealed class MeteoraRpcAddressAccountParameters : MeteoraRpcParameters
{
	public string Address { get; init; }
	public MeteoraRpcAccountConfig Config { get; init; }
}

sealed class MeteoraRpcAddressesAccountParameters : MeteoraRpcParameters
{
	public string[] Addresses { get; init; }
	public MeteoraRpcAccountConfig Config { get; init; }
}

sealed class MeteoraRpcAddressCommitmentParameters : MeteoraRpcParameters
{
	public string Address { get; init; }
	public MeteoraRpcCommitmentConfig Config { get; init; }
}

sealed class MeteoraRpcLatestBlockhashParameters : MeteoraRpcParameters
{
	public MeteoraRpcCommitmentConfig Config { get; init; }
}

sealed class MeteoraRpcSignaturesParameters : MeteoraRpcParameters
{
	public string Address { get; init; }
	public MeteoraRpcSignaturesConfig Config { get; init; }
}

sealed class MeteoraRpcTransactionParameters : MeteoraRpcParameters
{
	public string Signature { get; init; }
	public MeteoraRpcTransactionConfig Config { get; init; }
}

sealed class MeteoraRpcSendTransactionParameters : MeteoraRpcParameters
{
	public string Transaction { get; init; }
	public MeteoraRpcSendConfig Config { get; init; }
}

sealed class MeteoraRpcRecentFeesParameters : MeteoraRpcParameters
{
	public string[] Addresses { get; init; }
}

sealed class MeteoraRpcAccountConfig
{
	[JsonProperty("encoding")]
	public MeteoraEncodings Encoding { get; init; } = MeteoraEncodings.Base64;

	[JsonProperty("commitment")]
	public MeteoraCommitments Commitment { get; init; } =
		MeteoraCommitments.Confirmed;
}

sealed class MeteoraRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public MeteoraCommitments Commitment { get; init; } =
		MeteoraCommitments.Confirmed;
}

sealed class MeteoraRpcSignaturesConfig
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("before")]
	public string Before { get; init; }

	[JsonProperty("until")]
	public string Until { get; init; }

	[JsonProperty("commitment")]
	public MeteoraCommitments Commitment { get; init; } =
		MeteoraCommitments.Confirmed;
}

sealed class MeteoraRpcTransactionConfig
{
	[JsonProperty("encoding")]
	public MeteoraEncodings Encoding { get; init; } = MeteoraEncodings.Json;

	[JsonProperty("commitment")]
	public MeteoraCommitments Commitment { get; init; } =
		MeteoraCommitments.Confirmed;

	[JsonProperty("maxSupportedTransactionVersion")]
	public int MaximumSupportedTransactionVersion { get; init; }
}

sealed class MeteoraRpcSendConfig
{
	[JsonProperty("encoding")]
	public MeteoraEncodings Encoding { get; init; } = MeteoraEncodings.Base64;

	[JsonProperty("skipPreflight")]
	public bool IsPreflightSkipped { get; init; }

	[JsonProperty("preflightCommitment")]
	public MeteoraCommitments PreflightCommitment { get; init; } =
		MeteoraCommitments.Confirmed;

	[JsonProperty("maxRetries")]
	public int MaximumRetries { get; init; }
}

sealed class MeteoraRpcContext
{
	[JsonProperty("slot")]
	public long Slot { get; init; }
}

sealed class MeteoraRpcContextValue<TResult>
{
	[JsonProperty("context")]
	public MeteoraRpcContext Context { get; init; }

	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class MeteoraRpcAccount
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

sealed class MeteoraRpcLatestBlockhash
{
	[JsonProperty("blockhash")]
	public string Blockhash { get; init; }

	[JsonProperty("lastValidBlockHeight")]
	public long LastValidBlockHeight { get; init; }
}

sealed class MeteoraRpcSignatureInfo
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("err")]
	public MeteoraRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public MeteoraCommitments? ConfirmationStatus { get; init; }
}

sealed class MeteoraRpcTransaction
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("meta")]
	public MeteoraRpcTransactionMeta Meta { get; init; }
}

sealed class MeteoraRpcTransactionMeta
{
	[JsonProperty("err")]
	public MeteoraRpcTransactionError Error { get; init; }

	[JsonProperty("fee")]
	public ulong Fee { get; init; }

	[JsonProperty("logMessages")]
	public string[] LogMessages { get; init; }

	[JsonProperty("innerInstructions")]
	public MeteoraRpcInnerInstructionGroup[] InnerInstructions { get; init; } = [];

	[JsonProperty("loadedAddresses")]
	public MeteoraRpcLoadedAddresses LoadedAddresses { get; init; }
}

sealed class MeteoraRpcInnerInstructionGroup
{
	[JsonProperty("index")]
	public int Index { get; init; }

	[JsonProperty("instructions")]
	public MeteoraRpcInnerInstruction[] Instructions { get; init; } = [];
}

sealed class MeteoraRpcInnerInstruction
{
	[JsonProperty("accounts")]
	public int[] Accounts { get; init; } = [];

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("programIdIndex")]
	public int ProgramIdIndex { get; init; }

	[JsonProperty("stackHeight")]
	public int? StackHeight { get; init; }
}

sealed class MeteoraRpcLoadedAddresses
{
	[JsonProperty("writable")]
	public string[] Writable { get; init; } = [];

	[JsonProperty("readonly")]
	public string[] ReadOnly { get; init; } = [];
}

[JsonConverter(typeof(MeteoraRpcTransactionErrorConverter))]
sealed class MeteoraRpcTransactionError
{
}

sealed class MeteoraRpcTransactionErrorConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(MeteoraRpcTransactionError);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		reader.Skip();
		return new MeteoraRpcTransactionError();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class MeteoraRpcRecentFee
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("prioritizationFee")]
	public ulong PrioritizationFee { get; init; }
}

sealed class MeteoraRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(MeteoraRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case MeteoraRpcEmptyParameters:
				break;
			case MeteoraRpcAddressAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			case MeteoraRpcAddressesAccountParameters accounts:
				serializer.Serialize(writer, accounts.Addresses);
				serializer.Serialize(writer, accounts.Config);
				break;
			case MeteoraRpcAddressCommitmentParameters address:
				writer.WriteValue(address.Address);
				serializer.Serialize(writer, address.Config);
				break;
			case MeteoraRpcLatestBlockhashParameters blockhash:
				serializer.Serialize(writer, blockhash.Config);
				break;
			case MeteoraRpcSignaturesParameters signatures:
				writer.WriteValue(signatures.Address);
				serializer.Serialize(writer, signatures.Config);
				break;
			case MeteoraRpcTransactionParameters transaction:
				writer.WriteValue(transaction.Signature);
				serializer.Serialize(writer, transaction.Config);
				break;
			case MeteoraRpcSendTransactionParameters send:
				writer.WriteValue(send.Transaction);
				serializer.Serialize(writer, send.Config);
				break;
			case MeteoraRpcRecentFeesParameters fees:
				serializer.Serialize(writer, fees.Addresses);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
