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
            var copier = new PdfObjectCopier(reader, writer);
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
