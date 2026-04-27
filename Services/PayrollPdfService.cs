using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text.Json;

namespace HrSystem.Services;

/// <summary>
/// Generiert eine Lohnabrechnung als A4-PDF (gleicher Look wie der Vertrag:
/// gelbes Banner, Arial, kompakt). Erwartet als Eingabe das JSON-Objekt
/// von /api/payroll/calculate.
/// </summary>
public class PayrollPdfService
{
    private const string Yellow = "#FFC72C";
    // Walter-Wunsch (25.04.2026): PDF tief schwarz, ausser Banner-Gelb.
    // Konstanten beibehalten für lokale Override-Möglichkeiten, aber alle
    // standardmässig auf Dark (#000000-nahe) gesetzt.
    private const string Dark   = "#000000";
    private const string Muted  = "#000000";
    private const string Red    = "#000000";
    private const string Green  = "#000000";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static string CHF(decimal? v)
    {
        if (v == null) return "0.00";
        decimal abs = Math.Abs(v.Value);
        long  i    = (long)Math.Floor(abs);
        int   d    = (int)Math.Round((abs - i) * 100);
        string sign = v.Value < 0 ? "-" : "";
        return sign + i.ToString("N0", CultureInfo.InvariantCulture).Replace(",", "'") + "." + d.ToString("00");
    }

    private static string Num(decimal? v, int dec = 2)
    {
        if (v == null) return "";
        return v.Value.ToString($"F{dec}", CultureInfo.InvariantCulture);
    }

