# OpenPdf

A pure C# PDF library with zero external dependencies. Read, write, and edit PDF files.

## Features

| Category | Capabilities |
|---|---|
| **Read** | Parse PDF 1.0-1.7, xref streams (PDF 1.5+), object streams, tolerant/repair mode |
| **Write** | Generate PDF 1.7, FlateDecode compression, incremental updates, linearized PDF |
| **Text** | Type1 (standard 14), TrueType/CJK (Japanese/Chinese/Korean) with font subsetting; text extraction from Type0/CIDFontType2 with ToUnicode CMap or Adobe predefined CJK CMaps |
| **Images (extract)** | `ImageExtractor` yields JPEG / JP2 / TIFF / BMP with pixel dims and page-space bounds |
| **OCR** | Optional `OpenPdf.Ocr.Tesseract` package + `SearchablePdfBuilder` to turn image-only PDFs into drag-selectable searchable ones |
| **Graphics** | Lines, rectangles, curves, colors (RGB/CMYK), transparency, clipping |
| **Images** | JPEG, PNG (RGB/RGBA), BMP (24/32-bit) |
| **Layout** | Text wrapping, tables, bullet/numbered lists, headers/footers, page numbers |
| **Annotations** | Links, text notes, highlights |
| **Bookmarks** | Nested outline/bookmark tree |
| **Forms** | Read/write AcroForm text fields and checkboxes |
| **Barcode** | Code128B barcode, QR code (vector rendering) |
| **Security** | RC4/AES-128 encryption/decryption, password authentication |
| **PDF/A** | PDF/A-1b compliant generation and validation |
| **Edit** | Page add/delete/reorder, merge, split, page stamping |
| **Metadata** | XMP metadata, document info dictionary |
| **Color** | DeviceRGB/Gray/CMYK, CalGray/RGB, ICCBased, Separation, DeviceN |

## Quick Start

### Generate a PDF

```csharp
using OpenPdf.Document;

using var doc = PdfDocument.Create("output.pdf");
var page = doc.AddPage();
var font = page.AddFont("Helvetica");
page.DrawText(font, 24, 72, 700, "Hello, World!");
doc.Save();
```

### Japanese Text (CJK)

```csharp
using OpenPdf.Document;
using OpenPdf.Fonts;

using var doc = PdfDocument.Create("japanese.pdf");
var page = doc.AddPage(595, 842); // A4
var ttf = TrueTypeFont.Load(@"C:\Windows\Fonts\meiryo.ttc", 0);
var font = page.AddTrueTypeFont(ttf);
page.DrawText(font, 24, 72, 770, "こんにちは世界！");
doc.Save();
```

### Read a PDF

```csharp
using OpenPdf.Document;

using var reader = PdfReader.Open("input.pdf");
Console.WriteLine($"Pages: {reader.PageCount}");
Console.WriteLine($"Version: {reader.Version}");

var extractor = new TextExtractor(reader);
Console.WriteLine(extractor.ExtractAllText());
```

### Extract Images

```csharp
using var reader = PdfReader.Open("scanned.pdf");
var ie = new ImageExtractor(reader);
foreach (var img in ie.Extract(pageIndex: 0))
{
    Console.WriteLine($"{img.Format} {img.Width}x{img.Height} @ ({img.PageX:F0},{img.PageY:F0})");
    img.SaveTo($"page0_{img.XObjectName}");
}
```

### Make an Image-Only PDF Searchable (OCR)

```csharp
// Install: dotnet add package OpenPdf.Ocr.Tesseract
using OpenPdf.Ocr.Tesseract;
using OpenPdf.Fonts;

using var engine = new TesseractOcrEngine(@"C:\tessdata", "jpn+eng");
using var reader = PdfReader.Open("scanned.pdf");
var font = TrueTypeFont.Load(@"C:\Windows\Fonts\meiryo.ttc", 0);
new SearchablePdfBuilder(reader, engine, font).Build("scanned.searchable.pdf");
```

### Merge PDFs

```csharp
using OpenPdf.Document;

PdfMerger.Merge(
    new[] { "file1.pdf", "file2.pdf", "file3.pdf" },
    "merged.pdf"
);
```

### Table

```csharp
var table = new TableBuilder(page, font, 10,
    new[] { 100.0, 150.0, 100.0 });
table.AddHeaderRow("Name", "Value", "Unit");
table.AddRow("Width", "210", "mm");
table.AddRow("Height", "297", "mm");
table.Draw(72, 700);
```

### QR Code

```csharp
using OpenPdf.Barcode;

QrCodeEncoder.DrawQrCode(page, "https://example.com", 72, 600, moduleSize: 4);
```

### PDF/A-1b

```csharp
using var doc = PdfADocument.Create("archive.pdf");
var page = doc.AddPage();
var font = page.AddFont("Helvetica");
page.DrawText(font, 12, 72, 770, "Long-term archival document");
doc.SetInfo("Title", "Author");
doc.Save();
```

## Installation

```
dotnet add package OpenPdf
```

## Requirements

- .NET 10.0 or .NET Standard 2.0

## License

MIT

### Third-party notices

This package embeds the Adobe CMap resources for CJK text extraction
(Adobe-Japan1, Adobe-GB1, Adobe-CNS1, Adobe-Korea1 predefined encodings and
their UCS2 CID→Unicode mappings) from the
[adobe-type-tools/cmap-resources](https://github.com/adobe-type-tools/cmap-resources)
and
[adobe-type-tools/mapping-resources-pdf](https://github.com/adobe-type-tools/mapping-resources-pdf)
repositories, licensed under BSD-3-Clause. See
`src/OpenPdf/Resources/CMaps/LICENSE-Adobe-*` for the full license text.
