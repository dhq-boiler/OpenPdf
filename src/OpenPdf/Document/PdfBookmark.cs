using OpenPdf.IO;
using OpenPdf.Objects;

namespace OpenPdf.Document;

public sealed class PdfBookmark
{
    public string Title { get; set; } = "";
    public int PageIndex { get; set; }
    public double? Y { get; set; }
    public List<PdfBookmark> Children { get; } = new();

    public PdfBookmark() { }

    public PdfBookmark(string title, int pageIndex, double? y = null)
    {
        Title = title;
        PageIndex = pageIndex;
        Y = y;
    }
}

public sealed class PdfOutlineBuilder
{
    private readonly List<PdfBookmark> _bookmarks = new();

    public PdfBookmark Add(string title, int pageIndex, double? y = null)
    {
        var bookmark = new PdfBookmark(title, pageIndex, y);
        _bookmarks.Add(bookmark);
        return bookmark;
    }

    public PdfIndirectReference? Build(PdfWriter writer, List<PdfIndirectReference> pageRefs)
    {
        if (_bookmarks.Count == 0) return null;

        var outlineDict = new PdfDictionary();
        outlineDict["Type"] = new PdfName("Outlines");
        var outlineRef = writer.AddObject(outlineDict);

        var childRefs = BuildLevel(_bookmarks, writer, outlineRef, pageRefs);
        if (childRefs.Count > 0)
        {
            outlineDict["First"] = childRefs.First();
            outlineDict["Last"] = childRefs.Last();
            outlineDict["Count"] = new PdfInteger(CountAll(_bookmarks));
        }

        return outlineRef;
    }

    private List<PdfIndirectReference> BuildLevel(List<PdfBookmark> bookmarks, PdfWriter writer, PdfIndirectReference parent, List<PdfIndirectReference> pageRefs)
    {
        var refs = new List<PdfIndirectReference>();
        for (int i = 0; i < bookmarks.Count; i++)
        {
            var bm = bookmarks[i];
            var dict = new PdfDictionary();
            dict["Title"] = new PdfString(bm.Title);
            dict["Parent"] = parent;

            // Destination: [page /XYZ left top zoom]
            if (bm.PageIndex >= 0 && bm.PageIndex < pageRefs.Count)
            {
                var dest = new PdfArray();
                dest.Add(pageRefs[bm.PageIndex]);
                dest.Add(new PdfName("XYZ"));
                dest.Add(PdfNull.Instance); // left
                dest.Add(bm.Y.HasValue ? (PdfObject)new PdfReal(bm.Y.Value) : PdfNull.Instance);
                dest.Add(PdfNull.Instance); // zoom
                dict["Dest"] = dest;
            }

            var bmRef = writer.AddObject(dict);
            refs.Add(bmRef);

            if (bm.Children.Count > 0)
            {
                var childRefs = BuildLevel(bm.Children, writer, bmRef, pageRefs);
                if (childRefs.Count > 0)
                {
                    dict["First"] = childRefs.First();
                    dict["Last"] = childRefs.Last();
                    dict["Count"] = new PdfInteger(CountAll(bm.Children));
                }
            }
        }

        // Link siblings
        for (int i = 0; i < refs.Count; i++)
        {
            if (i > 0)
            {
                // We need to set Prev/Next but the dicts are already added.
                // Use the writer to get the objects back is not feasible,
                // so we pre-build the sibling refs.
            }
        }

        // Rebuild with sibling links
        refs.Clear();
        var dicts = new List<PdfDictionary>();
        var preRefs = new List<PdfIndirectReference>();

        for (int i = 0; i < bookmarks.Count; i++)
        {
            var bm = bookmarks[i];
            var dict = new PdfDictionary();
            dict["Title"] = new PdfString(bm.Title);
            dict["Parent"] = parent;

            if (bm.PageIndex >= 0 && bm.PageIndex < pageRefs.Count)
            {
                var dest = new PdfArray();
                dest.Add(pageRefs[bm.PageIndex]);
                dest.Add(new PdfName("XYZ"));
                dest.Add(PdfNull.Instance);
                dest.Add(bm.Y.HasValue ? (PdfObject)new PdfReal(bm.Y.Value) : PdfNull.Instance);
                dest.Add(PdfNull.Instance);
                dict["Dest"] = dest;
            }

            dicts.Add(dict);
            preRefs.Add(writer.AddObject(dict));
        }

        // Set sibling links
        for (int i = 0; i < dicts.Count; i++)
        {
            if (i > 0) dicts[i]["Prev"] = preRefs[i - 1];
            if (i < dicts.Count - 1) dicts[i]["Next"] = preRefs[i + 1];

            if (bookmarks[i].Children.Count > 0)
            {
                var childRefs = BuildLevel(bookmarks[i].Children, writer, preRefs[i], pageRefs);
                if (childRefs.Count > 0)
                {
                    dicts[i]["First"] = childRefs.First();
                    dicts[i]["Last"] = childRefs.Last();
                    dicts[i]["Count"] = new PdfInteger(CountAll(bookmarks[i].Children));
                }
            }
        }

        return preRefs;
    }

    private static int CountAll(List<PdfBookmark> bookmarks)
    {
        int count = bookmarks.Count;
        foreach (var bm in bookmarks)
            count += CountAll(bm.Children);
        return count;
    }
}
