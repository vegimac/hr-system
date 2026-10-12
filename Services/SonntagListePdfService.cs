using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Kontroll-Liste «Freie Sonntage» für die GF (Walter-Vorgabe 12.10.2026).
/// Rechnung: <see cref="SonntagKontrolle"/>.
/// </summary>
public class SonntagListePdfService
{
    public record Zeile(
        string? EmpNr, string? Vorname, string? Nachname, bool Familie,
        int Soll, int Frei, int NichtFrei, int Krank, int Fehlend, int Verbleibend, string Status,
        DateOnly? Austritt = null, bool NurVertragsende = false);

    public record Input(string FilialeTitel, int Jahr, DateOnly? ZaehlAb, bool NurKritisch,
        int AnzahlMa, int AnzahlRot, int AnzahlKnapp, List<Zeile> Zeilen);

    private const string Ink = "#1a1a1a";
    private const string Body = "#3f3f3f";
    private const string Muted = "#8b8b8b";
    private const string Line = "#d6d1c7";
    private const string Soft = "#f1efe9";

    public static string StatusText(string s) => s switch
    {
        "ok" => "erreicht",
        "offen" => "offen",
        "knapp" => "knapp",
        "rot" => "nicht mehr erreichbar",
        _ => "keine Stempeldaten",
    };

    public byte[] Generate(Input d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(10f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"Freie Sonntage {d.Jahr} – Kontrolle").Bold().FontSize(18f).FontColor(Ink);
                        r.AutoItem().AlignBottom().Text(d.FilialeTitel).FontSize(12f).FontColor(Body);
                    });
                    col.Item().PaddingTop(2).Text(
                        $"Stand {DateTime.Now:dd.MM.yyyy} · {d.AnzahlMa} Mitarbeitende · {d.AnzahlRot} nicht mehr erreichbar · {d.AnzahlKnapp} knapp"
                        + (d.ZaehlAb.HasValue ? $" · gezählt ab {d.ZaehlAb.Value:dd.MM.yyyy} (erste Stempeldaten)" : "")
                        + (d.NurKritisch ? " · gedruckt: nur knapp / nicht erreichbar" : ""))
                        .FontSize(9f).FontColor(Muted);
                    col.Item().PaddingTop(3).Text(
                        "Pro Kalenderjahr mindestens 4 freie Sonntage, mit Familienbetreuung (easy@work) 12 — anteilig bei Eintritt/Austritt unter dem Jahr. "
                        + "Während voller Krankheit, Unfall, Militär, Mutter-/Vaterschaft entsteht kein Anspruch: diese Tage kürzen das Soll, solche Sonntage zählen weder frei noch nicht frei. "
                        + "Frei = an diesem Sonntag kein Stempel und keine Absenz (auch keine Ferien). «Verbleibend» = kommende Sonntage ohne geplante Absenz bis Jahresende bzw. Austritt."
                        + (d.Zeilen.Any(z => z.NurVertragsende) ? " * = Vertragsende, kein Austrittsdatum erfasst." : ""))
                        .FontSize(9.5f).FontColor(Body);
                    col.Item().PaddingTop(6);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.1f); // MA-Nr
                        c.RelativeColumn(1.8f); // Vorname
                        c.RelativeColumn(1.9f); // Nachname
                        c.RelativeColumn(1.3f); // Familienbetr.
                        c.RelativeColumn(0.8f); // Soll
                        c.RelativeColumn(0.9f); // frei bisher
                        c.RelativeColumn(1.3f); // nicht frei
                        c.RelativeColumn(1.6f); // krank/Unfall
                        c.RelativeColumn(1.1f); // fehlen
                        c.RelativeColumn(1.7f); // verbleibend
                        c.RelativeColumn(1.7f); // Austritt
                        c.RelativeColumn(3.2f); // Status
                    });

                    t.Header(h =>
                    {
                        void Th(string s, bool rechts = false)
                        {
                            var c = h.Cell().Background(Soft).BorderBottom(1f).BorderColor(Ink)
                                .PaddingVertical(3).PaddingHorizontal(3).PaddingLeft(s == "Status" ? 7 : 0);
                            (rechts ? c.AlignRight() : c).Text(s).Bold().FontSize(9.5f).FontColor(Ink);
                        }
                        Th("MA-Nr.");
                        Th("Vorname");
                        Th("Nachname");
                        Th("Fam.-Betr.");
                        Th("Soll", true);
                        Th("frei", true);
                        Th("nicht frei", true);
                        Th("krank/Unfall", true);
                        Th("fehlen", true);
                        Th("verbleibend", true);
                        Th("Austritt", true);
                        Th("Status");
                    });

                    foreach (var z in d.Zeilen)
                    {
                        var (hg, tx) = z.Status switch
                        {
                            "rot" => ("#ffc7ce", "#9c0006"),
                            "knapp" => ("#ffeb9c", "#7a5c00"),
                            "ok" => ("#ffffff", "#15803d"),
                            _ => ("#ffffff", Body),
                        };
                        void Td(string s, bool rechts = false, bool status = false)
                        {
                            var c = t.Cell().BorderBottom(0.5f).BorderColor(Line);
                            if (status && hg != "#ffffff") c = c.Background(hg);
                            c = c.PaddingVertical(2).PaddingHorizontal(3);
                            if (status && !rechts) c = c.PaddingLeft(7);
                            var txt = (rechts ? c.AlignRight() : c).Text(s.Length == 0 ? " " : s).FontSize(10f)
                                .FontColor(status ? tx : Body);
                            if (status && z.Status is "rot" or "knapp") txt.Bold();
                        }
                        Td(z.EmpNr ?? "");
                        Td(z.Vorname ?? "");
                        Td(z.Nachname ?? "");
                        Td(z.Familie ? "Ja" : "Nein");
                        Td(z.Soll.ToString(), true);
                        Td(z.Frei.ToString(), true);
                        Td(z.NichtFrei.ToString(), true);
                        Td(z.Krank.ToString(), true);
                        Td(z.Fehlend.ToString(), true, status: z.Fehlend > 0);
                        Td(z.Verbleibend.ToString(), true);
                        Td(z.Austritt.HasValue ? z.Austritt.Value.ToString("dd.MM.yyyy") + (z.NurVertragsende ? "*" : "") : "", true);
                        Td(StatusText(z.Status), status: true);
                    }
                });

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                    txt.Span("Seite ");
                    txt.CurrentPageNumber();
                    txt.Span(" / ");
                    txt.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