    public byte[] Generate(JsonElement slip)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var periodLabel = GetString(slip, "periodLabel"); // z.B. "März 2026"
        var parentName  = GetString(slip, "companyParentName"); // z.B. "Schaub Restaurants GmbH"
        var company     = GetString(slip, "companyName");       // z.B. "Filiale Oftringen"
        var compAddr    = GetString(slip, "companyAddress");
        var compZip     = GetString(slip, "companyZipCity");
        var companyCity = GetString(slip, "companyCity");
        var fullName    = GetString(slip, "employeeName");
        var empStreet   = GetString(slip, "address");
        var empZip      = GetString(slip, "zipCity");
        var perFrom     = GetString(slip, "periodFrom");
        var perTo       = GetString(slip, "periodTo");
        var printDate   = GetString(slip, "printDate");
        var footerText  = GetString(slip, "pdfFooterText");

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginLeft(2.5f, Unit.Centimetre);   // breiter linker Rand (Walter)
                page.MarginRight(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.2f).FontColor(Dark));

                // Header: Banner mit ZENTRIERTEM Titel "Lohnabrechnung März 2026"
                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer()
                        .PaddingTop(10)
                        .AlignCenter()
                        .Text(string.IsNullOrWhiteSpace(periodLabel)
                            ? "Lohnabrechnung"
                            : $"Lohnabrechnung {periodLabel}")
                        .Bold().FontSize(12f).FontColor(Dark);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    // ── Filiale (oben links) ──
                    col.Item().Column(p =>
                    {
                        if (!string.IsNullOrWhiteSpace(parentName))
                            p.Item().Text(parentName).Bold().FontSize(10f);
                        p.Item().Text(company).FontSize(9.5f);
                        if (!string.IsNullOrWhiteSpace(compAddr)) p.Item().Text(compAddr).FontSize(9f);
                        if (!string.IsNullOrWhiteSpace(compZip))  p.Item().Text(compZip).FontSize(9f);
                    });

                    // ── Mitarbeiter-Adresse (Fenstercouvert-Position) ──
                    // Adresse soll sich ungefähr 5–6 cm vom Seitenanfang positionieren,
                    // damit sie im Fenster eines Standard-CH-Couverts (DL/C5) erscheint.
                    col.Item().PaddingTop(35).Column(p =>
                    {
                        p.Item().Text(fullName).Bold().FontSize(10f);
                        if (!string.IsNullOrWhiteSpace(empStreet)) p.Item().Text(empStreet).FontSize(9.5f);
                        if (!string.IsNullOrWhiteSpace(empZip))    p.Item().Text(empZip).FontSize(9.5f);
                    });

                    // ── Datum: "Oftringen, TT.MM.JJJJ" (zwischen MA-Adresse und Lohnteilen) ──
                    col.Item().PaddingTop(25).Text(t =>
                    {
                        var city = string.IsNullOrWhiteSpace(companyCity) ? "" : companyCity + ", ";
                        t.Span($"{city}{printDate}").FontSize(9.5f);
                    });

                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span($"Periode: {perFrom} – {perTo}").FontSize(9f).FontColor(Muted);
                    });

                    // ── Lohn-Tabelle (mit grosszügigem Abstand zum Datum oben) ──
                    col.Item().PaddingTop(30).Element(e => RenderLohnTable(e, slip));

                    // ── Nettolohn ──
                    col.Item().PaddingTop(4).Element(e => RenderNettoBlock(e, slip));

                    // ── Stunden-Übersicht (MTP/FIX) ──
                    col.Item().PaddingTop(10).Element(e => RenderStundenBlock(e, slip));

                    // ── Saldi ──
                    col.Item().PaddingTop(8).Element(e => RenderSaldiBlock(e, slip));
                });

                // ── Page-Fussnote: nur Periode-Bemerkung (keine Seitenzahl) ──
                if (!string.IsNullOrWhiteSpace(footerText))
                {
                    page.Footer()
                        .BorderTop(0.5f).BorderColor(Dark).PaddingTop(4)
                        .Text(footerText)
                        .FontSize(8.5f).FontColor(Dark).Italic();
                }
            });
        }).GeneratePdf();
    }

    private static void RenderLohnTable(IContainer c, JsonElement slip)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(4);   // Bezeichnung
                cd.RelativeColumn(1);   // Anzahl
                cd.RelativeColumn(1);   // %
                cd.RelativeColumn(1);   // Basis
                cd.RelativeColumn(1);   // Gerechnet
                cd.RelativeColumn(1);   // Ausbezahlt
            });

            // Header
            t.Header(h =>
            {
                Cell(h.Cell(), "Bezeichnung", left: true, head: true);
                Cell(h.Cell(), "Anzahl",     right: true, head: true);
                Cell(h.Cell(), "%",          right: true, head: true);
                Cell(h.Cell(), "Basis",      right: true, head: true);
                Cell(h.Cell(), "Gerechnet",  right: true, head: true);
                Cell(h.Cell(), "Ausbezahlt", right: true, head: true);
            });

            // Lohn-Zeilen aus lohnLines
            var lohnLines = TryGetArray(slip, "lohnLines");
            if (lohnLines.HasValue)
            {
                t.Cell().ColumnSpan(6).PaddingTop(4).Text("Lohn").Bold().FontSize(9.5f);
                decimal totalLohn = 0;
                foreach (var line in lohnLines.Value.EnumerateArray())
                {
                    var bez   = GetString(line, "bezeichnung");
                    var anz   = GetDecimal(line, "anzahl");
                    var proz  = GetDecimal(line, "prozent");
                    var basis = GetDecimal(line, "basis");
                    var betr  = GetDecimal(line, "betrag");
                    var accr  = GetDecimal(line, "accrued");

                    Cell(t.Cell(), bez, left: true);
                    Cell(t.Cell(), anz.HasValue ? Num(anz, 2) : "", right: true);
                    Cell(t.Cell(), proz.HasValue ? Num(proz, 3) : "", right: true);
                    Cell(t.Cell(), basis.HasValue ? CHF(basis) : "", right: true);
                    // "Gerechnet" zeigt accrued wenn betrag = 0 (= aufgespart)
                    string gerechnetTxt = (betr == 0 && accr.HasValue && accr != 0) ? CHF(accr) : "";
                    Cell(t.Cell(), gerechnetTxt, right: true, color: Muted);
                    Cell(t.Cell(), CHF(betr), right: true);
                    if (betr.HasValue) totalLohn += betr.Value;
                }

                // Total Lohn
                Cell(t.Cell().BorderTop(0.5f).BorderColor(Dark), "Total Lohn", left: true, bold: true);
                t.Cell().ColumnSpan(4).BorderTop(0.5f).BorderColor(Dark).Text("");
                Cell(t.Cell().BorderTop(0.5f).BorderColor(Dark), CHF(totalLohn), right: true, bold: true);
            }

            // Abzüge
            var abzugLines = TryGetArray(slip, "abzugLines");
            if (abzugLines.HasValue)
            {
                t.Cell().ColumnSpan(6).PaddingTop(8).Text("Abzüge").Bold().FontSize(9.5f);
                foreach (var line in abzugLines.Value.EnumerateArray())
                {
                    var bez   = GetString(line, "bezeichnung");
                    var proz  = GetDecimal(line, "prozent");
                    var basis = GetDecimal(line, "basis");
                    var betr  = GetDecimal(line, "betrag");

                    Cell(t.Cell(), bez, left: true);
                    Cell(t.Cell(), "", right: true);
                    Cell(t.Cell(), proz.HasValue ? Num(proz, 3) : "", right: true);
                    Cell(t.Cell(), basis.HasValue ? CHF(basis) : "", right: true);
                    Cell(t.Cell(), "", right: true);
                    Cell(t.Cell(), betr.HasValue ? "-" + CHF(Math.Abs(betr.Value)) : "", right: true, color: Red);
                }

                var totalAbz = GetDecimal(slip, "totalDeductions");
                Cell(t.Cell().BorderTop(0.5f).BorderColor(Dark), "Total Abzüge", left: true, bold: true);
                t.Cell().ColumnSpan(4).BorderTop(0.5f).BorderColor(Dark).Text("");
                Cell(t.Cell().BorderTop(0.5f).BorderColor(Dark),
                    totalAbz.HasValue ? "-" + CHF(Math.Abs(totalAbz.Value)) : "",
                    right: true, bold: true, color: Red);
            }
        });
    }

    private static void RenderNettoBlock(IContainer c, JsonElement slip)
    {
        var nettolohn         = GetDecimal(slip, "nettolohn");
        var auszahlungsbetrag = GetDecimal(slip, "auszahlungsbetrag");
        var abzuegeExtra      = TryGetArray(slip, "abzuegeExtraLines");

        c.Column(col =>
        {
            col.Item().BorderTop(1f).BorderColor(Dark).PaddingTop(4).Row(r =>
            {
                r.RelativeItem().Text("Nettolohn").Bold().FontSize(11f);
                r.AutoItem().Text(CHF(nettolohn)).Bold().FontSize(11f);
            });

            // Weitere Abzüge (Lohnpfändung etc.)
            if (abzuegeExtra.HasValue && abzuegeExtra.Value.GetArrayLength() > 0)
            {
                col.Item().PaddingTop(6).Text("Weitere Abzüge").Bold().FontSize(9.5f);
                foreach (var line in abzuegeExtra.Value.EnumerateArray())
                {
                    var bez  = GetString(line, "bezeichnung");
                    var betr = GetDecimal(line, "betrag");
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(bez).FontSize(9f);
                        r.AutoItem().Text(betr.HasValue ? "-" + CHF(Math.Abs(betr.Value)) : "")
                            .FontSize(9f).FontColor(Red);
                    });
                }
            }

            col.Item().PaddingTop(4).BorderTop(1f).BorderColor(Dark).PaddingTop(4).Row(r =>
            {
                r.RelativeItem().Text("Auszahlungsbetrag").Bold().FontSize(11f);
                r.AutoItem().Text(CHF(auszahlungsbetrag)).Bold().FontSize(11f);
            });
        });
    }

    private static void RenderStundenBlock(IContainer c, JsonElement slip)
    {
        var model = GetString(slip, "employmentModel");
        if (model != "MTP" && model != "FIX" && model != "FIX-M") return;

        var soll       = GetDecimal(slip, "sollStunden");
        var workedHrs  = GetDecimal(slip, "workedHours");
        var absenz     = GetDecimal(slip, "absenzGutschrift");
        var ist        = (workedHrs ?? 0) + (absenz ?? 0);
        var diff       = ist - (soll ?? 0);
        var vor        = GetDecimal(slip, "vormonatHourSaldo");
        var saldo      = GetDecimal(slip, "neuerHourSaldo");

        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(3);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
            });
            t.Header(h =>
            {
                Cell(h.Cell(), "Stunden-Übersicht", left: true, head: true);
                Cell(h.Cell(), "Soll",       right: true, head: true);
                Cell(h.Cell(), "Ist",        right: true, head: true);
                Cell(h.Cell(), "Differenz",  right: true, head: true);
                Cell(h.Cell(), "Übertrag",   right: true, head: true);
                Cell(h.Cell(), "Saldo",      right: true, head: true);
            });
            Cell(t.Cell(), "Arbeitsstunden", left: true);
            Cell(t.Cell(), Num(soll), right: true);
            Cell(t.Cell(), Num(ist), right: true);
            Cell(t.Cell(), (diff > 0 ? "+" : "") + Num(diff), right: true,
                color: diff < 0 ? Red : (diff > 0 ? Green : null));
            Cell(t.Cell(), (vor > 0 ? "+" : "") + Num(vor ?? 0), right: true,
                color: vor < 0 ? Red : (vor > 0 ? Green : null));
            Cell(t.Cell(), Num(saldo), right: true, bold: true);
        });
    }

    private static void RenderSaldiBlock(IContainer c, JsonElement slip)
    {
        var ferienTageSaldo = GetDecimal(slip, "ferienTageSaldoNeu");
        var ferienGeldSaldo = GetDecimal(slip, "ferienGeldSaldoNeu");
        var feiertagSaldo   = GetDecimal(slip, "feiertagTageSaldoNeu");
        var thirteen        = GetDecimal(slip, "thirteenthAccumulated");

        bool hasSaldi = (ferienTageSaldo ?? 0) != 0 || (ferienGeldSaldo ?? 0) != 0
                     || (feiertagSaldo ?? 0) != 0 || (thirteen ?? 0) != 0;
        if (!hasSaldi) return;

        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(3);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
            });
            t.Header(h =>
            {
                Cell(h.Cell(), "Saldi", left: true, head: true);
                Cell(h.Cell(), "Vormonat",  right: true, head: true);
                Cell(h.Cell(), "Aktuell",   right: true, head: true);
                Cell(h.Cell(), "Bezogen",   right: true, head: true);
                Cell(h.Cell(), "Saldo",     right: true, head: true);
            });

            if ((ferienTageSaldo ?? 0) != 0 || GetDecimal(slip, "ferienTageAccrual") > 0)
            {
                var vor = GetDecimal(slip, "vormonatFerienTage");
                var acc = GetDecimal(slip, "ferienTageAccrual");
                var bez = GetDecimal(slip, "ferienTageGenommen");
                var weeks = GetInt(slip, "vacationWeeks");
                Cell(t.Cell(), $"Ferien-Saldo Tage ({weeks} Wo.)", left: true, color: Green);
                Cell(t.Cell(), Num(vor), right: true, color: Muted);
                Cell(t.Cell(), "+" + Num(acc), right: true, color: Green);
                Cell(t.Cell(), bez > 0 ? "-" + Num(bez) : "—", right: true, color: bez > 0 ? Red : Muted);
                Cell(t.Cell(), Num(ferienTageSaldo), right: true, bold: true,
                    color: (ferienTageSaldo ?? 0) >= 0 ? Green : Red);
            }

            if ((ferienGeldSaldo ?? 0) != 0)
            {
                var vor  = GetDecimal(slip, "vormonatFerienGeld");
                var acc  = GetDecimal(slip, "ferienGeldAccrual");
                var bez  = GetDecimal(slip, "ferienGeldAuszahlung");
                Cell(t.Cell(), "Ferien-Geld (CHF)", left: true, color: Green);
                Cell(t.Cell(), CHF(vor), right: true, color: Muted);
                Cell(t.Cell(), "+" + CHF(acc), right: true, color: Green);
                Cell(t.Cell(), bez > 0 ? "-" + CHF(bez) : "—", right: true, color: bez > 0 ? Red : Muted);
                Cell(t.Cell(), CHF(ferienGeldSaldo), right: true, bold: true,
                    color: (ferienGeldSaldo ?? 0) >= 0 ? Green : Red);
            }

            if ((feiertagSaldo ?? 0) != 0)
            {
                var vor = GetDecimal(slip, "vormonatFeiertagTage");
                var acc = GetDecimal(slip, "feiertagTageAccrual");
                var bez = GetDecimal(slip, "feiertagTageGenommen");
                Cell(t.Cell(), "Feiertag-Saldo Tage", left: true, color: Dark);
                Cell(t.Cell(), Num(vor), right: true, color: Muted);
                Cell(t.Cell(), "+" + Num(acc), right: true, color: Green);
                Cell(t.Cell(), bez > 0 ? "-" + Num(bez) : "—", right: true, color: bez > 0 ? Red : Muted);
                Cell(t.Cell(), Num(feiertagSaldo), right: true, bold: true, color: Dark);
            }

            if ((thirteen ?? 0) != 0)
            {
                var prev = GetDecimal(slip, "prevThirteenth");
                var monthly = GetDecimal(slip, "thirteenthMonthly");
                Cell(t.Cell(), "Rückst. 13. Monatslohn (CHF)", left: true);
                Cell(t.Cell(), CHF(prev), right: true, color: Muted);
                Cell(t.Cell(), "+" + CHF(monthly), right: true, color: Green);
                Cell(t.Cell(), "—", right: true, color: Muted);
                Cell(t.Cell(), CHF(thirteen), right: true, bold: true);
            }
        });
    }

    // ─── Cell-Helper ─────────────────────────────────────────────────
    // Linke Spalten ohne PaddingLeft (bündig mit Sektions-Titeln "Lohn"/"Abzüge").
    // Rechte Spalten mit PaddingRight=2 für leichten Abstand zum Spaltenende.
    private static void Cell(IContainer cell, string text,
        bool left = false, bool right = false,
        bool head = false, bool bold = false, string? color = null)
    {
        var c = cell.PaddingVertical(2);
        if (right) c = c.PaddingRight(2).AlignRight();
        else c = c.AlignLeft();
        var span = c.Text(text ?? "");
        if (head) span.FontSize(8.5f).FontColor(Muted);
        else span.FontSize(9f);
        if (bold) span.Bold();
        if (!string.IsNullOrEmpty(color)) span.FontColor(color);
    }

    // ─── JSON Helpers ────────────────────────────────────────────────
    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "") : "";

    private static decimal? GetDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        return null;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
        return 0;
    }

    private static JsonElement? TryGetArray(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind != JsonValueKind.Array) return null;
        return v;
    }

    private static string GetGermanMonthName(int m) => m switch
    {
        1 => "Januar", 2 => "Februar", 3 => "März", 4 => "April",
        5 => "Mai", 6 => "Juni", 7 => "Juli", 8 => "August",
        9 => "September", 10 => "Oktober", 11 => "November", 12 => "Dezember",
        _ => ""
    };
}
