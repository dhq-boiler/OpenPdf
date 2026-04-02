using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public static class PdfMerger
{
    public static void Merge(IEnumerable<Stream> inputStreams, Stream output)
    {
        var readers = inputStreams.Select(s => PdfReader.Open(s)).ToList();
        try
        {
            Merge(readers, output);
        }
        finally
        {
            foreach (var reader in readers)
                reader.Dispose();
        }
    }

    public static void Merge(IEnumerable<string> inputPaths, string outputPath)
    {
        var streams = inputPaths.Select(p => (Stream)new FileStream(p, FileMode.Open, FileAccess.Read)).ToList();
        try
        {
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            Merge(streams, output);
        }
        finally
        {
            foreach (var s in streams)
                s.Dispose();
        }
    }

    private static void Merge(List<PdfReader> readers, Stream output)
    {
        var writer = new PdfWriter(output);

        var pagesDict = new PdfDictionary();
        pagesDict["Type"] = PdfName.Pages;
        var pagesRef = writer.AddObject(pagesDict);

        var kids = new PdfArray();
        int totalPages = 0;

        foreach (var reader in readers)
        {
            var copier = new PdfObjectCopierForMerge(reader, writer);
            for (int i = 0; i < reader.PageCount; i++)
            {
                var page = reader.GetPage(i);
                var copiedPage = copier.CopyPageDict(page.Dictionary, pagesRef);
                var pageRef = writer.AddObject(copiedPage);
                kids.Add(pageRef);
                totalPages++;
            }
        }

        pagesDict["Kids"] = kids;
        pagesDict["Count"] = new PdfInteger(totalPages);

        var catalogDict = new PdfDictionary();
        catalogDict["Type"] = PdfName.Catalog;
        catalogDict["Pages"] = pagesRef;
        var catalogRef = writer.AddObject(catalogDict);

        writer.Write(catalogRef);
    }
}

internal sealed class PdfObjectCopierForMerge
{
    private readonly PdfReader _reader;
    private readonly PdfWriter _writer;
    private readonly Dictionary<int, PdfIndirectReference> _objectMap = new();

    public PdfObjectCopierForMerge(PdfReader reader, PdfWriter writer)
    {
        _reader = reader;
        _writer = writer;
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

    private PdfObject DeepCopy(PdfObject obj)
    {
        switch (obj)
        {
            case PdfIndirectReference reference:
            {
                if (_objectMap.TryGetValue(reference.ObjectNumber, out var mapped))
                    return mapped;
                var resolved = _reader.GetObject(reference.ObjectNumber);
                if (resolved == null)
                    return reference;
                var copied = DeepCopy(resolved);
                var newRef = _writer.AddObject(copied);
                _objectMap[reference.ObjectNumber] = newRef;
                return newRef;
            }
            case PdfDictionary dict:
            {
                var result = new PdfDictionary();
                foreach (var kvp in dict.Entries)
                    result[kvp.Key] = DeepCopy(kvp.Value);
                return result;
            }
            case PdfArray array:
            {
                var result = new PdfArray();
                foreach (var item in array.Items)
                    result.Add(DeepCopy(item));
                return result;
            }
            case PdfStream stream:
            {
                var newDict = new PdfDictionary();
                foreach (var kvp in stream.Dictionary.Entries)
                    newDict[kvp.Key] = DeepCopy(kvp.Value);
                return new PdfStream(newDict, stream.Data);
            }
            default:
                return obj;
        }
    }
}
