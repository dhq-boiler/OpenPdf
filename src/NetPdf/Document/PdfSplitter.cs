namespace NetPdf.Document;

public static class PdfSplitter
{
    public static void Split(Stream input, Stream output, int startPage, int endPage)
    {
        using var reader = PdfReader.Open(input);
        Split(reader, output, startPage, endPage);
    }

    public static void Split(string inputPath, string outputPath, int startPage, int endPage)
    {
        using var reader = PdfReader.Open(inputPath);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        Split(reader, output, startPage, endPage);
    }

    public static void SplitEach(string inputPath, string outputDirectory, string fileNamePattern = "page_{0}.pdf")
    {
        using var reader = PdfReader.Open(inputPath);
        Directory.CreateDirectory(outputDirectory);
        for (int i = 0; i < reader.PageCount; i++)
        {
            var outputPath = Path.Combine(outputDirectory, string.Format(fileNamePattern, i + 1));
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            Split(reader, output, i, i);
        }
    }

    private static void Split(PdfReader reader, Stream output, int startPage, int endPage)
    {
        if (startPage < 0) startPage = 0;
        if (endPage >= reader.PageCount) endPage = reader.PageCount - 1;
        if (startPage > endPage)
            throw new ArgumentException("startPage must be <= endPage");

        var pageIndices = Enumerable.Range(startPage, endPage - startPage + 1).ToList();
        var copier = new PdfObjectCopier(reader);
        copier.CopyPages(pageIndices, output);
    }
}
