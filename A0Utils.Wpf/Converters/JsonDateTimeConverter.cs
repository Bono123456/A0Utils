using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A0Utils.Wpf.Converters
{
    internal sealed class JsonDateTimeConverter : JsonConverter<DateTime>
    {
        private const string Format = "dd-MM-yyyy"; // Define custom date format

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.ParseExact(reader.GetString(), Format, null);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(Format));
    }

}
