using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Okojo.DotNet.Modules;

public readonly record struct DotNetFileBasedAppCacheKey(string ApplicationName, string Fingerprint)
{
    public string RunFileDirectoryName => $"{ApplicationName}-{Fingerprint}";

    public static DotNetFileBasedAppCacheKey Create(
        string sourcePath,
        IEnumerable<DotNetModuleReference> moduleReferences,
        string sdkVersion,
        string? implicitBuildFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(moduleReferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkVersion);

        var appName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(appName))
            appName = "app";

        var orderedReferences = new List<DotNetModuleReference>();
        foreach (var moduleReference in moduleReferences)
            orderedReferences.Add(moduleReference);
        orderedReferences.Sort(static (left, right) =>
        {
            var kind = left.Kind.CompareTo(right.Kind);
            if (kind != 0)
                return kind;

            var specifier = string.Compare(left.Specifier, right.Specifier, StringComparison.Ordinal);
            if (specifier != 0)
                return specifier;

            return string.Compare(left.Version, right.Version, StringComparison.Ordinal);
        });

        var buffer = new StringBuilder();
        buffer.Append("sdk=").AppendLine(sdkVersion.Trim());
        buffer.Append("implicit=").AppendLine(implicitBuildFingerprint?.Trim() ?? string.Empty);
        for (var i = 0; i < orderedReferences.Count; i++)
        {
            var moduleReference = orderedReferences[i];
            buffer.Append(moduleReference.Kind)
                .Append('=')
                .Append(moduleReference.Specifier)
                .Append('@')
                .AppendLine(moduleReference.Version ?? string.Empty);
        }

        return new(SanitizeApplicationName(appName), ComputeSha256Hex(buffer.ToString()));
    }

    private static string SanitizeApplicationName(string applicationName)
    {
        var builder = new StringBuilder(applicationName.Length);
        foreach (var ch in applicationName)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        return builder.Length == 0 ? "app" : builder.ToString();
    }

    private static string ComputeSha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
