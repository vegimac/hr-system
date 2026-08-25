using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

// ── Daten-Typen der MTP-Stunden-Kontrolle (Controller + PDF teilen sie) ──
public record MtpWeekCell(decimal Total, decimal Gearbeitet, decimal Absenz);
public record MtpRow(string? Vorname, string? Name, bool Schwanger, bool Mutterschutz,
                     decimal GarantiertH, List<MtpWeekCell?> Weeks, decimal? Avg);
public record MtpStundenData(DateOnly From, DateOnly To, List<DateOnly> Wochen, List<MtpRow> Rows);

/// <summary>
/// PDF der MTP-Stunden-Kontrolle (Walter-Vorgabe 25.08.2026) — A4 quer,
/// gleiche Daten/Sortierung wie die Bildschirm-Ansicht: grösstes Minus
/// zuoberst, grösstes Plus zuunterst; Ø grün = Garantie erreicht, rot =
/// darunter; * = Woche enthält angerechnete Absenz-Stunden.
/// </summary>
public class MtpStundenPdfService
{
    private static readonly string Ink   = "#1a1a1a";
    private static readonly string Body  = "#3f3f3f";
    private static readonly string Muted = "#8b8b8b";
    private static readonly string Line  = "#b9b4aa";
    private static readonly string Soft  = "#f1efe9";
    private static readonly string Gruen = "#15803d";
    private static readonly string Rot   = "#b91c1c";

    public byte[] Generate(MtpStundenData d, string filialeTitel)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(8f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("MTP-Stunden-Kontrolle").Bold().FontSize(14f).FontColor(Ink);
                        r.AutoItem().AlignBottom().Text(filialeTitel).FontSize(10f).FontColor(Body);
                    });
                    col.Item().PaddingTop(2).Text(
                        $"Zeitraum {d.From:dd.MM.yyyy} – {d.To:dd.MM.yyyy} · nur volle Wochen (Mo–So) · " +
                        "Zelle = gestempelte Stunden + angerechnete Absenz (* = enthält Absenz) · " +
                        "Ø grün = Garantie erreicht/übertroffen, rot = darunter · Sortierung: grösstes Minus zuoberst.")
                        .FontSize(7f).FontColor(Muted);
                    col.Item().PaddingTop(5);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2.2f);  // Vorname
                        c.RelativeColumn(2.2f);  // Name
                        c.RelativeColumn(1.1f);  // Garantie
                        foreach (var _ in d.Wochen) c.RelativeColumn(1.15f);
                        c.RelativeColumn(1.2f);  // Ø
                    });

                    t.Header(h =>
                    {
                        void Th(string a, string? b = null, bool right = false)
                        {
                            var cell = h.Cell().Background(Soft)
                                .BorderBottom(1f).BorderColor(Ink)
                                .PaddingVertical(3).PaddingHorizontal(3);
                            var el = right ? cell.AlignRight() : cell;
                            el.Column(cc =>
                            {
                                cc.Item().Text(a).Bold().FontSize(7.5f).FontColor(Ink);
                                if (b != null) cc.Item().Text(b).FontSize(6.5f).FontColor(Muted);
                            });
                        }
                        Th("Vorname");
                        Th("Name");
                        Th("Garantie", right: true);
                        foreach (var mo in d.Wochen)
                            Th($"KW{System.Globalization.ISOWeek.GetWeekOfYear(mo.ToDateTime(TimeOnly.MinValue))}",
                               mo.ToString("dd.MM."), right: true);
                        Th("Ø h/Wo", right: true);
                    });

                    foreach (var r in d.Rows)
                    {
                        IContainer Td() => t.Cell().BorderBottom(0.5f).BorderColor(Line)
                            .PaddingVertical(2.5f).PaddingHorizontal(3);

                        var nameSuffix = r.Mutterschutz ? "  (Mutterschutz)" : (r.Schwanger ? "  (schwanger)" : "");
                        Td().Text(r.Vorname ?? "").FontSize(8f);
                        Td().Text(txt =>
                        {
                            txt.Span(r.Name ?? "").FontSize(8f);
                            if (nameSuffix.Length > 0)
                                txt.Span(nameSuffix).FontSize(6.5f).Italic().FontColor("#be5a83");
                        });
                        Td().AlignRight().Text(r.GarantiertH.ToString("0.00")).SemiBold().FontSize(8f);
                        foreach (var w in r.Weeks)
                        {
                            var cell = Td().AlignRight();
                            if (w == null) { cell.Text("–").FontColor(Muted); continue; }
                            cell.Text(txt =>
                            {
                                txt.Span(w.Total.ToString("0.00")).FontSize(8f);
                                if (w.Absenz > 0) txt.Span("*").FontColor("#b45309").Bold();
                            });
                        }
                        var avgCell = Td().AlignRight();
                        if (r.Avg == null) avgCell.Text("–").FontColor(Muted);
                        else avgCell.Text(r.Avg.Value.ToString("0.00")).Bold().FontSize(8.5f)
                            .FontColor(r.Avg.Value >= r.GarantiertH ? Gruen : Rot);
                    }
                });

                page.Footer().Row(r =>
                {
                    r.RelativeItem().Text($"Erstellt {DateTime.Now:dd.MM.yyyy HH:mm}")
                        .FontSize(6.5f).FontColor(Muted);
                    r.AutoItem().Text(txt =>
                    {
                        txt.DefaultTextStyle(s => s.FontSize(6.5f).FontColor(Muted));
                        txt.Span("Seite ");
                        txt.CurrentPageNumber();
                        txt.Span(" / ");
                        txt.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }
}
