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

public static class PdfOutlineReader
{
    /// <summary>
    /// Reads the outline/bookmark tree from an existing PDF.
    /// Returns an empty list if the PDF has no outlines.
    /// </summary>
    public static List<PdfBookmark> Read(IPdfReader reader)
    {
        var result = new List<PdfBookmark>();
        try
        {
            var catalog = reader.GetCatalog();
            if (catalog == null) return result;

            var outlinesRef = catalog["Outlines"];
            if (outlinesRef == null) return result;

            var outlinesDict = reader.ResolveReference(outlinesRef) as PdfDictionary;
            if (outlinesDict == null) return result;

            var firstRef = outlinesDict["First"];
            if (firstRef == null) return result;

            var allPages = reader.GetAllPages();
            ReadLevel(reader, firstRef, result, allPages);
        }
        catch
        {
            // Silently ignore errors in outline parsing.
        }

        return result;
    }

    private static void ReadLevel(IPdfReader reader, PdfObject firstRef, List<PdfBookmark> items, List<PdfPage> allPages)
    {
        var currentObj = firstRef;
        int safetyLimit = 10000;

        while (currentObj != null && safetyLimit-- > 0)
        {
            var dict = reader.ResolveReference(currentObj) as PdfDictionary;
            if (dict == null) break;

            var bookmark = new PdfBookmark();

            var titleObj = reader.ResolveReference(dict["Title"]) as PdfString;
            if (titleObj != null)
                bookmark.Title = DecodeTitle(titleObj);

            bookmark.PageIndex = ResolveDestination(reader, dict, allPages);
            items.Add(bookmark);

            var childFirst = dict["First"];
            if (childFirst != null)
                ReadLevel(reader, childFirst, bookmark.Children, allPages);

            currentObj = dict["Next"];
        }
    }

    private static string DecodeTitle(PdfString pdfStr)
    {
        var bytes = pdfStr.Value;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return pdfStr.GetText();
    }

    private static int ResolveDestination(IPdfReader reader, PdfDictionary dict, List<PdfPage> allPages)
    {
        // Try direct Dest entry
        var destObj = dict["Dest"];
        if (destObj != null)
        {
            int idx = ResolveDestValue(reader, destObj, allPages);
            if (idx >= 0) return idx;
        }

        // Try Action dictionary (GoTo action)
        var action = reader.ResolveReference(dict["A"]) as PdfDictionary;
        if (action != null)
        {
            var actionType = action.GetName("S");
            if (actionType == null || actionType == "GoTo")
            {
                var actionDest = action["D"];
                if (actionDest != null)
                {
                    int idx = ResolveDestValue(reader, actionDest, allPages);
                    if (idx >= 0) return idx;
                }
            }
        }

        return 0;
    }

    private static int ResolveDestValue(IPdfReader reader, PdfObject destObj, List<PdfPage> allPages)
    {
        destObj = reader.ResolveReference(destObj)!;
        if (destObj == null) return -1;

        // Direct array destination: [pageRef /XYZ left top zoom]
        if (destObj is PdfArray destArray && destArray.Count > 0)
            return ResolvePageIndex(reader, destArray[0], allPages);

        // Named destination (PdfString or PdfName) — look up in Names/Dests tree
        string? destName = null;
        if (destObj is PdfString pdfStr)
            destName = DecodeTitle(pdfStr);
        else if (destObj is PdfName pdfName)
            destName = pdfName.Value;

        if (destName != null)
        {
            var resolved = LookupNamedDestination(reader, destName);
            if (resolved is PdfArray namedArray && namedArray.Count > 0)
                return ResolvePageIndex(reader, namedArray[0], allPages);
            if (resolved is PdfDictionary namedDict)
            {
                var d = reader.ResolveReference(namedDict["D"]) as PdfArray;
                if (d != null && d.Count > 0)
                    return ResolvePageIndex(reader, d[0], allPages);
            }
        }

        return -1;
    }

    private static PdfObject? LookupNamedDestination(IPdfReader reader, string name)
    {
        var catalog = reader.GetCatalog();
        if (catalog == null) return null;

        // Try Catalog → Names → Dests (name tree, PDF 1.2+)
        var namesDict = reader.ResolveReference(catalog["Names"]) as PdfDictionary;
        if (namesDict != null)
        {
            var destsTree = reader.ResolveReference(namesDict["Dests"]);
            if (destsTree is PdfDictionary destsNode)
            {
                var result = SearchNameTree(reader, destsNode, name);
                if (result != null) return result;
            }
        }

        // Try Catalog → Dests (old-style dictionary, PDF 1.1)
        var destsDict = reader.ResolveReference(catalog["Dests"]) as PdfDictionary;
        if (destsDict != null)
        {
            var result = reader.ResolveReference(destsDict[name]);
            if (result != null) return result;
        }

        return null;
    }

    private static PdfObject? SearchNameTree(IPdfReader reader, PdfDictionary node, string name)
    {
        // Check Names array (leaf node)
        var names = reader.ResolveReference(node["Names"]) as PdfArray;
        if (names != null)
        {
            for (int i = 0; i + 1 < names.Count; i += 2)
            {
                string? key = null;
                if (names[i] is PdfString keyStr)
                    key = DecodeTitle(keyStr);
                else if (names[i] is PdfName keyName)
                    key = keyName.Value;

                if (key == name)
                    return reader.ResolveReference(names[i + 1]);
            }
        }

        // Check Kids array (intermediate node)
        var kids = reader.ResolveReference(node["Kids"]) as PdfArray;
        if (kids != null)
        {
            foreach (var kid in kids.Items)
            {
                var kidNode = reader.ResolveReference(kid) as PdfDictionary;
                if (kidNode == null) continue;

                // Check Limits to skip irrelevant subtrees
                var limits = reader.ResolveReference(kidNode["Limits"]) as PdfArray;
                if (limits != null && limits.Count >= 2)
                {
                    string? lower = (limits[0] as PdfString)?.GetText();
                    string? upper = (limits[1] as PdfString)?.GetText();
                    if (lower != null && upper != null)
                    {
                        if (string.Compare(name, lower, StringComparison.Ordinal) < 0 ||
                            string.Compare(name, upper, StringComparison.Ordinal) > 0)
                            continue;
                    }
                }

                var result = SearchNameTree(reader, kidNode, name);
                if (result != null) return result;
            }
        }

        return null;
    }

    private static int ResolvePageIndex(IPdfReader reader, PdfObject pageRef, List<PdfPage> allPages)
    {
        if (pageRef is PdfIndirectReference indRef)
        {
            var pageDict = reader.GetObject(indRef.ObjectNumber) as PdfDictionary;
            if (pageDict != null)
            {
                for (int i = 0; i < allPages.Count; i++)
                {
                    if (ReferenceEquals(allPages[i].Dictionary, pageDict))
                        return i;
                }
            }
        }
        else if (pageRef is PdfInteger intVal)
        {
            return (int)intVal.Value;
        }

        return -1;
    }
}
