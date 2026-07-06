using OpenPdf.Document;
using OpenPdf.Fonts;

namespace OpenPdf.Tests.Document;

public class RetirementLetterTests
{
    private const string MeiryoPath = @"C:\Windows\Fonts\meiryo.ttc";
    private static readonly string OutputPath = @"C:\Git\OpenPdf\退職届.pdf";

    private static bool FontExists() => File.Exists(MeiryoPath);

    [Fact]
    public void GenerateRetirementLetter()
    {
        if (!FontExists()) return;

        // ----- Page geometry (A4) -----
        const double pageWidth = 595;
        const double pageHeight = 842;
        const double leftMargin = 75;
        const double rightMargin = 75;
        const double contentWidth = pageWidth - leftMargin - rightMargin;

        var ttf = TrueTypeFont.Load(MeiryoPath, 0);
        var measurer = new CidFontBuilder(ttf);

        using var doc = PdfDocument.Create(OutputPath);
        var page = doc.AddPage(pageWidth, pageHeight);
        var font = page.AddTrueTypeFont(ttf);

        // ----- Title (centered) -----
        const string title = "退職届";
        const double titleSize = 26;
        double titleWidth = measurer.MeasureString(title, titleSize);
        double titleX = (pageWidth - titleWidth) / 2.0;
        double titleY = 770;
        page.DrawText(font, titleSize, titleX, titleY, title);

        // ----- Body -----
        const double bodySize = 11;
        const double bodyLeading = bodySize * 1.7;
        const double paraGap = bodyLeading * 0.35;
        var layout = new TextLayout(page, font, bodySize, bodyLeading, ttf);

        double cursorY = titleY - 48;

        page.DrawText(font, bodySize, leftMargin, cursorY, "私事、");
        cursorY -= bodyLeading;

        cursorY = layout.DrawParagraph(leftMargin, cursorY, contentWidth,
            "このたび、一身上の都合により、来る2026年6月19日をもって退職いたしたく、" +
            "ここにお願い申し上げます。");
        cursorY -= paraGap;

        cursorY = layout.DrawParagraph(leftMargin, cursorY, contentWidth,
            "つきましては、最終出社日を2026年5月19日(火)とし、" +
            "翌日以降は下記のとおり休暇を取得させていただきたく存じます。");
        cursorY -= paraGap * 0.6;

        // 休暇内訳（列を座標で揃える）
        const double bulletX = leftMargin + 14;
        const double labelX = bulletX + 14;
        const double valueX = labelX + 120;
        var schedule = new (string Label, string Value)[]
        {
            ("ロマンティック休暇", "2026年5月20日(水)〜5月21日(木)　計2日間"),
            ("年次有給休暇",       "2026年5月22日(金)〜6月19日(金)　計21日間"),
        };
        foreach (var (label, value) in schedule)
        {
            page.DrawText(font, bodySize, bulletX, cursorY, "・");
            page.DrawText(font, bodySize, labelX, cursorY, label);
            page.DrawText(font, bodySize, valueX, cursorY, value);
            cursorY -= bodyLeading;
        }
        cursorY -= paraGap * 0.6;

        cursorY = layout.DrawParagraph(leftMargin, cursorY, contentWidth,
            "貸与いただいておりますPC等の機材、鍵、およびカードキーにつきましては、" +
            "PCの初期化(フォーマット)を完了したうえで、" +
            "パソコン宅急便およびレターパック等を用いて貴社所在地宛てに郵送にてご返却いたします。");
        cursorY -= paraGap;

        cursorY = layout.DrawParagraph(leftMargin, cursorY, contentWidth,
            "業務の引き継ぎにつきましては、Claude Codeがスムーズに作業を継続できるよう、" +
            "引き継ぎ用のMarkdown形式ファイル(.md)を作成のうえ、別途共有させていただきます。");
        cursorY -= paraGap;

        cursorY = layout.DrawParagraph(leftMargin, cursorY, contentWidth,
            "末筆ながら、在職中に賜りましたご厚情に厚く御礼申し上げます。");

        // ----- 署名ブロック（右寄せ・左端を揃える） -----
        const double sigSize = 11.5;
        const double sigLeading = sigSize * 1.8;

        const string submitDate = "2026年5月19日";
        const string sigAffiliation = "所属　テクニカルサービス部　セクション1";
        const string sigNameLine = "氏名　本間　昂";

        double affWidth = measurer.MeasureString(sigAffiliation, sigSize);
        double nameWidth = measurer.MeasureString(sigNameLine, sigSize);
        double sigBlockWidth = Math.Max(affWidth, nameWidth);
        double sigBlockX = pageWidth - rightMargin - sigBlockWidth;

        double dateY = cursorY - bodyLeading * 2.2;
        double dateWidth = measurer.MeasureString(submitDate, sigSize);
        page.DrawText(font, sigSize, pageWidth - rightMargin - dateWidth, dateY, submitDate);

        double affY = dateY - sigLeading * 1.3;
        page.DrawText(font, sigSize, sigBlockX, affY, sigAffiliation);

        double nameY = affY - sigLeading;
        page.DrawText(font, sigSize, sigBlockX, nameY, sigNameLine);
        page.DrawText(font, sigSize, sigBlockX + nameWidth + 14, nameY, "㊞");

        // ----- 宛先（左寄せ・最下段） -----
        const double addrSize = 12.5;
        const double addrLeading = addrSize * 1.8;
        double addrY = nameY - sigLeading * 2.2;
        page.DrawText(font, addrSize, leftMargin, addrY, "株式会社ラグザイア");
        page.DrawText(font, addrSize, leftMargin, addrY - addrLeading, "代表取締役　毛利　良相　殿");

        doc.SetInfo(title: "退職届", creator: "OpenPdf");
        doc.Save();

        Assert.True(File.Exists(OutputPath));
        Assert.True(new FileInfo(OutputPath).Length > 0);
    }
}
