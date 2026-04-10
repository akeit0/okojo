using System.Text.Json;

namespace Okojo.DebugServer.Tests;

[NonParallelizable]
public sealed partial class OkojoDebugServerIntegrationTests
{
    private static string? GetString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetNestedString(JsonElement payload, string parentPropertyName, string propertyName)
    {
        return payload.TryGetProperty(parentPropertyName, out var parent) &&
               parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(propertyName, out var child) &&
               child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
    }

    private static int? GetNestedInt(JsonElement payload, string parentPropertyName, string propertyName)
    {
        return payload.TryGetProperty(parentPropertyName, out var parent) &&
               parent.ValueKind == JsonValueKind.Object &&
               parent.TryGetProperty(propertyName, out var child) &&
               child.ValueKind == JsonValueKind.Number &&
               child.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static int? GetInt(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private sealed class TempWorkspace : IAsyncDisposable
    {
        public TempWorkspace(string source)
        {
            Root = Path.Combine(Path.GetTempPath(), "okojo-debugserver-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ScriptPath = Path.Combine(Root, "sample.js");
            File.WriteAllText(ScriptPath, source.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        }

        public string Root { get; }
        public string ScriptPath { get; }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempModuleWorkspace : IAsyncDisposable
    {
        public TempModuleWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "okojo-debugserver-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "lib"));
            EntryPath = Path.Combine(Root, "entry.mjs");
        }

        public string Root { get; }
        public string EntryPath { get; }

        public void WriteFile(string relativePath, string source)
        {
            string fullPath = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, source.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
