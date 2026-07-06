using OpenPdf.Objects;

namespace OpenPdf.Document;

/// <summary>
/// Walks a page's content stream (including nested Form XObjects) and yields
/// every raster image drawn on it, together with its page-space bounding
/// rectangle so downstream consumers (OCR, region-of-interest editors) know
/// where each image lives.
///
/// <para>
/// The extractor is passive — it does not touch the image data other than
/// packaging it into a container the caller can hand to a decoder. Formats
/// with a natural container (JPEG, JPEG2000) pass through unchanged; raw
/// pixel arrays are wrapped in a minimal BMP so callers can treat the output
/// uniformly. CCITT-encoded scans are wrapped in TIFF, which is what most
/// image libraries expect.
/// </para>
/// </summary>
public sealed class ImageExtractor
{
    private readonly PdfReader _reader;

    public ImageExtractor(PdfReader reader)
    {
        _reader = reader;
    }

    public List<ExtractedImage> Extract(int pageIndex)
    {
        var page = _reader.GetPage(pageIndex);
        var results = new List<ExtractedImage>();
        var resources = ResolveDict(page.Dictionary["Resources"]);
        var contentBytes = GetContentBytes(page.Dictionary["Contents"]);
        if (contentBytes.Length == 0) return results;

        ProcessContent(contentBytes, resources, Matrix.Identity, pageIndex, results, depth: 0);
        return results;
    }

    public List<ExtractedImage> ExtractAll()
    {
        var all = new List<ExtractedImage>();
        for (int i = 0; i < _reader.PageCount; i++)
            all.AddRange(Extract(i));
        return all;
    }

    // --- Content stream walker ---------------------------------------------

    private void ProcessContent(
        byte[] contentBytes,
        PdfDictionary? resources,
        Matrix parentCtm,
        int pageIndex,
        List<ExtractedImage> outList,
        int depth)
    {
        if (depth > 8) return; // Guard against pathological XObject recursion.

        var text = PdfEncoding.Latin1.GetString(contentBytes);
        var tokens = ContentStreamTokenizer.Tokenize(text);

        var ctmStack = new Stack<Matrix>();
        var ctm = parentCtm;
        var stack = new List<string>();

        foreach (var (type, value) in tokens)
        {
            if (type == "operator")
            {
                switch (value)
                {
                    case "q":
                        ctmStack.Push(ctm);
                        stack.Clear();
                        break;
                    case "Q":
                        if (ctmStack.Count > 0) ctm = ctmStack.Pop();
                        stack.Clear();
                        break;
                    case "cm":
                        if (stack.Count >= 6 && TryParse6(stack, out var m))
                            ctm = m.Multiply(ctm);
                        stack.Clear();
                        break;
                    case "Do":
                        if (stack.Count >= 1)
                            HandleDo(stack[stack.Count - 1], resources, ctm, pageIndex, outList, depth);
                        stack.Clear();
                        break;
                    default:
                        stack.Clear();
                        break;
                }
            }
            else
            {
                stack.Add(value);
            }
        }
    }

    private void HandleDo(
        string nameOperand,
        PdfDictionary? resources,
        Matrix ctm,
        int pageIndex,
        List<ExtractedImage> outList,
        int depth)
    {
        if (resources == null) return;
        string name = nameOperand.StartsWith("/") ? nameOperand.Substring(1) : nameOperand;

        var xobjectDict = ResolveDict(resources["XObject"]);
        if (xobjectDict == null) return;
        var target = _reader.ResolveReference(xobjectDict[name]) as PdfStream;
        if (target == null) return;

        var subtype = target.Dictionary.GetName("Subtype");
        if (subtype == "Image")
        {
            var img = BuildExtractedImage(target, name, ctm, pageIndex);
            if (img != null) outList.Add(img);
        }
        else if (subtype == "Form")
        {
            // Form XObjects carry their own /Matrix and /Resources. The
            // effective drawing transform is FormMatrix * outer CTM.
            var formMatrix = ReadMatrix(target.Dictionary["Matrix"]) ?? Matrix.Identity;
            var innerResources = ResolveDict(target.Dictionary["Resources"]) ?? resources;
            var innerBytes = _reader.DecodeStream(target);
            ProcessContent(innerBytes, innerResources, formMatrix.Multiply(ctm), pageIndex, outList, depth + 1);
        }
    }

