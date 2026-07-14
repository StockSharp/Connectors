using Newtonsoft.Json.Linq;

namespace StockSharp.MoexISS.Native;

class JsonIssResponseToObjectConverter : JsonConverter
{
	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;

		reader.Read(); // перейти на metadata

		var metadataJtoken = JToken.Load(reader);
		var columnsJtoken = JToken.Load(reader);
		var dataJtoken = JToken.Load(reader);

		var columnsArray = JArray.Load(columnsJtoken.First.CreateReader());

		var result = new IssResponsePayload
		{
			Columns = [],
			Data = []
		};

		foreach (var column in columnsArray)
		{
			result.Columns.Add(column.Value<string>());
		}

		var dataArray = JArray.Load(dataJtoken.First.CreateReader());

		foreach (var dataItemJson in dataArray)
		{
			var fieldsArray = JArray.Load(dataItemJson.CreateReader());

			var dataItem = new Dictionary<string, string>();
			result.Data.Add(dataItem);

			for (var i = 0; i < fieldsArray.Count; i++)
				dataItem[result.Columns[i]] = fieldsArray[i].Value<string>();
		}

		return result;
	}

	public override bool CanConvert(Type objectType) => true;
}