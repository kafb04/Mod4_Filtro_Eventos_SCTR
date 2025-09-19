using System.Text.Json;

namespace Modulo4FilterEventsWinForms
{
    public record OutputDataDto(int id_ied, string filter, bool active);

    public static class JsonMod3
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string BuildString(int idIed, string filter, bool active)
        {
            var wrapper = new { outputData = new OutputDataDto(idIed, filter, active) };
            return JsonSerializer.Serialize(wrapper, Options);
        }

        public static byte[] BuildBytes(int idIed, string filter, bool active)
        {
            var wrapper = new { outputData = new OutputDataDto(idIed, filter, active) };
            return JsonSerializer.SerializeToUtf8Bytes(wrapper, Options);
        }
    }
}