    private ExtractedImage? BuildExtractedImage(PdfStream stream, string name, Matrix ctm, int pageIndex)
    {
        var d = stream.Dictionary;
        int width = (int)d.GetInt("Width", 0);
        int height = (int)d.GetInt("Height", 0);
        int bpc = (int)d.GetInt("BitsPerComponent", 8);
        var csObj = _reader.ResolveReference(d["ColorSpace"]);
        string? colorSpace = csObj switch
        {
            PdfName n => n.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfName nn => nn.Value,
            _ => null,
        };

        var filters = GetFilterChain(d);
        byte[] data;
        string format;

        // DecodeStream handles encryption and unknown filters transparently
        // — DCTDecode / JPXDecode / CCITTFaxDecode aren't registered as
        // decompressors, so the bytes we get back are the still-encoded image
        // payload, ready to embed in a JPEG / JP2 / TIFF container. Only
        // filters that ARE registered (FlateDecode + predictors) actually
        // decompress.
        var decoded = _reader.DecodeStream(stream);
        if (filters.Contains("DCTDecode"))
        {
            data = decoded;
            format = "jpeg";
        }
        else if (filters.Contains("JPXDecode"))
        {
            data = decoded;
            format = "jpeg2000";
        }
        else if (filters.Contains("CCITTFaxDecode"))
        {
            data = WrapCcittAsTiff(stream, decoded, width, height, bpc);
            format = "tiff";
        }
        else
        {
            data = WrapRawAsBmp(decoded, width, height, bpc, colorSpace);
            format = "bmp";
        }

        // Page-space bounds: CTM maps the unit square (0,0)-(1,1) into page
        // coords. Take min/max of the four corners so rotated/flipped images
        // still get a sensible axis-aligned bounding box.
        var (x0, y0) = ctm.Apply(0, 0);
        var (x1, y1) = ctm.Apply(1, 0);
        var (x2, y2) = ctm.Apply(0, 1);
        var (x3, y3) = ctm.Apply(1, 1);
        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        double maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        double maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));

        return new ExtractedImage(
            pageIndex, name, format, data,
            width, height, bpc, colorSpace,
            minX, minY, maxX - minX, maxY - minY);
    }

    private static List<string> GetFilterChain(PdfDictionary d)
    {
        var result = new List<string>();
        var f = d["Filter"];
        if (f is PdfName n) result.Add(n.Value);
        else if (f is PdfArray a)
        {
            foreach (var item in a.Items)
                if (item is PdfName nn) result.Add(nn.Value);
        }
        return result;
    }

    // --- Image wrapping ----------------------------------------------------

    private static byte[] WrapRawAsBmp(byte[] pixels, int width, int height, int bpc, string? colorSpace)
    {
        // Only handle common CJK-scan cases: 8bpc DeviceRGB, 8bpc DeviceGray,
        // and 1bpc DeviceGray (rare, since scanners tend to CCITTFaxDecode
        // instead). Other pixel formats round-trip as best effort.
        if (width <= 0 || height <= 0) return pixels;
        bool isRgb = colorSpace is "DeviceRGB" or "CalRGB";
        bool isGray = colorSpace is "DeviceGray" or "CalGray" or null;
        if (!isRgb && !isGray) return pixels;

        int bppOut = isRgb ? 24 : (bpc == 1 ? 1 : 8);
        int rowBytesPacked = (width * bppOut + 7) / 8;
        int rowBytesPadded = ((rowBytesPacked + 3) / 4) * 4;
        int paletteEntries = bppOut == 24 ? 0 : (1 << bppOut);
        int paletteBytes = paletteEntries * 4;
        int pixelDataSize = rowBytesPadded * height;
        int pixelOffset = 14 + 40 + paletteBytes;
        int fileSize = pixelOffset + pixelDataSize;

        var bmp = new byte[fileSize];
        // File header
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt32(bmp, 10, pixelOffset);
        // DIB header (BITMAPINFOHEADER)
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, width);
        WriteInt32(bmp, 22, height); // positive = bottom-up
        bmp[26] = 1; bmp[27] = 0; // planes = 1
        bmp[28] = (byte)bppOut; bmp[29] = 0;
        WriteInt32(bmp, 30, 0); // BI_RGB no compression
        WriteInt32(bmp, 34, pixelDataSize);
        WriteInt32(bmp, 38, 2835); // 72 DPI ≈ 2835 px/m
        WriteInt32(bmp, 42, 2835);
        WriteInt32(bmp, 46, paletteEntries);
        WriteInt32(bmp, 50, 0);
        // Palette
        if (bppOut == 8)
        {
            for (int i = 0; i < 256; i++)
            {
                int o = 54 + i * 4;
                bmp[o] = bmp[o + 1] = bmp[o + 2] = (byte)i;
                bmp[o + 3] = 0;
            }
        }
        else if (bppOut == 1)
        {
            // 0 = black, 1 = white (default PDF DeviceGray convention).
            int o = 54; bmp[o + 3] = 0;
            o += 4; bmp[o] = bmp[o + 1] = bmp[o + 2] = 0xFF; bmp[o + 3] = 0;
        }

        // Pixel rows, bottom-up
        for (int row = 0; row < height; row++)
        {
            int srcRow = row * rowBytesPacked;
            int dstRow = pixelOffset + (height - 1 - row) * rowBytesPadded;
            if (bppOut == 24)
            {
                // PDF gives RGB, BMP wants BGR
                for (int x = 0; x < width; x++)
                {
                    int s = srcRow + x * 3;
                    int t = dstRow + x * 3;
                    if (s + 2 >= pixels.Length) break;
                    bmp[t] = pixels[s + 2];
                    bmp[t + 1] = pixels[s + 1];
                    bmp[t + 2] = pixels[s];
                }
            }
            else
            {
                int len = Math.Min(rowBytesPacked, Math.Max(0, pixels.Length - srcRow));
                if (len > 0) Buffer.BlockCopy(pixels, srcRow, bmp, dstRow, len);
            }
        }
        return bmp;
    }

    private static byte[] WrapCcittAsTiff(PdfStream stream, byte[] payload, int width, int height, int bpc)
    {
        // Emit a minimal little-endian TIFF with one strip pointing at the
        // CCITT-encoded bytes exactly as they appear in the PDF stream.
        // DecodeParms determines G3-1D / G3-2D / G4.
        var parms = _readerParmsFor(stream);
        int k = (int)(parms?.GetInt("K", 0) ?? 0);
        bool blackIs1 = (parms?["BlackIs1"] is PdfBoolean b && b.Value);
        bool encodedByteAlign = (parms?["EncodedByteAlign"] is PdfBoolean e && e.Value);
        int compression = k < 0 ? 4 : 3; // 4 = Group 4, 3 = Group 3

        int numTags = 8;
        int ifdOffset = 8;
        int ifdSize = 2 + numTags * 12 + 4;
        int stripOffset = ifdOffset + ifdSize;

        var tiff = new byte[stripOffset + payload.Length];
        // Header: II 42 offset
        tiff[0] = (byte)'I'; tiff[1] = (byte)'I';
        WriteInt16LE(tiff, 2, 42);
        WriteInt32LE(tiff, 4, ifdOffset);

        int p = ifdOffset;
        WriteInt16LE(tiff, p, (short)numTags); p += 2;
        // 256 ImageWidth  SHORT/LONG 1 width
        WriteTag(tiff, ref p, 256, 3, 1, width);
        // 257 ImageLength SHORT/LONG 1 height
        WriteTag(tiff, ref p, 257, 3, 1, height);
        // 258 BitsPerSample SHORT 1 bpc
        WriteTag(tiff, ref p, 258, 3, 1, bpc);
        // 259 Compression SHORT 1 (3 or 4)
        WriteTag(tiff, ref p, 259, 3, 1, compression);
        // 262 PhotometricInterpretation SHORT 1 (0 = WhiteIsZero, 1 = BlackIsZero)
        WriteTag(tiff, ref p, 262, 3, 1, blackIs1 ? 1 : 0);
        // 273 StripOffsets LONG 1 stripOffset
        WriteTag(tiff, ref p, 273, 4, 1, stripOffset);
        // 278 RowsPerStrip LONG 1 height
        WriteTag(tiff, ref p, 278, 4, 1, height);
        // 279 StripByteCounts LONG 1 payload.Length
        WriteTag(tiff, ref p, 279, 4, 1, payload.Length);
        WriteInt32LE(tiff, p, 0); // next IFD = 0
        Buffer.BlockCopy(payload, 0, tiff, stripOffset, payload.Length);
        // Ignore encodedByteAlign — most viewers cope; a proper TIFF T.6/T.4
        // extraparams field could carry the option but Tesseract's leptonica
        // handles raw CCITT fine.
        _ = encodedByteAlign;
        return tiff;
    }

    private static PdfDictionary? _readerParmsFor(PdfStream stream)
    {
        var pm = stream.Dictionary["DecodeParms"];
        if (pm is PdfDictionary d) return d;
        if (pm is PdfArray arr && arr.Items.Count > 0 && arr.Items[0] is PdfDictionary d0) return d0;
        return null;
    }

    // --- Helpers -----------------------------------------------------------

    private byte[] GetContentBytes(PdfObject? contentObj)
    {
        var resolved = _reader.ResolveReference(contentObj);
        if (resolved is PdfStream s) return _reader.DecodeStream(s);
        if (resolved is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var it in arr.Items)
            {
                if (_reader.ResolveReference(it) is PdfStream stream)
                {
                    var b = _reader.DecodeStream(stream);
                    ms.Write(b, 0, b.Length);
                    ms.WriteByte((byte)' ');
                }
            }
            return ms.ToArray();
        }
        return Array.Empty<byte>();
    }

    private PdfDictionary? ResolveDict(PdfObject? obj) =>
        obj == null ? null : _reader.ResolveReference(obj) as PdfDictionary;

    private static bool TryParse6(List<string> stack, out Matrix m)
    {
        m = Matrix.Identity;
        int n = stack.Count;
        if (n < 6) return false;
        if (!double.TryParse(stack[n - 6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)) return false;
        if (!double.TryParse(stack[n - 5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)) return false;
        if (!double.TryParse(stack[n - 4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c)) return false;
        if (!double.TryParse(stack[n - 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return false;
        if (!double.TryParse(stack[n - 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e)) return false;
        if (!double.TryParse(stack[n - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) return false;
        m = new Matrix(a, b, c, d, e, f);
        return true;
    }

    private static Matrix? ReadMatrix(PdfObject? o)
    {
        if (o is not PdfArray arr || arr.Items.Count < 6) return null;
        double[] v = new double[6];
        for (int i = 0; i < 6; i++)
        {
            v[i] = arr.Items[i] switch
            {
                PdfInteger pi => pi.Value,
                PdfReal pr => pr.Value,
                _ => 0
            };
        }
        return new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    private static void WriteInt32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
    private static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
    private static void WriteInt32LE(byte[] buf, int offset, int value) => WriteInt32(buf, offset, value);
    private static void WriteTag(byte[] buf, ref int p, short tag, short type, int count, int value)
    {
        WriteInt16LE(buf, p, tag); p += 2;
        WriteInt16LE(buf, p, type); p += 2;
        WriteInt32LE(buf, p, count); p += 4;
        WriteInt32LE(buf, p, value); p += 4;
    }

    /// <summary>2D affine transform used by PDF content streams.</summary>
    internal readonly struct Matrix
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);
        public double A { get; }
        public double B { get; }
        public double C { get; }
        public double D { get; }
        public double E { get; }
        public double F { get; }
        public Matrix(double a, double b, double c, double d, double e, double f)
        { A = a; B = b; C = c; D = d; E = e; F = f; }

        // PDF matrix convention: new = self * other
        // [A B 0]   [oA oB 0]   [A*oA+B*oC   A*oB+B*oD    0]
        // [C D 0] * [oC oD 0] = [C*oA+D*oC   C*oB+D*oD    0]
        // [E F 1]   [oE oF 1]   [E*oA+F*oC+oE E*oB+F*oD+oF 1]
        public Matrix Multiply(Matrix o) => new(
            A * o.A + B * o.C,
            A * o.B + B * o.D,
            C * o.A + D * o.C,
            C * o.B + D * o.D,
            E * o.A + F * o.C + o.E,
            E * o.B + F * o.D + o.F);

        public (double X, double Y) Apply(double x, double y) =>
            (x * A + y * C + E, x * B + y * D + F);
    }
}
