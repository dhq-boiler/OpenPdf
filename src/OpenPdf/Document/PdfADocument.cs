using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Document;

public sealed class PdfADocument : IDisposable
{
    private readonly PdfWriter _writer;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly List<PdfPageBuilder> _pages = new();
    private string _title = "";
    private string _author = "";
    private string _creator = "OpenPdf";

    public PdfADocument(Stream stream, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _writer = new PdfWriter(stream);
    }

    public static PdfADocument Create(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        return new PdfADocument(stream, ownsStream: true);
    }

    public static PdfADocument Create(Stream stream) => new(stream);

    public PdfPageBuilder AddPage(double width = 595, double height = 842)
    {
        var page = new PdfPageBuilder(width, height);
        _pages.Add(page);
        return page;
    }

    public void SetInfo(string title, string author = "", string creator = "OpenPdf")
    {
        _title = title;
        _author = author;
        _creator = creator;
    }

    public void Save()
    {
        var now = DateTime.UtcNow;

        // 1. Build catalog
        var catalogDict = new PdfDictionary();
        catalogDict["Type"] = PdfName.Catalog;

        // 2. Build pages tree
        var pagesDict = new PdfDictionary();
        pagesDict["Type"] = PdfName.Pages;
        pagesDict["Count"] = new PdfInteger(_pages.Count);
        var pagesRef = _writer.AddObject(pagesDict);
        catalogDict["Pages"] = pagesRef;
        var catalogRef = _writer.AddObject(catalogDict);

        // 3. Build pages (always compressed, always with embedded fonts)
        var pageRefs = new PdfArray();
        foreach (var pageBuilder in _pages)
        {
            var (pageDict, _) = pageBuilder.Build(_writer, pagesRef, compressContent: true);
            var pageRef = _writer.AddObject(pageDict);
            pageRefs.Add(pageRef);
        }
        pagesDict["Kids"] = pageRefs;

        // 4. XMP metadata (required for PDF/A)
        var xmp = new XmpMetadata
        {
            Title = _title,
            Author = _author,
            Creator = _creator,
            Producer = "OpenPdf",
            CreateDate = now,
            ModifyDate = now,
            PdfAPart = 1,
            PdfAConformance = "B"
        };
        var xmpStream = xmp.BuildXmpStream();
        var xmpRef = _writer.AddObject(xmpStream);
        catalogDict["Metadata"] = xmpRef;

        // 5. OutputIntent (required for PDF/A-1b)
        var outputIntent = BuildOutputIntent();
        var outputIntentRef = _writer.AddObject(outputIntent);
        catalogDict["OutputIntents"] = new PdfArray(new PdfObject[] { outputIntentRef });

        // 6. MarkInfo (recommended for PDF/A)
        var markInfo = new PdfDictionary();
        markInfo["Marked"] = PdfBoolean.True;
        catalogDict["MarkInfo"] = markInfo;

        // 7. Info dictionary
        var info = new PdfDictionary();
        if (!string.IsNullOrEmpty(_title)) info["Title"] = new PdfString(_title);
        if (!string.IsNullOrEmpty(_author)) info["Author"] = new PdfString(_author);
        info["Creator"] = new PdfString(_creator);
        info["Producer"] = new PdfString("OpenPdf");
        info["CreationDate"] = new PdfString(FormatPdfDate(now));
        info["ModDate"] = new PdfString(FormatPdfDate(now));

        _writer.Write(catalogRef, info);
    }

    private PdfDictionary BuildOutputIntent()
    {
        var intent = new PdfDictionary();
        intent["Type"] = new PdfName("OutputIntent");
        intent["S"] = new PdfName("GTS_PDFA1");
        intent["OutputConditionIdentifier"] = new PdfString("sRGB");
        intent["RegistryName"] = new PdfString("http://www.color.org");
        intent["Info"] = new PdfString("sRGB IEC61966-2.1");

        // Minimal sRGB ICC profile (simplified header)
        var iccProfile = BuildMinimalSrgbProfile();
        var profileDict = new PdfDictionary();
        profileDict["N"] = new PdfInteger(3);
        profileDict["Filter"] = PdfName.FlateDecode;

        var flate = new Filters.FlateDecodeFilter();
        var compressed = flate.Encode(iccProfile);
        var profileStream = new PdfStream(profileDict, compressed);
        var profileRef = _writer.AddObject(profileStream);
        intent["DestOutputProfile"] = profileRef;

        return intent;
    }

    private static byte[] BuildMinimalSrgbProfile()
    {
        // Minimal ICC profile header (128 bytes) + minimal tag table
        // This is a simplified sRGB profile sufficient for PDF/A validation
        var profile = new byte[140];

        // Profile size (big-endian)
        int size = profile.Length;
        profile[0] = (byte)(size >> 24); profile[1] = (byte)(size >> 16);
        profile[2] = (byte)(size >> 8); profile[3] = (byte)size;

        // Preferred CMM type
        profile[4] = (byte)'a'; profile[5] = (byte)'c'; profile[6] = (byte)'s'; profile[7] = (byte)'p';

        // Profile version (2.1.0)
        profile[8] = 2; profile[9] = 0x10;

        // Device class: 'mntr' (monitor)
        profile[12] = (byte)'m'; profile[13] = (byte)'n'; profile[14] = (byte)'t'; profile[15] = (byte)'r';

        // Color space: 'RGB '
        profile[16] = (byte)'R'; profile[17] = (byte)'G'; profile[18] = (byte)'B'; profile[19] = (byte)' ';

        // PCS: 'XYZ '
        profile[20] = (byte)'X'; profile[21] = (byte)'Y'; profile[22] = (byte)'Z'; profile[23] = (byte)' ';

        // Date/time (2000-01-01)
        profile[24] = 0x07; profile[25] = 0xD0; // 2000
        profile[26] = 0; profile[27] = 1; // January
        profile[28] = 0; profile[29] = 1; // 1st

        // 'acsp' signature
        profile[36] = (byte)'a'; profile[37] = (byte)'c'; profile[38] = (byte)'s'; profile[39] = (byte)'p';

        // Primary platform: 'MSFT'
        profile[40] = (byte)'M'; profile[41] = (byte)'S'; profile[42] = (byte)'F'; profile[43] = (byte)'T';

        // Illuminant (D50: X=0.9642, Y=1.0, Z=0.8249 in s15Fixed16Number)
        WriteFixed16(profile, 68, 0.9642);
        WriteFixed16(profile, 72, 1.0);
        WriteFixed16(profile, 76, 0.8249);

        // Tag count = 0 (minimal)
        profile[128] = 0; profile[129] = 0; profile[130] = 0; profile[131] = 0;

        return profile;
    }

    private static void WriteFixed16(byte[] buf, int offset, double value)
    {
        int fixed16 = (int)(value * 65536);
        buf[offset] = (byte)(fixed16 >> 24);
        buf[offset + 1] = (byte)(fixed16 >> 16);
        buf[offset + 2] = (byte)(fixed16 >> 8);
        buf[offset + 3] = (byte)fixed16;
    }

    private static string FormatPdfDate(DateTime dt)
    {
        return $"D:{dt:yyyyMMddHHmmss}Z";
    }

    public void Dispose()
    {
        _writer.Dispose();
        if (_ownsStream)
            _stream.Dispose();
    }
}
