using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Okojo.DocGenerator.Cli;

internal sealed record XmlDocParamComment(string Name, string Text);

internal sealed record XmlDocComment(
    string Summary,
    string Remarks,
    string Returns,
    IReadOnlyList<XmlDocParamComment> Parameters);

internal static class XmlDocCommentReader
{
    public static XmlDocComment Read(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: default) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(xml))
            return new(string.Empty, string.Empty, string.Empty, Array.Empty<XmlDocParamComment>());

        try
        {
            var root = XElement.Parse(xml);
            var parameters = root.Elements("param")
                .Select(static element => new XmlDocParamComment(
                    element.Attribute("name")?.Value ?? string.Empty,
                    NormalizeText(element)))
                .Where(static x => x.Name.Length != 0 && x.Text.Length != 0)
                .ToArray();
            return new(
                NormalizeText(root.Element("summary")),
                NormalizeText(root.Element("remarks")),
                NormalizeText(root.Element("returns")),
                parameters);
        }
        catch
        {
            return new(string.Empty, string.Empty, string.Empty, Array.Empty<XmlDocParamComment>());
        }
    }

    private static string NormalizeText(XElement? element)
    {
        if (element is null)
            return string.Empty;

        var text = string.Join(" ", element
            .DescendantNodesAndSelf()
            .OfType<XText>()
            .Select(static x => x.Value.Trim())
            .Where(static x => x.Length != 0));
        return text.Trim();
    }
}
