using Newtonsoft.Json;

namespace OpenShift
{
    public partial class OpenShiftApiClient
    {
        partial void UpdateJsonSerializerSettings(Newtonsoft.Json.JsonSerializerSettings settings)
        {
            settings.Converters.Add(new PatchConverter());
        }

        private class PatchConverter : JsonConverter<Patch>
        {
            public override void WriteJson(JsonWriter writer, Patch patch, JsonSerializer serializer)
            {
                serializer.Serialize(writer, patch.Value);
            }

            public override Patch ReadJson(JsonReader reader, Type objectType, Patch existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }

    public partial class Patch
    {
        public object Value { get; private set; }

        private Patch(object value) => Value = value;

        public static Patch CreateFrom(object value) => new Patch(value);
    }
}