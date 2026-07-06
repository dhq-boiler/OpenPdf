# OpenPdf.Ocr.Tesseract

Tesseract-backed OCR provider for [OpenPdf](https://www.nuget.org/packages/OpenPdf).

## Usage

```csharp
using OpenPdf.Document;
using OpenPdf.Ocr.Tesseract;

// Point tessdataPath at a folder containing jpn.traineddata and eng.traineddata.
// Download them from https://github.com/tesseract-ocr/tessdata_fast
using var engine = new TesseractOcrEngine(@"C:\tessdata", "jpn+eng");

using var reader = PdfReader.Open("scanned.pdf");
var ie = new ImageExtractor(reader);
var editor = new PdfEditor(reader);

for (int i = 0; i < reader.PageCount; i++)
{
    foreach (var img in ie.Extract(i))
    {
        var result = engine.Recognize(img.Data, img.Format);
        editor.AddOcrTextLayer(i, img, result);   // draggable text on top of the image
    }
}
editor.Save("scanned.searchable.pdf");
```

Native binaries and language data files ship separately per Tesseract's
convention — this package pulls in the managed wrapper only.
