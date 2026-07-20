namespace StockSharp.Raydium.Native.Model;

sealed class RaydiumRpcRequest<TParameters>
	where TParameters : RaydiumRpcParameters
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

sealed class RaydiumRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public RaydiumRpcError Error { get; init; }
}

sealed class RaydiumRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(RaydiumRpcParametersConverter))]
abstract class RaydiumRpcParameters
{
}

sealed class RaydiumRpcEmptyParameters : RaydiumRpcParameters
{
}

sealed class RaydiumRpcAddressAccountParameters : RaydiumRpcParameters
{
	public string Address { get; init; }
	public RaydiumRpcAccountConfig Config { get; init; }
}

sealed class RaydiumRpcAddressesAccountParameters : RaydiumRpcParameters
{
	public string[] Addresses { get; init; }
	public RaydiumRpcAccountConfig Config { get; init; }
}

sealed class RaydiumRpcAddressCommitmentParameters : RaydiumRpcParameters
{
	public string Address { get; init; }
	public RaydiumRpcCommitmentConfig Config { get; init; }
}

sealed class RaydiumRpcSignaturesParameters : RaydiumRpcParameters
{
	public string Address { get; init; }
	public RaydiumRpcSignaturesConfig Config { get; init; }
}

sealed class RaydiumRpcTransactionParameters : RaydiumRpcParameters
{
	public string Signature { get; init; }
	public RaydiumRpcTransactionConfig Config { get; init; }
}

sealed class RaydiumRpcSendTransactionParameters : RaydiumRpcParameters
{
	public string Transaction { get; init; }
	public RaydiumRpcSendConfig Config { get; init; }
}

sealed class RaydiumRpcAccountConfig
{
	[JsonProperty("encoding")]
	public RaydiumEncodings Encoding { get; init; } = RaydiumEncodings.Base64;

	[JsonProperty("commitment")]
	public RaydiumCommitments Commitment { get; init; } =
		RaydiumCommitments.Confirmed;
}

sealed class RaydiumRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public RaydiumCommitments Commitment { get; init; } =
		RaydiumCommitments.Confirmed;
}

sealed class RaydiumRpcSignaturesConfig
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("before")]
	public string Before { get; init; }

	[JsonProperty("until")]
	public string Until { get; init; }

	[JsonProperty("commitment")]
	public RaydiumCommitments Commitment { get; init; } =
		RaydiumCommitments.Confirmed;
}

sealed class RaydiumRpcTransactionConfig
{
	[JsonProperty("encoding")]
	public RaydiumEncodings Encoding { get; init; } = RaydiumEncodings.Json;

	[JsonProperty("commitment")]
	public RaydiumCommitments Commitment { get; init; } =
		RaydiumCommitments.Confirmed;

	[JsonProperty("maxSupportedTransactionVersion")]
	public RaydiumSolanaMessageVersions MessageVersion { get; init; } =
		RaydiumSolanaMessageVersions.V0;
}

sealed class RaydiumRpcSendConfig
{
	[JsonProperty("encoding")]
	public RaydiumEncodings Encoding { get; init; } = RaydiumEncodings.Base64;

	[JsonProperty("skipPreflight")]
	public bool IsPreflightSkipped { get; init; }

	[JsonProperty("preflightCommitment")]
	public RaydiumCommitments PreflightCommitment { get; init; } =
		RaydiumCommitments.Confirmed;

	[JsonProperty("maxRetries")]
	public int MaximumRetries { get; init; }
}

sealed class RaydiumRpcContext
{
	[JsonProperty("slot")]
	public long Slot { get; init; }
}

sealed class RaydiumRpcContextValue<TResult>
{
	[JsonProperty("context")]
	public RaydiumRpcContext Context { get; init; }

	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class RaydiumRpcAccount
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

sealed class RaydiumRpcSignatureInfo
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("err")]
	public RaydiumRpcTransactionError Error { get; init; }

	[JsonProperty("confirmationStatus")]
	public RaydiumCommitments? ConfirmationStatus { get; init; }
}

sealed class RaydiumRpcTransaction
{
	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("blockTime")]
	public long? BlockTime { get; init; }

	[JsonProperty("meta")]
	public RaydiumRpcTransactionMeta Meta { get; init; }

	[JsonProperty("transaction")]
	public RaydiumRpcTransactionData Transaction { get; init; }
}

sealed class RaydiumRpcTransactionMeta
{
	[JsonProperty("err")]
	public RaydiumRpcTransactionError Error { get; init; }

	[JsonProperty("fee")]
	public ulong Fee { get; init; }

	[JsonProperty("logMessages")]
	public string[] LogMessages { get; init; }

	[JsonProperty("loadedAddresses")]
	public RaydiumRpcLoadedAddresses LoadedAddresses { get; init; }

	[JsonProperty("preTokenBalances")]
	public RaydiumRpcTokenBalance[] PreTokenBalances { get; init; }

	[JsonProperty("postTokenBalances")]
	public RaydiumRpcTokenBalance[] PostTokenBalances { get; init; }
}

sealed class RaydiumRpcTransactionData
{
	[JsonProperty("message")]
	public RaydiumRpcTransactionMessage Message { get; init; }

	[JsonProperty("signatures")]
	public string[] Signatures { get; init; }
}

sealed class RaydiumRpcTransactionMessage
{
	[JsonProperty("accountKeys")]
	public string[] AccountKeys { get; init; }
}

sealed class RaydiumRpcLoadedAddresses
{
	[JsonProperty("writable")]
	public string[] Writable { get; init; }

	[JsonProperty("readonly")]
	public string[] ReadOnly { get; init; }
}

sealed class RaydiumRpcTokenBalance
{
	[JsonProperty("accountIndex")]
	public int AccountIndex { get; init; }

	[JsonProperty("mint")]
	public string Mint { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("programId")]
	public string ProgramId { get; init; }

	[JsonProperty("uiTokenAmount")]
	public RaydiumRpcTokenAmount TokenAmount { get; init; }
}

sealed class RaydiumRpcTokenAmount
{
	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("uiAmountString")]
	public string DisplayAmount { get; init; }
}

[JsonConverter(typeof(RaydiumRpcTransactionErrorConverter))]
sealed class RaydiumRpcTransactionError
{
}

sealed class RaydiumRpcTransactionErrorConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(RaydiumRpcTransactionError);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		reader.Skip();
		return new RaydiumRpcTransactionError();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class RaydiumRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(RaydiumRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case RaydiumRpcEmptyParameters:
				break;
			case RaydiumRpcAddressAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			case RaydiumRpcAddressesAccountParameters accounts:
				serializer.Serialize(writer, accounts.Addresses);
				serializer.Serialize(writer, accounts.Config);
				break;
			case RaydiumRpcAddressCommitmentParameters address:
				writer.WriteValue(address.Address);
				serializer.Serialize(writer, address.Config);
				break;
			case RaydiumRpcSignaturesParameters signatures:
				writer.WriteValue(signatures.Address);
				serializer.Serialize(writer, signatures.Config);
				break;
			case RaydiumRpcTransactionParameters transaction:
				writer.WriteValue(transaction.Signature);
				serializer.Serialize(writer, transaction.Config);
				break;
			case RaydiumRpcSendTransactionParameters send:
				writer.WriteValue(send.Transaction);
				serializer.Serialize(writer, send.Config);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
