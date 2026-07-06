using System.Text;
using OpenPdf.IO;

namespace OpenPdf.Tests.IO;

public class XrefReaderTests
{
    /// <summary>
    /// Linearized PDFs (and a handful of legacy writers) pack `trailer` and
    /// its dictionary onto one line:
    /// `trailer&lt;&lt;/Root 1 0 R/Size 3&gt;&gt;`.
    /// The reader must not consume the dict together with the keyword; it
    /// used to, which produced a null Trailer and a downstream NRE in
    /// PdfReader.GetCatalog.
    /// </summary>
    [Fact]
    public void TrailerDictOnSameLineAsKeyword_ParsesRoot()
    {
        // Minimal well-formed classic-xref PDF where trailer sits on one line
        // with its dict.
        const string pdf =
            "%PDF-1.4\n" +
            "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n" +
            "2 0 obj\n<</Type/Pages/Count 0/Kids[]>>\nendobj\n" +
            "xref\n" +
            "0 3\n" +
            "0000000000 65535 f\r\n" +
            "0000000009 00000 n\r\n" +
            "0000000054 00000 n\r\n" +
            "trailer<</Size 3/Root 1 0 R>>\n" +
            "startxref\n" +
            "104\n" +
            "%%EOF\n";
        var bytes = Encoding.ASCII.GetBytes(pdf);
        // Locate the actual `xref` offset because inline construction is
        // brittle — anchor to it so the test isn't sensitive to whitespace.
        int xrefOffset = Encoding.ASCII.GetString(bytes).IndexOf("xref\n", StringComparison.Ordinal);
        var withOffset = pdf.Replace("startxref\n104\n", $"startxref\n{xrefOffset}\n");
        bytes = Encoding.ASCII.GetBytes(withOffset);

        using var ms = new MemoryStream(bytes);
        var reader = new XrefReader(ms);
        var table = reader.Read();
        Assert.NotNull(table.Trailer);
        Assert.NotNull(table.Trailer!["Root"]);
    }
}
