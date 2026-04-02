using System.Text;
using OpenPdf.Objects;

namespace OpenPdf.Document;

public sealed class XmpMetadata
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Creator { get; set; }
    public string? Producer { get; set; } = "OpenPdf";
    public DateTime? CreateDate { get; set; }
    public DateTime? ModifyDate { get; set; }
    public string? PdfAConformance { get; set; } // "B" for PDF/A-1b, etc.
    public int? PdfAPart { get; set; } // 1, 2, or 3

    public PdfStream BuildXmpStream()
    {
        var xmp = BuildXmpXml();
        var data = Encoding.UTF8.GetBytes(xmp);

        var dict = new PdfDictionary();
        dict["Type"] = new PdfName("Metadata");
        dict["Subtype"] = new PdfName("XML");
        return new PdfStream(dict, data);
    }

    private string BuildXmpXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xpacket begin=\"\xEF\xBB\xBF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        sb.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.AppendLine("    <rdf:Description rdf:about=\"\"");
        sb.AppendLine("      xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        sb.AppendLine("      xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        sb.AppendLine("      xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");
        sb.AppendLine("      xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\"");
        if (PdfAPart.HasValue)
            sb.AppendLine("      xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"");
        sb.AppendLine("    >");

        // dc:title
        if (Title != null)
        {
            sb.AppendLine("      <dc:title>");
            sb.AppendLine("        <rdf:Alt>");
            sb.AppendLine($"          <rdf:li xml:lang=\"x-default\">{EscapeXml(Title)}</rdf:li>");
            sb.AppendLine("        </rdf:Alt>");
            sb.AppendLine("      </dc:title>");
        }

        // dc:creator
        if (Author != null)
        {
            sb.AppendLine("      <dc:creator>");
            sb.AppendLine("        <rdf:Seq>");
            sb.AppendLine($"          <rdf:li>{EscapeXml(Author)}</rdf:li>");
            sb.AppendLine("        </rdf:Seq>");
            sb.AppendLine("      </dc:creator>");
        }

        // dc:description
        if (Subject != null)
        {
            sb.AppendLine("      <dc:description>");
            sb.AppendLine("        <rdf:Alt>");
            sb.AppendLine($"          <rdf:li xml:lang=\"x-default\">{EscapeXml(Subject)}</rdf:li>");
            sb.AppendLine("        </rdf:Alt>");
            sb.AppendLine("      </dc:description>");
        }

        // xmp:CreatorTool
        if (Creator != null)
            sb.AppendLine($"      <xmp:CreatorTool>{EscapeXml(Creator)}</xmp:CreatorTool>");

        // pdf:Producer
        if (Producer != null)
            sb.AppendLine($"      <pdf:Producer>{EscapeXml(Producer)}</pdf:Producer>");

        // xmp:CreateDate
        if (CreateDate.HasValue)
            sb.AppendLine($"      <xmp:CreateDate>{CreateDate.Value:yyyy-MM-ddTHH:mm:sszzz}</xmp:CreateDate>");

        // xmp:ModifyDate
        if (ModifyDate.HasValue)
            sb.AppendLine($"      <xmp:ModifyDate>{ModifyDate.Value:yyyy-MM-ddTHH:mm:sszzz}</xmp:ModifyDate>");

        // PDF/A identification
        if (PdfAPart.HasValue)
        {
            sb.AppendLine($"      <pdfaid:part>{PdfAPart.Value}</pdfaid:part>");
            if (PdfAConformance != null)
                sb.AppendLine($"      <pdfaid:conformance>{EscapeXml(PdfAConformance)}</pdfaid:conformance>");
        }

        sb.AppendLine("    </rdf:Description>");
        sb.AppendLine("  </rdf:RDF>");
        sb.AppendLine("</x:xmpmeta>");
        sb.AppendLine("<?xpacket end=\"w\"?>");
        return sb.ToString();
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

public static class PdfDocumentXmpExtensions
{
    public static void SetXmpMetadata(this PdfDocument doc, XmpMetadata xmp)
    {
        doc.SetXmpMetadataInternal(xmp.BuildXmpStream());
    }
}
