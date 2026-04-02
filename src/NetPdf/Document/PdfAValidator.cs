using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class PdfAValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class PdfAValidator
{
    private readonly IPdfReader _reader;

    public PdfAValidator(IPdfReader reader)
    {
        _reader = reader;
    }

    public PdfAValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateCatalog(errors, warnings);
        ValidateXmpMetadata(errors, warnings);
        ValidateOutputIntents(errors, warnings);
        ValidateFonts(errors, warnings);
        ValidateNoTransparency(errors, warnings);

        return new PdfAValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private void ValidateCatalog(List<string> errors, List<string> warnings)
    {
        var catalog = _reader.GetCatalog();
        if (catalog == null)
        {
            errors.Add("Missing document catalog");
            return;
        }

        // MarkInfo
        var markInfo = catalog.Get<PdfDictionary>("MarkInfo");
        if (markInfo == null)
        {
            var markRef = catalog["MarkInfo"];
            markInfo = _reader.ResolveReference(markRef) as PdfDictionary;
        }
        if (markInfo == null)
            warnings.Add("Missing MarkInfo dictionary (recommended for PDF/A)");
    }

    private void ValidateXmpMetadata(List<string> errors, List<string> warnings)
    {
        var catalog = _reader.GetCatalog();
        if (catalog == null) return;

        var metadataRef = catalog["Metadata"];
        if (metadataRef == null)
        {
            errors.Add("Missing XMP metadata stream (required for PDF/A)");
            return;
        }

        var metadataStream = _reader.ResolveReference(metadataRef) as PdfStream;
        if (metadataStream == null)
        {
            errors.Add("Metadata entry does not reference a stream");
            return;
        }

        var data = _reader.DecodeStream(metadataStream);
        var text = System.Text.Encoding.UTF8.GetString(data);

        if (!text.Contains("pdfaid:part"))
            errors.Add("XMP metadata missing PDF/A identification (pdfaid:part)");
        if (!text.Contains("pdfaid:conformance"))
            warnings.Add("XMP metadata missing PDF/A conformance level (pdfaid:conformance)");
    }

    private void ValidateOutputIntents(List<string> errors, List<string> warnings)
    {
        var catalog = _reader.GetCatalog();
        if (catalog == null) return;

        var outputIntents = catalog.Get<PdfArray>("OutputIntents");
        if (outputIntents == null)
        {
            var oiRef = catalog["OutputIntents"];
            outputIntents = _reader.ResolveReference(oiRef) as PdfArray;
        }

        if (outputIntents == null || outputIntents.Count == 0)
        {
            errors.Add("Missing OutputIntents (required for PDF/A)");
            return;
        }

        bool hasGtsPdfa1 = false;
        foreach (var item in outputIntents.Items)
        {
            var dict = _reader.ResolveReference(item) as PdfDictionary;
            if (dict == null) continue;
            var s = dict.GetName("S");
            if (s == "GTS_PDFA1") hasGtsPdfa1 = true;
        }
        if (!hasGtsPdfa1)
            errors.Add("No OutputIntent with S=GTS_PDFA1 found");
    }

    private void ValidateFonts(List<string> errors, List<string> warnings)
    {
        for (int i = 0; i < _reader.PageCount; i++)
        {
            var page = _reader.GetPage(i);
            var resources = page.Dictionary.Get<PdfDictionary>("Resources");
            if (resources == null)
            {
                var resRef = page.Dictionary["Resources"];
                resources = _reader.ResolveReference(resRef) as PdfDictionary;
            }
            if (resources == null) continue;

            var fontDict = resources.Get<PdfDictionary>("Font");
            if (fontDict == null)
            {
                var fontRef = resources["Font"];
                fontDict = _reader.ResolveReference(fontRef) as PdfDictionary;
            }
            if (fontDict == null) continue;

            foreach (var kvp in fontDict.Entries)
            {
                var fontObj = _reader.ResolveReference(kvp.Value) as PdfDictionary;
                if (fontObj == null) continue;

                var subtype = fontObj.GetName("Subtype");

                // Type1 standard 14 fonts: check if embedded
                if (subtype == "Type1")
                {
                    var descriptorRef = fontObj["FontDescriptor"];
                    var descriptor = _reader.ResolveReference(descriptorRef) as PdfDictionary;
                    if (descriptor == null)
                    {
                        warnings.Add($"Page {i + 1}, Font /{kvp.Key}: no FontDescriptor (standard 14 fonts should be embedded for PDF/A)");
                        continue;
                    }
                    bool hasFile = descriptor["FontFile"] != null ||
                                   descriptor["FontFile2"] != null ||
                                   descriptor["FontFile3"] != null;
                    if (!hasFile)
                        warnings.Add($"Page {i + 1}, Font /{kvp.Key}: font not embedded (required for PDF/A)");
                }
            }
        }
    }

    private void ValidateNoTransparency(List<string> errors, List<string> warnings)
    {
        for (int i = 0; i < _reader.PageCount; i++)
        {
            var page = _reader.GetPage(i);
            var group = page.Dictionary.Get<PdfDictionary>("Group");
            if (group == null)
            {
                var groupRef = page.Dictionary["Group"];
                group = _reader.ResolveReference(groupRef) as PdfDictionary;
            }
            if (group != null)
            {
                var groupSubtype = group.GetName("S");
                if (groupSubtype == "Transparency")
                    errors.Add($"Page {i + 1}: transparency group found (prohibited in PDF/A-1)");
            }
        }
    }
}
