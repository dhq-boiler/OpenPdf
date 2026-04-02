using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Document;

internal sealed class PdfObjectCopier
{
    private readonly PdfReader _reader;
    private readonly Dictionary<int, PdfIndirectReference> _objectMap = new();
    private PdfWriter? _writer;

    public PdfObjectCopier(PdfReader reader)
    {
        _reader = reader;
    }

    public PdfObjectCopier(PdfReader reader, PdfWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public void CopyPages(List<int> pageIndices, Stream output)
    {
        _writer = new PdfWriter(output);
        _objectMap.Clear();

        var pagesDict = new PdfDictionary();
        pagesDict["Type"] = PdfName.Pages;
        pagesDict["Count"] = new PdfInteger(pageIndices.Count);
        var pagesRef = _writer.AddObject(pagesDict);

        var kids = new PdfArray();
        foreach (var pageIndex in pageIndices)
        {
            var sourcePage = _reader.GetPage(pageIndex);
            var copiedPageDict = CopyPageDict(sourcePage.Dictionary, pagesRef);
            var pageRef = _writer.AddObject(copiedPageDict);
            kids.Add(pageRef);
        }
        pagesDict["Kids"] = kids;

        var catalogDict = new PdfDictionary();
        catalogDict["Type"] = PdfName.Catalog;
        catalogDict["Pages"] = pagesRef;
        var catalogRef = _writer.AddObject(catalogDict);

        _writer.Write(catalogRef);
    }

    public PdfDictionary CopyPageDict(PdfDictionary source, PdfIndirectReference newParent)
    {
        var result = new PdfDictionary();
        foreach (var kvp in source.Entries)
        {
            if (kvp.Key == "Parent")
                result["Parent"] = newParent;
            else
                result[kvp.Key] = DeepCopy(kvp.Value);
        }
        return result;
    }

    public PdfObject DeepCopy(PdfObject obj) => DeepCopy(obj, 0);

    private PdfObject DeepCopy(PdfObject obj, int depth)
    {
        if (depth > PdfLimits.MaxRecursionDepth)
            throw new InvalidDataException("Object nesting exceeds maximum depth. Possible circular reference.");

        switch (obj)
        {
            case PdfIndirectReference reference:
            {
                if (_objectMap.TryGetValue(reference.ObjectNumber, out var mapped))
                    return mapped;
                var resolved = _reader.GetObject(reference.ObjectNumber);
                if (resolved == null)
                    return reference;
                var copied = DeepCopy(resolved, depth + 1);
                var newRef = _writer!.AddObject(copied);
                _objectMap[reference.ObjectNumber] = newRef;
                return newRef;
            }

            case PdfDictionary dict:
            {
                var result = new PdfDictionary();
                foreach (var kvp in dict.Entries)
                    result[kvp.Key] = DeepCopy(kvp.Value, depth + 1);
                return result;
            }

            case PdfArray array:
            {
                var result = new PdfArray();
                foreach (var item in array.Items)
                    result.Add(DeepCopy(item, depth + 1));
                return result;
            }

            case PdfStream stream:
            {
                var newDict = new PdfDictionary();
                foreach (var kvp in stream.Dictionary.Entries)
                    newDict[kvp.Key] = DeepCopy(kvp.Value, depth + 1);
                return new PdfStream(newDict, stream.Data);
            }

            default:
                return obj;
        }
    }
}
