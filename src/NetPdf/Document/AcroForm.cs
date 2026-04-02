using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public sealed class AcroFormReader
{
    private readonly PdfReader _reader;

    public AcroFormReader(PdfReader reader)
    {
        _reader = reader;
    }

    public List<FormField> GetFields()
    {
        var fields = new List<FormField>();
        var catalog = _reader.GetCatalog();
        if (catalog == null) return fields;

        var acroFormRef = catalog["AcroForm"];
        var acroForm = _reader.ResolveReference(acroFormRef) as PdfDictionary;
        if (acroForm == null) return fields;

        var fieldsArray = acroForm.Get<PdfArray>("Fields");
        if (fieldsArray == null)
        {
            var fieldsRef = acroForm["Fields"];
            fieldsArray = _reader.ResolveReference(fieldsRef) as PdfArray;
        }
        if (fieldsArray == null) return fields;

        foreach (var item in fieldsArray.Items)
        {
            var fieldDict = _reader.ResolveReference(item) as PdfDictionary;
            if (fieldDict == null) continue;
            fields.Add(ReadField(fieldDict));
        }
        return fields;
    }

    private FormField ReadField(PdfDictionary dict)
    {
        var field = new FormField();
        field.Name = (dict.Get<PdfString>("T"))?.GetText() ?? "";

        var ftObj = dict["FT"];
        if (ftObj is PdfName ft)
            field.FieldType = ft.Value;

        var vObj = dict["V"];
        if (vObj is PdfString vs)
            field.Value = vs.GetText();
        else if (vObj is PdfName vn)
            field.Value = vn.Value;

        var ffObj = dict["Ff"];
        if (ffObj is PdfInteger ffInt)
            field.Flags = (int)ffInt.Value;

        // Check for kids (child fields)
        var kids = dict.Get<PdfArray>("Kids");
        if (kids == null)
        {
            var kidsRef = dict["Kids"];
            kids = _reader.ResolveReference(kidsRef) as PdfArray;
        }
        if (kids != null)
        {
            foreach (var kid in kids.Items)
            {
                var kidDict = _reader.ResolveReference(kid) as PdfDictionary;
                if (kidDict != null)
                    field.Children.Add(ReadField(kidDict));
            }
        }

        return field;
    }
}

public sealed class FormField
{
    public string Name { get; set; } = "";
    public string? FieldType { get; set; } // Tx, Btn, Ch, Sig
    public string? Value { get; set; }
    public int Flags { get; set; }
    public List<FormField> Children { get; } = new();

    public bool IsTextField => FieldType == "Tx";
    public bool IsCheckbox => FieldType == "Btn" && (Flags & (1 << 16)) == 0 && (Flags & (1 << 15)) == 0;
    public bool IsRadioButton => FieldType == "Btn" && (Flags & (1 << 15)) != 0;
    public bool IsChecked => Value != null && Value != "Off";
}

public sealed class AcroFormBuilder
{
    private readonly List<(FormFieldDefinition Field, PdfIndirectReference PageRef)> _fields = new();

    public void AddTextField(string name, double x, double y, double width, double height,
        string defaultValue = "", PdfIndirectReference? pageRef = null)
    {
        _fields.Add((new FormFieldDefinition
        {
            Name = name,
            FieldType = "Tx",
            Value = defaultValue,
            X = x, Y = y, Width = width, Height = height
        }, pageRef!));
    }

    public void AddCheckbox(string name, double x, double y, double size = 12,
        bool isChecked = false, PdfIndirectReference? pageRef = null)
    {
        _fields.Add((new FormFieldDefinition
        {
            Name = name,
            FieldType = "Btn",
            Value = isChecked ? "Yes" : "Off",
            X = x, Y = y, Width = size, Height = size
        }, pageRef!));
    }

    public PdfDictionary Build(PdfWriter writer)
    {
        var acroForm = new PdfDictionary();
        var fieldsArray = new PdfArray();

        // Default resources with Helvetica
        var dr = new PdfDictionary();
        var fontDict = new PdfDictionary();
        var helv = new PdfDictionary();
        helv["Type"] = PdfName.Font;
        helv["Subtype"] = new PdfName("Type1");
        helv["BaseFont"] = new PdfName("Helvetica");
        var helvRef = writer.AddObject(helv);
        fontDict["Helv"] = helvRef;
        dr["Font"] = fontDict;
        acroForm["DR"] = dr;
        acroForm["DA"] = new PdfString("/Helv 10 Tf 0 g");

        foreach (var (fieldDef, pageRef) in _fields)
        {
            var fieldDict = new PdfDictionary();
            fieldDict["Type"] = new PdfName("Annot");
            fieldDict["Subtype"] = new PdfName("Widget");
            fieldDict["FT"] = new PdfName(fieldDef.FieldType);
            fieldDict["T"] = new PdfString(fieldDef.Name);
            fieldDict["Rect"] = new PdfArray(new PdfObject[]
            {
                new PdfReal(fieldDef.X), new PdfReal(fieldDef.Y),
                new PdfReal(fieldDef.X + fieldDef.Width), new PdfReal(fieldDef.Y + fieldDef.Height)
            });

            if (fieldDef.FieldType == "Tx")
            {
                fieldDict["V"] = new PdfString(fieldDef.Value ?? "");
                fieldDict["DA"] = new PdfString("/Helv 10 Tf 0 g");
            }
            else if (fieldDef.FieldType == "Btn")
            {
                fieldDict["V"] = new PdfName(fieldDef.Value ?? "Off");
                fieldDict["AS"] = new PdfName(fieldDef.Value ?? "Off");
            }

            if (pageRef != null)
                fieldDict["P"] = pageRef;

            var fieldRef = writer.AddObject(fieldDict);
            fieldsArray.Add(fieldRef);
        }

        acroForm["Fields"] = fieldsArray;
        return acroForm;
    }
}

internal sealed class FormFieldDefinition
{
    public string Name { get; set; } = "";
    public string FieldType { get; set; } = "Tx";
    public string? Value { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
