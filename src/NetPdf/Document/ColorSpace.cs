using NetPdf.IO;
using NetPdf.Objects;

namespace NetPdf.Document;

public abstract class PdfColorSpace
{
    public abstract PdfObject ToPdfObject(PdfWriter writer);
}

public sealed class DeviceRgbColorSpace : PdfColorSpace
{
    public static readonly DeviceRgbColorSpace Instance = new();
    public override PdfObject ToPdfObject(PdfWriter writer) => new PdfName("DeviceRGB");
}

public sealed class DeviceGrayColorSpace : PdfColorSpace
{
    public static readonly DeviceGrayColorSpace Instance = new();
    public override PdfObject ToPdfObject(PdfWriter writer) => new PdfName("DeviceGray");
}

public sealed class DeviceCmykColorSpace : PdfColorSpace
{
    public static readonly DeviceCmykColorSpace Instance = new();
    public override PdfObject ToPdfObject(PdfWriter writer) => new PdfName("DeviceCMYK");
}

public sealed class CalGrayColorSpace : PdfColorSpace
{
    public double[] WhitePoint { get; set; } = new[] { 1.0, 1.0, 1.0 };
    public double[]? BlackPoint { get; set; }
    public double Gamma { get; set; } = 1.0;

    public override PdfObject ToPdfObject(PdfWriter writer)
    {
        var dict = new PdfDictionary();
        dict["WhitePoint"] = ToArray(WhitePoint);
        if (BlackPoint != null)
            dict["BlackPoint"] = ToArray(BlackPoint);
        if (Gamma != 1.0)
            dict["Gamma"] = new PdfReal(Gamma);
        return new PdfArray(new PdfObject[] { new PdfName("CalGray"), dict });
    }

    private static PdfArray ToArray(double[] values) =>
        new PdfArray(values.Select(v => (PdfObject)new PdfReal(v)));
}

public sealed class CalRgbColorSpace : PdfColorSpace
{
    public double[] WhitePoint { get; set; } = new[] { 1.0, 1.0, 1.0 };
    public double[]? BlackPoint { get; set; }
    public double[]? Gamma { get; set; }
    public double[]? Matrix { get; set; }

    public override PdfObject ToPdfObject(PdfWriter writer)
    {
        var dict = new PdfDictionary();
        dict["WhitePoint"] = ToArray(WhitePoint);
        if (BlackPoint != null)
            dict["BlackPoint"] = ToArray(BlackPoint);
        if (Gamma != null)
            dict["Gamma"] = ToArray(Gamma);
        if (Matrix != null)
            dict["Matrix"] = ToArray(Matrix);
        return new PdfArray(new PdfObject[] { new PdfName("CalRGB"), dict });
    }

    private static PdfArray ToArray(double[] values) =>
        new PdfArray(values.Select(v => (PdfObject)new PdfReal(v)));
}

public sealed class IccBasedColorSpace : PdfColorSpace
{
    public byte[] IccProfileData { get; }
    public int NumberOfComponents { get; }
    public string? AlternateColorSpace { get; set; }

    public IccBasedColorSpace(byte[] iccProfileData, int numberOfComponents)
    {
        IccProfileData = iccProfileData;
        NumberOfComponents = numberOfComponents;
    }

    public override PdfObject ToPdfObject(PdfWriter writer)
    {
        var streamDict = new PdfDictionary();
        streamDict["N"] = new PdfInteger(NumberOfComponents);
        if (AlternateColorSpace != null)
            streamDict["Alternate"] = new PdfName(AlternateColorSpace);

        var filter = new Filters.FlateDecodeFilter();
        var compressed = filter.Encode(IccProfileData);
        streamDict["Filter"] = PdfName.FlateDecode;

        var stream = new PdfStream(streamDict, compressed);
        var streamRef = writer.AddObject(stream);
        return new PdfArray(new PdfObject[] { new PdfName("ICCBased"), streamRef });
    }
}

public sealed class SeparationColorSpace : PdfColorSpace
{
    public string ColorantName { get; }
    public PdfColorSpace AlternateSpace { get; }
    public double[] TintTransform { get; } // Simplified: C0 and C1 values for exponential function

    public SeparationColorSpace(string colorantName, PdfColorSpace alternateSpace, double[] c0, double[] c1)
    {
        ColorantName = colorantName;
        AlternateSpace = alternateSpace;
        TintTransform = c0.Concat(c1).ToArray();
    }

    public override PdfObject ToPdfObject(PdfWriter writer)
    {
        int n = TintTransform.Length / 2;
        var c0 = TintTransform.Take(n).ToArray();
        var c1 = TintTransform.Skip(n).ToArray();

        // Type 2 (exponential) function
        var funcDict = new PdfDictionary();
        funcDict["FunctionType"] = new PdfInteger(2);
        funcDict["Domain"] = new PdfArray(new PdfObject[] { new PdfReal(0), new PdfReal(1) });
        funcDict["C0"] = new PdfArray(c0.Select(v => (PdfObject)new PdfReal(v)));
        funcDict["C1"] = new PdfArray(c1.Select(v => (PdfObject)new PdfReal(v)));
        funcDict["N"] = new PdfReal(1);
        var funcRef = writer.AddObject(funcDict);

        return new PdfArray(new PdfObject[]
        {
            new PdfName("Separation"),
            new PdfName(ColorantName),
            AlternateSpace.ToPdfObject(writer),
            funcRef
        });
    }
}

public sealed class DeviceNColorSpace : PdfColorSpace
{
    public string[] ColorantNames { get; }
    public PdfColorSpace AlternateSpace { get; }

    public DeviceNColorSpace(string[] colorantNames, PdfColorSpace alternateSpace)
    {
        ColorantNames = colorantNames;
        AlternateSpace = alternateSpace;
    }

    public override PdfObject ToPdfObject(PdfWriter writer)
    {
        var names = new PdfArray(ColorantNames.Select(n => (PdfObject)new PdfName(n)));

        // Identity tint transform (pass-through)
        var funcDict = new PdfDictionary();
        funcDict["FunctionType"] = new PdfInteger(4);
        funcDict["Domain"] = new PdfArray(ColorantNames.SelectMany(_ =>
            new PdfObject[] { new PdfReal(0), new PdfReal(1) }));
        funcDict["Range"] = new PdfArray(ColorantNames.SelectMany(_ =>
            new PdfObject[] { new PdfReal(0), new PdfReal(1) }));
        // PostScript calculator: identity function
        var psCode = "{ }";
        var funcStream = new PdfStream(funcDict, System.Text.Encoding.ASCII.GetBytes(psCode));
        var funcRef = writer.AddObject(funcStream);

        return new PdfArray(new PdfObject[]
        {
            new PdfName("DeviceN"),
            names,
            AlternateSpace.ToPdfObject(writer),
            funcRef
        });
    }
}
