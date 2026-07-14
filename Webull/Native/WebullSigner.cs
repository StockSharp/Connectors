namespace StockSharp.Webull.Native;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using Ecng.Common;

static class WebullSigner
{
	public static string Sign(string path, IEnumerable<KeyValuePair<string, string>> query, string body, string appKey, string appSecret, string host, string timestamp, string nonce)
	{
		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["host"] = host, ["x-app-key"] = appKey, ["x-signature-algorithm"] = "HMAC-SHA1",
			["x-signature-nonce"] = nonce, ["x-signature-version"] = "1.0", ["x-timestamp"] = timestamp,
		};
		foreach (var pair in query)
			parameters[pair.Key] = pair.Value;
		var source = path + "&" + parameters.Select(p => $"{p.Key}={p.Value}").Join("&");
		if (!body.IsEmpty())
		{
			var bodyBytes = body.UTF8();
			source += "&" + MD5.HashData(bodyBytes).Digest();
		}
		using var hmac = new HMACSHA1((appSecret + "&").UTF8());
		return hmac.ComputeHash(source.DataEscape().UTF8()).Base64();
	}

	public static (string signature, IDictionary<string, string> metadata) SignEvent(byte[] body, string appKey, string appSecret)
	{
		var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
		var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["x-app-key"] = appKey,
			["x-signature-algorithm"] = "HMAC-SHA1",
			["x-signature-version"] = "1.0",
			["x-signature-nonce"] = Guid.NewGuid().ToString(),
			["x-timestamp"] = timestamp,
		};

		var source = metadata.Select(p => $"{p.Key}={p.Value}").Join("=") + "&" + MD5.HashData(body).Digest().ToLowerInvariant();
		using var hmac = new HMACSHA1((appSecret + "&").UTF8());
		return (hmac.ComputeHash(source.DataEscape().UTF8()).Base64(), metadata);
	}
}
