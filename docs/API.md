# OpenPdf API Reference

OpenPdf is a pure C# PDF library for reading, writing, and editing PDF files.
Supports Japanese (CJK) text, font subsetting, annotations, bookmarks, form fields, PDF/A-1b, encryption, barcode/QR code generation, and more.

---

## Table of Contents

- [OpenPdf.Document](#openpdfdocument) - Core PDF creation & manipulation
  - [PdfDocument](#pdfdocument) - Create new PDFs
  - [PdfPageBuilder](#pdfpagebuilder) - Build pages with text, graphics, images
  - [PdfReader](#pdfreader) - Read existing PDFs
  - [PdfEditor](#pdfeditor) - Edit page order
  - [TextExtractor](#textextractor) - Extract text from pages
  - [PdfMerger](#pdfmerger) - Merge multiple PDFs
  - [PdfSplitter](#pdfsplitter) - Split PDFs
  - [TableBuilder](#tablebuilder) - Build tables
  - [TextLayout](#textlayout) - Text wrapping
  - [ListLayout](#listlayout) - Bullet/numbered lists
  - [PdfOutlineBuilder](#pdfoutlinebuilder) - Bookmarks
  - [PdfAnnotation](#pdfannotation) - Annotations (links, highlights)
  - [PageStamper](#pagestamper) - Stamp text on pages
  - [PageDecorator](#pagedecorator) - Headers/footers
  - [PdfADocument](#pdfadocument) - PDF/A-1b compliant documents
  - [AcroFormReader](#acroformreader) - Read form fields
- [OpenPdf.Objects](#openpdfobjects) - PDF primitive types
- [OpenPdf.IO](#openpdfio) - File I/O & parsing
- [OpenPdf.Fonts](#openpdffonts) - Font support
- [OpenPdf.Filters](#openpdffilters) - Stream compression
- [OpenPdf.Barcode](#openpdfbarcode) - Barcode & QR code
- [OpenPdf.Security](#openpdfsecurity) - Encryption

---

## OpenPdf.Document

### PdfDocument

Main class for creating new PDF documents. Implements `IDisposable`.

```csharp
using var doc = PdfDocument.Create("output.pdf");
// or
using var doc = PdfDocument.Create(stream);
```

| Member | Type | Description |
|--------|------|-------------|
| `Create(string path)` | static | Create a new PDF and write to file |
| `Create(Stream stream)` | static | Create a new PDF and write to stream |
| `AddPage(double width, double height)` | `PdfPageBuilder` | Add a page with custom dimensions (points) |
| `SetInfo(string title, string author, string subject, string keywords, string creator, string producer)` | `void` | Set document metadata |
| `Outlines` | `PdfOutlineBuilder` | Access the outline (bookmark) builder |
| `CompressContent` | `bool` | Enable/disable FlateDecode compression (default: true) |
| `Save()` | `void` | Finalize and write the PDF |
| `Dispose()` | `void` | Save and release resources |

**Example:**

```csharp
using var doc = PdfDocument.Create("hello.pdf");
var page = doc.AddPage(595, 842); // A4 size
page.AddFont("Helvetica");
page.SetFont("Helvetica", 12);
page.BeginText();
page.MoveTextPosition(72, 750);
page.ShowText("Hello, World!");
page.EndText();
doc.Save();
```

---

### PdfPageBuilder

Builder for constructing PDF pages with text, graphics, and images.
Obtained from `PdfDocument.AddPage()`.

#### Text Operations

| Method | Description |
|--------|-------------|
| `AddFont(string name)` | Register a standard PDF font (Helvetica, Times-Roman, Courier, etc.) |
| `AddTrueTypeFont(string name, TrueTypeFont font)` | Register a TrueType font |
| `SetFont(string name, double size)` | Set the current font and size |
| `BeginText()` | Start a text block |
| `EndText()` | End a text block |
| `MoveTextPosition(double x, double y)` | Move the text cursor |
| `ShowText(string text)` | Write text using a standard font |
| `ShowUnicodeText(string text)` | Write Unicode text (CJK, etc.) using a CID font |
| `DrawText(string text, double x, double y)` | Convenience: begin, move, show, end in one call |
| `SetLeading(double leading)` | Set line spacing |
| `NextLine()` | Move to the next line |

#### Graphics Operations

| Method | Description |
|--------|-------------|
| `SaveGraphicsState()` | Push current graphics state |
| `RestoreGraphicsState()` | Pop graphics state |
| `SetLineWidth(double width)` | Set stroke line width |
| `SetStrokeColor(double r, double g, double b)` | Set stroke color (RGB, 0.0-1.0) |
| `SetFillColor(double r, double g, double b)` | Set fill color (RGB, 0.0-1.0) |
| `SetStrokeGray(double gray)` | Set stroke grayscale (0.0-1.0) |
| `SetFillGray(double gray)` | Set fill grayscale (0.0-1.0) |
| `SetStrokeCmyk(double c, double m, double y, double k)` | Set stroke color (CMYK) |
| `SetFillCmyk(double c, double m, double y, double k)` | Set fill color (CMYK) |
| `MoveTo(double x, double y)` | Move path cursor |
| `LineTo(double x, double y)` | Draw line segment |
| `CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)` | Draw Bezier curve |
| `Rectangle(double x, double y, double w, double h)` | Define a rectangle path |
| `Stroke()` | Stroke the current path |
| `Fill()` | Fill the current path |
| `FillAndStroke()` | Fill and stroke the current path |
| `ClosePath()` | Close the current subpath |
| `CloseAndStroke()` | Close and stroke |
| `ClipRectangle(double x, double y, double w, double h)` | Set a rectangular clipping region |
| `Clip()` | Clip to current path (non-zero winding) |
| `ClipEvenOdd()` | Clip to current path (even-odd rule) |
| `SetTransparency(double alpha)` | Set transparency (0.0 transparent - 1.0 opaque) |

#### Image Operations

| Method | Returns | Description |
|--------|---------|-------------|
| `AddJpegImage(byte[] data, int width, int height)` | `string` | Register a JPEG image, returns image name |
| `AddPngImage(byte[] data)` | `string` | Register a PNG image, returns image name |
| `AddBmpImage(byte[] data)` | `string` | Register a BMP image, returns image name |
| `AddImageFromFile(string path)` | `string` | Register an image from file (auto-detect format) |
| `DrawImage(string name, double x, double y, double w, double h)` | `void` | Draw a registered image at position/size |

#### Raw Content

| Method | Description |
|--------|-------------|
| `AppendRawContent(string content)` | Append raw PDF content stream operators |

---

### PdfReader

Reads and parses existing PDF files. Implements `IDisposable`.

```csharp
using var reader = PdfReader.Open("input.pdf");
// or
using var reader = PdfReader.Open(stream);
```

| Member | Type | Description |
|--------|------|-------------|
| `Open(string path)` | static | Open a PDF file for reading |
| `Open(Stream stream)` | static | Open a PDF stream for reading |
| `Version` | `string` | PDF version (e.g., "1.4") |
| `PageCount` | `int` | Number of pages |
| `Trailer` | `PdfDictionary` | PDF trailer dictionary |
| `GetObject(int objectNumber)` | `PdfObject` | Get an object by number |
| `ResolveReference(PdfObject obj)` | `PdfObject` | Resolve an indirect reference |
| `GetCatalog()` | `PdfDictionary` | Get the document catalog |
| `GetPage(int index)` | `PdfPage` | Get a page (0-based index) |
| `GetAllPages()` | `IReadOnlyList<PdfPage>` | Get all pages |
| `DecodeStream(PdfStream stream)` | `byte[]` | Decode a PDF stream |

---

### PdfEditor

Edits page order and structure of existing PDFs. Implements `IDisposable`.

```csharp
using var editor = PdfEditor.Open("input.pdf");
editor.DeletePage(0);
editor.MovePage(2, 0);
editor.SaveTo("output.pdf");
```

| Member | Type | Description |
|--------|------|-------------|
| `Open(string path)` | static | Open a PDF for editing |
| `PageCount` | `int` | Number of pages |
| `DeletePage(int index)` | `void` | Delete a page (0-based) |
| `MovePage(int from, int to)` | `void` | Move a page |
| `ReorderPages(int[] order)` | `void` | Reorder all pages |
| `SaveTo(string path)` | `void` | Save to a new file |

---

### TextExtractor

Extracts text content from PDF pages.

```csharp
using var reader = PdfReader.Open("input.pdf");
string text = TextExtractor.ExtractText(reader, 0);
string allText = TextExtractor.ExtractAllText(reader);
```

| Method | Returns | Description |
|--------|---------|-------------|
| `ExtractText(IPdfReader reader, int pageIndex)` | `string` | Extract text from a single page |
| `ExtractAllText(IPdfReader reader)` | `string` | Extract text from all pages |

---

### PdfMerger

Merges multiple PDF files into one. Static class.

```csharp
PdfMerger.Merge(new[] { "file1.pdf", "file2.pdf" }, "merged.pdf");
```

| Method | Description |
|--------|-------------|
| `Merge(string[] inputPaths, string outputPath)` | Merge files by path |
| `Merge(Stream[] inputs, Stream output)` | Merge streams |

---

### PdfSplitter

Splits or extracts page ranges from PDFs. Static class.

```csharp
PdfSplitter.Split("input.pdf", "output.pdf", 0, 4);  // Pages 0-4
PdfSplitter.SplitEach("input.pdf", "output_dir");     // One file per page
```

| Method | Description |
|--------|-------------|
| `Split(string input, string output, int from, int to)` | Extract a page range |
| `SplitEach(string input, string outputDir)` | Split into individual pages |

---

### TableBuilder

Builds and renders tables on PDF pages.

```csharp
var table = new TableBuilder(page, x: 72, y: 700, columnWidths: new[] { 100.0, 200.0, 150.0 });
table.BorderWidth = 0.5;
table.RowHeight = 20;
table.AddHeaderRow("ID", "Name", "Value");
table.AddRow("1", "Alpha", "100");
table.AddRow("2", "Beta", "200");
table.Draw();
```

| Member | Type | Description |
|--------|------|-------------|
| `BorderWidth` | `double` | Table border width |
| `BorderColor` | `(double R, double G, double B)` | Border color |
| `HeaderBackgroundColor` | `(double R, double G, double B)?` | Header row background |
| `RowHeight` | `double` | Height of each row |
| `AddHeaderRow(params string[] cells)` | `void` | Add a header row |
| `AddRow(params string[] cells)` | `void` | Add a data row |
| `AddRow(params TableCell[] cells)` | `void` | Add a row with custom cells |
| `Draw()` | `void` | Render the table to the page |

### TableCell

Custom table cell with styling options.

| Property | Type | Description |
|----------|------|-------------|
| `Text` | `string` | Cell text |
| `ColSpan` | `int` | Column span |
| `RowSpan` | `int` | Row span |
| `PaddingLeft` | `double` | Left padding |
| `PaddingRight` | `double` | Right padding |
| `PaddingTop` | `double` | Top padding |
| `PaddingBottom` | `double` | Bottom padding |
| `BackgroundColor` | `(double R, double G, double B)?` | Cell background color |
| `Alignment` | `HorizontalAlignment` | Text alignment (Left, Center, Right) |

---

### TextLayout

Renders text with automatic word wrapping.

```csharp
double nextY = TextLayout.DrawParagraph(page, "Long text here...", x: 72, y: 700, maxWidth: 450, lineHeight: 14);
```

| Method | Returns | Description |
|--------|---------|-------------|
| `DrawParagraph(PdfPageBuilder page, string text, double x, double y, double maxWidth, double lineHeight)` | `double` | Draw wrapped text, returns Y position after last line |
| `WrapText(string text, string fontName, double fontSize, double maxWidth, PdfPageBuilder page)` | `List<string>` | Split text into wrapped lines |

---

### ListLayout

Renders bullet or numbered lists.

```csharp
var items = new[] { "First item", "Second item", "Third item" };
double nextY = ListLayout.DrawList(page, items, ListStyle.Bullet, x: 72, y: 700, maxWidth: 450, lineHeight: 14);
```

| Member | Type | Description |
|--------|------|-------------|
| `IndentWidth` | `double` | Total indent for list items (default: 20) |
| `BulletIndent` | `double` | Indent for bullet character (default: 0) |
| `BulletChar` | `string` | Custom bullet character |
| `DashChar` | `string` | Custom dash character |
| `DrawList(...)` | `double` | Draw a list, returns Y after last item |

### ListStyle (Enum)

| Value | Description |
|-------|-------------|
| `Bullet` | Bullet list |
| `Numbered` | Numbered list (1. 2. 3.) |
| `Dash` | Dash list |
| `Custom` | Custom bullet character |

---

### PdfOutlineBuilder

Builds PDF bookmarks (document outline).

```csharp
using var doc = PdfDocument.Create("output.pdf");
doc.Outlines.Add(new PdfBookmark("Chapter 1", pageIndex: 0));
doc.Outlines.Add(new PdfBookmark("Chapter 2", pageIndex: 1, children: new List<PdfBookmark>
{
    new PdfBookmark("Section 2.1", pageIndex: 1, y: 500)
}));
```

#### PdfBookmark

| Property | Type | Description |
|----------|------|-------------|
| `Title` | `string` | Bookmark title |
| `PageIndex` | `int` | Target page (0-based) |
| `Y` | `double?` | Vertical position on page |
| `Children` | `List<PdfBookmark>` | Child bookmarks |

#### PdfOutlineBuilder

| Method | Description |
|--------|-------------|
| `Add(PdfBookmark bookmark)` | Add a top-level bookmark |
| `Build(IPdfWriter writer, ...)` | Build and write outlines (called internally) |

---

### PdfAnnotation

Static factory for creating PDF annotations.

```csharp
// Using extension methods on PdfPageBuilder:
page.AddLink(72, 700, 200, 20, "https://example.com");
page.AddTextAnnotation(72, 650, 200, 20, "This is a note");
```

| Method | Description |
|--------|-------------|
| `CreateLink(double x, double y, double w, double h, string uri)` | Create a URI link annotation |
| `CreateTextAnnotation(double x, double y, double w, double h, string content)` | Create a text annotation |
| `CreateHighlight(double x, double y, double w, double h)` | Create a highlight annotation |

#### Extension Methods (PdfPageAnnotationExtensions)

| Method | Description |
|--------|-------------|
| `page.AddAnnotation(PdfDictionary annotation)` | Add a raw annotation |
| `page.AddLink(x, y, w, h, uri)` | Add a link annotation |
| `page.AddTextAnnotation(x, y, w, h, content)` | Add a text note |

---

### PageStamper

Stamps text onto existing PDF pages.

| Method | Description |
|--------|-------------|
| `StampText(...)` | Stamp text at a position on an existing page |

---

### PageDecorator

Adds headers and footers to PDF pages.

```csharp
var decorator = new PageDecorator(page);
decorator.HeaderCenter = "My Document";
decorator.FooterCenter = "Page {0}";
decorator.ShowPageNumber = true;
decorator.Apply();
```

| Property | Type | Description |
|----------|------|-------------|
| `HeaderLeft` | `string` | Left header text |
| `HeaderCenter` | `string` | Center header text |
| `HeaderRight` | `string` | Right header text |
| `FooterLeft` | `string` | Left footer text |
| `FooterCenter` | `string` | Center footer text |
| `FooterRight` | `string` | Right footer text |
| `ShowPageNumber` | `bool` | Show page numbers |
| `PageNumberFormat` | `string` | Format string (e.g., "Page {0}") |
| `HeaderFontSize` | `double` | Header font size |
| `FooterFontSize` | `double` | Footer font size |
| `MarginLeft` | `double` | Left margin |
| `MarginRight` | `double` | Right margin |
| `HeaderY` | `double` | Header Y position |
| `FooterY` | `double` | Footer Y position |
| `Apply()` | `void` | Render headers/footers |

---

### PdfADocument

Creates PDF/A-1b compliant documents. Implements `IDisposable`.

```csharp
using var doc = PdfADocument.Create("output.pdf");
doc.SetInfo("Title", "Author");
var page = doc.AddPage(595, 842);
// ... build page content ...
doc.Save();
```

| Member | Type | Description |
|--------|------|-------------|
| `Create(string path)` | static | Create a PDF/A document to file |
| `Create(Stream stream)` | static | Create a PDF/A document to stream |
| `AddPage(double width, double height)` | `PdfPageBuilder` | Add a page |
| `SetInfo(string title, string author)` | `void` | Set document info |
| `Save()` | `void` | Finalize and write |

---

### AcroFormReader

Reads interactive form fields from PDFs.

```csharp
using var reader = PdfReader.Open("form.pdf");
var fields = AcroFormReader.GetFields(reader);
foreach (var field in fields)
    Console.WriteLine($"{field.Name}: {field.Value}");
```

| Method | Returns | Description |
|--------|---------|-------------|
| `GetFields(IPdfReader reader)` | `List<FormField>` | Read all form fields |

### FormField

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Field name |
| `FieldType` | `string` | Field type (Tx, Btn, Ch, etc.) |
| `Value` | `string` | Field value |
| `Flags` | `int` | Field flags |
| `Children` | `List<FormField>` | Child fields |

---

### XmpMetadata

XMP metadata for PDF documents.

```csharp
doc.SetXmpMetadata(new XmpMetadata
{
    Title = "My Document",
    Author = "Author Name",
    PdfAPart = 1,
    PdfAConformance = "B"
});
```

| Property | Type | Description |
|----------|------|-------------|
| `Title` | `string` | Document title |
| `Author` | `string` | Author |
| `Subject` | `string` | Subject |
| `Creator` | `string` | Creator application |
| `Producer` | `string` | PDF producer |
| `CreateDate` | `DateTime?` | Creation date |
| `ModifyDate` | `DateTime?` | Modification date |
| `PdfAConformance` | `string` | PDF/A conformance level |
| `PdfAPart` | `int` | PDF/A part number |

---

## OpenPdf.Objects

PDF primitive types. All inherit from abstract `PdfObject`.

| Class | Description | Key Members |
|-------|-------------|-------------|
| `PdfArray` | Ordered list | `Items`, `Count`, `Add()`, indexer `[int]` |
| `PdfBoolean` | Boolean | `Value`, static `True`, `False` |
| `PdfDictionary` | Key-value map | `Entries`, `ContainsKey()`, `Get<T>()`, `GetInt()`, `GetName()`, indexer |
| `PdfInteger` | Integer | `Value` (long), implicit from int/long |
| `PdfName` | Name identifier | `Value` (string), implicit from string, well-known static names |
| `PdfNull` | Null | static `Instance` |
| `PdfReal` | Float | `Value` (double), implicit from double |
| `PdfStream` | Data stream | `Dictionary`, `Data` (byte[]) |
| `PdfString` | String | `Value` (byte[]), `IsHex`, `GetText()` |
| `PdfIndirectReference` | Object reference | `ObjectNumber`, `GenerationNumber` |

All objects implement `WriteTo(Stream stream)`.

---

## OpenPdf.IO

### PdfWriter

Writes PDF objects to a stream. Implements `IDisposable`.

| Member | Description |
|--------|-------------|
| `AddObject(PdfObject obj)` | Add an object, returns `PdfIndirectReference` |
| `AddObject(PdfObject obj, int objectNumber)` | Add with explicit object number |
| `Write()` | Write cross-reference table and trailer |

### PdfLexer

Tokenizes PDF byte streams.

| Member | Description |
|--------|-------------|
| `NextToken()` | Read the next token |
| `BaseStream` | Underlying stream |
| `Position` | Current position (get/set) |

### PdfToken

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `PdfTokenType` | Token type |
| `Value` | `string` | Token value |
| `Position` | `long` | Byte position in stream |

### PdfTokenType (Enum)

`Boolean`, `Integer`, `Real`, `LiteralString`, `HexString`, `Name`, `ArrayBegin`, `ArrayEnd`, `DictionaryBegin`, `DictionaryEnd`, `Null`, `Keyword`, `Eof`

---

## OpenPdf.Fonts

### TrueTypeFont

Parses TrueType font files (.ttf).

```csharp
var font = TrueTypeFont.Load("path/to/font.ttf");
page.AddTrueTypeFont("MyFont", font);
```

| Member | Type | Description |
|--------|------|-------------|
| `Load(string path)` | static | Load a TTF file |
| `Load(byte[] data)` | static | Load from byte array |
| `FontFamily` | `string` | Font family name |
| `PostScriptName` | `string` | PostScript name |
| `UnitsPerEm` | `int` | Units per em |
| `Ascender` | `short` | Ascender metric |
| `Descender` | `short` | Descender metric |
| `CapHeight` | `short` | Cap height |
| `StemV` | `int` | Stem vertical width |
| `RawData` | `byte[]` | Raw font data |
| `GetCharWidth(char c)` | `int` | Get character width |
| `GetGlyphId(char c)` | `int` | Get glyph ID for a character |

### TrueTypeSubsetter

Creates font subsets for embedding (reduces file size).

| Method | Description |
|--------|-------------|
| `Subset(TrueTypeFont font, IEnumerable<char> chars)` | Create a subset containing only the specified characters |

### CidFontBuilder

Builds Type0/CID fonts for Unicode (CJK) text rendering.

| Method | Description |
|--------|-------------|
| `AddCharacters(string text)` | Register characters to include |
| `Build(IPdfWriter writer)` | Build the CID font resources |
| `EncodeStringAsHex(string text)` | Encode text for CID font output |

---

## OpenPdf.Filters

### IPdfFilter (Interface)

| Method | Description |
|--------|-------------|
| `Decode(byte[] data)` | Decompress/decode data |
| `Encode(byte[] data)` | Compress/encode data |

### Built-in Filters

| Class | Description |
|-------|-------------|
| `FlateDecodeFilter` | FlateDecode (zlib/deflate) compression |
| `AsciiHexDecodeFilter` | ASCII hex encoding |

### FilterFactory

| Method | Description |
|--------|-------------|
| `Register(string name, Func<IPdfFilter> factory)` | Register a custom filter |
| `Create(string name)` | Create a filter by name |

---

## OpenPdf.Barcode

### Code128Encoder

Generates Code 128 barcodes. Static class.

```csharp
Code128Encoder.DrawBarcode(page, "ABC-123", x: 72, y: 500, barWidth: 1, barHeight: 50);
```

| Method | Description |
|--------|-------------|
| `Encode(string data)` | Encode data to Code 128 bar pattern |
| `DrawBarcode(PdfPageBuilder page, string data, double x, double y, double barWidth, double barHeight)` | Draw a barcode on a page |

### QrCodeEncoder

Generates QR codes. Static class.

```csharp
QrCodeEncoder.DrawQrCode(page, "https://example.com", x: 72, y: 400, moduleSize: 3);
```

| Method | Description |
|--------|-------------|
| `Encode(string data)` | Encode data to QR code matrix |
| `DrawQrCode(PdfPageBuilder page, string data, double x, double y, double moduleSize)` | Draw a QR code on a page |

---

## OpenPdf.Security

### PdfEncryption

Handles PDF encryption and decryption (RC4 40/128-bit, AES).

| Member | Type | Description |
|--------|------|-------------|
| `Revision` | `int` | Encryption revision |
| `KeyLength` | `int` | Key length in bits |
| `EncryptionKey` | `byte[]` | Computed encryption key |
| `Permissions` | `int` | Permission flags |
| `Authenticate(string password)` | `bool` | Authenticate with a password |
| `AuthenticateEmpty()` | `bool` | Try empty password |
| `DecryptObject(PdfObject obj, int objectNumber, int generationNumber)` | `PdfObject` | Decrypt a PDF object |

---

## Constants (PdfLimits)

| Constant | Value | Description |
|----------|-------|-------------|
| `MaxDecompressedSize` | 256 MB | Maximum decompressed stream size |
| `MaxStreamLength` | 256 MB | Maximum stream length |
| `MaxImagePixels` | 100,000,000 | Maximum image pixel count |
| `MaxRecursionDepth` | 100 | Maximum parsing recursion depth |
| `MaxXrefPrevChain` | 50 | Maximum cross-reference chain length |
| `MaxXrefFieldWidth` | 8 | Maximum xref field width |
