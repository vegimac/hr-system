using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// PDF für die beiden GF-Auswertungen (Walter 03.08.2026):
///   • Sollstunden-Übersicht (Stichtag + Monat)
///   • Ferien / Feiertage / Nacht
/// A4 quer, kompakt — gleiche Spalten wie die Bildschirmlisten.
/// </summary>
public class AuswertungenReportPdfService
{
    private const string Dark = "#3f3f3f";
    private const string Muted = "#646464";
    private const string Soft = "#f1efe9";
    private const string SoftBlue = "#dbe7ff";
    private const string SoftBlueHead = "#c4d8ff";
    private const string Neg = "#b91c1c";
    private const string Pos = "#166534";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static readonly CultureInfo CH = CultureInfo.GetCultureInfo("de-CH");
    private static readonly string[] Monate =
        { "", "Januar", "Februar", "März", "April", "Mai", "Juni",
          "Juli", "August", "September", "Oktober", "November", "Dezember" };

    private static string N2(decimal? v) =>
        v == null ? "–" : v.Value.ToString("0.00", CH);

    private static string Signed(decimal? v)
    {
        if (v == null) return "–";
        var s = v.Value.ToString("0.00", CH);
        return v.Value > 0.005m ? "+" + s : s;
    }

    private static string DdMmYyyy(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || iso.Length < 10) return iso ?? "";
        return $"{iso.Substring(8, 2)}.{iso.Substring(5, 2)}.{iso.Substring(0, 4)}";
    }

    private static void Banner(IContainer c, string title)
    {
        c.Height(36).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer().PaddingHorizontal(12).PaddingTop(10)
                .Text(title).Bold().FontSize(11f).FontColor(Dark);
        });
    }

    // ── Sollstunden ───────────────────────────────────────────────────────

    public sealed class SollRow
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Model { get; set; }
        public decimal? Pensum { get; set; }
        public decimal? GuaranteedHours { get; set; }
        public decimal StSoll { get; set; }
        public decimal StSollRed { get; set; }
        public decimal StAbsenz { get; set; }
        public decimal StGearb { get; set; }
        public decimal StTotal { get; set; }
        public decimal StSaldoVor { get; set; }
        public decimal StSaldo { get; set; }
        public decimal MtSoll { get; set; }
        public decimal MtSollRed { get; set; }
        public decimal MtAbsenz { get; set; }
        public decimal MtGearb { get; set; }
        public decimal MtTotal { get; set; }
        public decimal MtSaldoVor { get; set; }
        public decimal MtSaldo { get; set; }
    }

    public byte[] GenerateSollstunden(
        string branchLabel, string periodFrom, string periodTo, string stichtag,
        int daysToStichtag, int daysInMonth, IReadOnlyList<SollRow> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var title = "Sollstunden-Übersicht";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginTop(0.6f, Unit.Centimetre);
                page.MarginBottom(0.7f, Unit.Centimetre);
                page.MarginHorizontal(0.7f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(7f).FontColor(Dark).LineHeight(1.1f));

                page.Header().Element(h => Banner(h, title));

                page.Content().PaddingTop(6).Column(col =>
                {
                    col.Item().Text(branchLabel).SemiBold().FontSize(9f);
                    col.Item().PaddingTop(2).Text(
                        $"Periode {DdMmYyyy(periodFrom)} – {DdMmYyyy(periodTo)} · Stichtag {DdMmYyyy(stichtag)} (Tag {daysToStichtag}/{daysInMonth}) · {rows.Count} MA · Druck {DateTime.Today:dd.MM.yyyy}")
                        .FontSize(7.5f).FontColor(Muted);

                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.4f); // Name
                            c.ConstantColumn(34);   // Modell
                            c.ConstantColumn(28);   // Pens
                            for (var i = 0; i < 14; i++) c.RelativeColumn(1f);
                        });

                        void H(string txt, bool st = false, bool left = false)
                        {
                            var cell = t.Cell().Background(st ? SoftBlueHead : Soft)
                                .BorderBottom(0.7f).BorderColor("#cbd5e1")
                                .PaddingVertical(3).PaddingHorizontal(2);
                            var tx = left ? cell.AlignLeft() : cell.AlignRight();
                            tx.Text(txt).SemiBold().FontSize(6.5f).FontColor(Dark);
                        }

                        // Gruppenkopf
                        t.Cell().ColumnSpan(3).Background(Soft).Padding(3)
                            .Text("").FontSize(6.5f);
                        t.Cell().ColumnSpan(7).Background(SoftBlueHead).Padding(3).AlignCenter()
                            .Text($"STICHTAG (bis {DdMmYyyy(stichtag)})").SemiBold().FontSize(7f);
                        t.Cell().ColumnSpan(7).Background(Soft).Padding(3).AlignCenter()
                            .Text("MONAT").SemiBold().FontSize(7f);

                        H("Mitarbeiter", left: true);
                        H("Modell", left: true);
                        H("Pens", left: true);
                        foreach (var h in new[] { "Soll", "Soll red.", "Absenz", "Gearb.", "Total", "Vor.M", "Saldo" })
                            H(h, st: true);
                        foreach (var h in new[] { "Soll", "Soll red.", "Absenz", "Gearb.", "Total", "Vor.M", "Saldo" })
                            H(h);

                        string prev = "";
                        foreach (var r in rows)
                        {
                            var model = r.Model ?? "";
                            if (prev != "" && prev != model)
                            {
                                t.Cell().ColumnSpan(17).PaddingVertical(3).Text(" ").FontSize(4f);
                            }
                            prev = model;

                            var pens = model == "MTP" ? r.GuaranteedHours : r.Pensum;
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2)
                                .Text($"{r.Name} ({r.Number})").FontSize(6.5f);
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2).Text(model).FontSize(6.5f);
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2)
                                .Text(pens == null ? "" : pens.Value.ToString("0.##", CH)).FontSize(6.5f).FontColor(Muted);

                            void Cell(decimal v, bool st, bool signed)
                            {
                                var bg = st ? SoftBlue : "#ffffff";
                                var color = !signed ? Dark
                                    : (v < -0.01m ? Neg : (v > 0.01m ? Pos : Muted));
                                var el = t.Cell().Background(bg)
                                    .PaddingVertical(2).PaddingHorizontal(2).AlignRight();
                                var text = signed ? Signed(v) : N2(v);
                                if (signed && Math.Abs(v) > 0.01m)
                                    el.Text(text).SemiBold().FontSize(6.5f).FontColor(color);
                                else
                                    el.Text(text).FontSize(6.5f).FontColor(color);
                            }

                            Cell(r.StSoll, true, false);
                            Cell(r.StSollRed, true, false);
                            Cell(r.StAbsenz, true, false);
                            Cell(r.StGearb, true, false);
                            Cell(r.StTotal, true, false);
                            Cell(r.StSaldoVor, true, true);
                            Cell(r.StSaldo, true, true);
                            Cell(r.MtSoll, false, false);
                            Cell(r.MtSollRed, false, false);
                            Cell(r.MtAbsenz, false, false);
                            Cell(r.MtGearb, false, false);
                            Cell(r.MtTotal, false, false);
                            Cell(r.MtSaldoVor, false, true);
                            Cell(r.MtSaldo, false, true);
                        }
                    });
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Seite ").FontSize(7f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7f).FontColor(Muted);
                    t.Span(" / ").FontSize(7f).FontColor(Muted);
                    t.TotalPages().FontSize(7f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    // ── Ferien / Feiertage / Nacht ────────────────────────────────────────

    public sealed class FerienRow
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Model { get; set; }
        public decimal? Pensum { get; set; }
        public decimal? GuaranteedHours { get; set; }
        public decimal? VacationWeeks { get; set; }
        public decimal AnspruchTage { get; set; }
        public decimal KuerzungTage { get; set; }
        public decimal BezugTage { get; set; }
        public decimal SaldoTage { get; set; }
        public decimal? FeiertagAnspruch { get; set; }
        public decimal? FeiertagBezug { get; set; }
        public decimal? FeiertagSaldo { get; set; }
        public int MaxNaechte6Wochen { get; set; }
        public decimal NachtStunden { get; set; }
        public decimal NachtZuschlag { get; set; }
        public decimal NachtKomp { get; set; }
        public decimal NachtSaldo { get; set; }
        public bool NachtWarn { get; set; }
    }

    public byte[] GenerateFerien(
        string branchLabel, int year, int month, int nachtWarnTotal,
        IReadOnlyList<FerienRow> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var title = "Ferien / Feiertage / Nacht";
        var monName = month >= 1 && month <= 12 ? Monate[month] : month.ToString();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginTop(0.6f, Unit.Centimetre);
                page.MarginBottom(0.7f, Unit.Centimetre);
                page.MarginHorizontal(0.7f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(7f).FontColor(Dark).LineHeight(1.1f));

                page.Header().Element(h => Banner(h, title));

                page.Content().PaddingTop(6).Column(col =>
                {
                    col.Item().Text(branchLabel).SemiBold().FontSize(9f);
                    col.Item().PaddingTop(2).Text(
                        $"Jahr {year} · aufgelaufen Januar – {monName} · {rows.Count} MA · Druck {DateTime.Today:dd.MM.yyyy}")
                        .FontSize(7.5f).FontColor(Muted);

                    if (nachtWarnTotal > 0)
                    {
                        col.Item().PaddingTop(4).Background("#fee2e2").Padding(5)
                            .Text($"⚠ {nachtWarnTotal} MA mit >18 Nächten in 6 Wochen ohne vollständige Nachtarbeit-Nachweise")
                            .FontSize(7.5f).FontColor(Neg).SemiBold();
                    }

                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.6f);
                            c.ConstantColumn(34);
                            c.ConstantColumn(28);
                            c.ConstantColumn(32);
                            for (var i = 0; i < 4; i++) c.RelativeColumn(1f); // Ferien
                            for (var i = 0; i < 3; i++) c.RelativeColumn(1f); // Feiertage
                            for (var i = 0; i < 5; i++) c.RelativeColumn(1f); // Nacht
                        });

                        void H(string txt, bool left = false)
                        {
                            var cell = t.Cell().Background(Soft)
                                .BorderBottom(0.7f).BorderColor("#cbd5e1")
                                .PaddingVertical(3).PaddingHorizontal(2);
                            (left ? cell.AlignLeft() : cell.AlignRight())
                                .Text(txt).SemiBold().FontSize(6.5f);
                        }

                        t.Cell().ColumnSpan(4).Background(Soft).Padding(3).Text("").FontSize(6f);
                        t.Cell().ColumnSpan(4).Background(Soft).Padding(3).AlignCenter()
                            .Text("FERIEN (Tage)").SemiBold().FontSize(7f);
                        t.Cell().ColumnSpan(3).Background(Soft).Padding(3).AlignCenter()
                            .Text("FEIERTAGE (FIX)").SemiBold().FontSize(7f);
                        t.Cell().ColumnSpan(5).Background(Soft).Padding(3).AlignCenter()
                            .Text("NACHT").SemiBold().FontSize(7f);

                        H("Mitarbeiter", left: true);
                        H("Modell", left: true);
                        H("Pens", left: true);
                        H("Wo/J");
                        foreach (var h in new[] { "Anspruch", "Kürzung", "Bezug", "Saldo" }) H(h);
                        foreach (var h in new[] { "Anspruch", "Bezug", "Saldo" }) H(h);
                        foreach (var h in new[] { "Max 6W", "Std", "Zuschlag", "Komp", "Saldo" }) H(h);

                        void Cell(string text, string? color = null, bool bold = false)
                        {
                            var el = t.Cell().PaddingVertical(2).PaddingHorizontal(2).AlignRight();
                            var tspan = el.Text(text).FontSize(6.5f).FontColor(color ?? Dark);
                            if (bold) tspan.SemiBold();
                        }

                        string prev = "";
                        foreach (var r in rows)
                        {
                            var model = r.Model ?? "";
                            if (prev != "" && prev != model)
                                t.Cell().ColumnSpan(16).PaddingVertical(3).Text(" ").FontSize(4f);
                            prev = model;

                            var pens = model == "MTP" ? r.GuaranteedHours : r.Pensum;
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2)
                                .Text($"{r.Name} ({r.Number})").FontSize(6.5f);
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2).Text(model).FontSize(6.5f);
                            t.Cell().PaddingVertical(2).PaddingHorizontal(2)
                                .Text(pens == null ? "" : pens.Value.ToString("0.##", CH)).FontSize(6.5f).FontColor(Muted);
                            Cell(r.VacationWeeks == null ? "–" : r.VacationWeeks.Value.ToString("0.##", CH), Muted);

                            Cell(N2(r.AnspruchTage));
                            Cell(r.KuerzungTage > 0.01m ? "−" + N2(r.KuerzungTage) : N2(r.KuerzungTage),
                                r.KuerzungTage > 0.01m ? Neg : Dark);
                            Cell(N2(r.BezugTage));
                            Cell(Signed(r.SaldoTage), r.SaldoTage < -0.01m ? Neg : Pos, bold: true);

                            Cell(N2(r.FeiertagAnspruch));
                            Cell(N2(r.FeiertagBezug));
                            if (r.FeiertagSaldo == null) Cell("–", Muted);
                            else Cell(Signed(r.FeiertagSaldo), r.FeiertagSaldo < -0.01m ? Neg : Pos, bold: true);

                            var maxN = r.MaxNaechte6Wochen;
                            var maxColor = r.NachtWarn ? Neg : (maxN > 18 ? Pos : Dark);
                            Cell(r.NachtWarn ? $"{maxN} ⚠" : maxN.ToString(CH), maxColor, bold: maxN > 18);
                            Cell(N2(r.NachtStunden));
                            Cell(N2(r.NachtZuschlag));
                            Cell(N2(r.NachtKomp));
                            var nsColor = r.NachtSaldo >= 19 ? Neg
                                : (r.NachtSaldo > 9 ? "#854d0e" : (r.NachtSaldo < -0.01m ? Neg : Pos));
                            Cell(Signed(r.NachtSaldo), nsColor, bold: true);
                        }
                    });
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Seite ").FontSize(7f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7f).FontColor(Muted);
                    t.Span(" / ").FontSize(7f).FontColor(Muted);
                    t.TotalPages().FontSize(7f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }
}
