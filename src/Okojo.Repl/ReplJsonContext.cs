using System.Text.Json;
using System.Text.Json.Serialization;

namespace Okojo.Repl;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class ReplJsonContext : JsonSerializerContext;
