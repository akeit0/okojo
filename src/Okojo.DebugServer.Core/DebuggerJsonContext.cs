using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Okojo.DebugServer;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(string))]
internal sealed partial class DebuggerJsonContext : JsonSerializerContext;
