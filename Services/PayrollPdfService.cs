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
                });

                // ── Footer fix unten (Walter 02.08.2026): Saldi + Bank + Bemerkung.
                // Oberer Teil (Lohn/Abzüge) bleibt flexibel im Content; der untere
                // Block klebt am Seitenende. Keine Trennstriche. Ca. 3 Zeilen
                // Abstand zwischen Bankinfo und Bemerkung.
                page.Footer().Column(fcol =>
                {
                    fcol.Item().Element(e => RenderSaldiBlock(e, slip));
                    fcol.Item().PaddingTop(12).Element(e => RenderAuszahlungBlock(e, slip));
                    if (!string.IsNullOrWhiteSpace(footerText))
                    {
                        // ~3 Zeilen (9.5pt × 1.2 × 3 ≈ 34pt)
                        fcol.Item().PaddingTop(34)
                            .Text(footerText)
                            .FontSize(8.5f).FontColor(Dark).Italic();
                    }
                });
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

                // Total Lohn (ohne Trennstrich — Walter 02.08.2026)
                Cell(t.Cell().PaddingTop(4), "Total Lohn", left: true, bold: true);
                t.Cell().ColumnSpan(4).PaddingTop(4).Text("");
                Cell(t.Cell().PaddingTop(4), CHF(totalLohn), right: true, bold: true);
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

                // Slip-Feld heisst totalAbzuege (nicht totalDeductions) —
                // sonst blieb die Betrags-Spalte leer (Walter 02.08.2026).
                var totalAbz = GetDecimal(slip, "totalAbzuege")
                            ?? GetDecimal(slip, "totalDeductions");
                Cell(t.Cell().PaddingTop(4), "Total Abzüge", left: true, bold: true);
                t.Cell().ColumnSpan(4).PaddingTop(4).Text("");
                Cell(t.Cell().PaddingTop(4),
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
            col.Item().PaddingTop(6).Row(r =>
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

            col.Item().PaddingTop(8).Row(r =>
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
        var nachtSaldo      = GetDecimal(slip, "neuerNachtSaldo");
        var thirteen        = GetDecimal(slip, "thirteenthAccumulated");

        // 13. ML-Saldo: MTP / FIX / FIX-M führen ihn immer (dort IMMER zeigen,
        // auch bei Saldo 0 — analog HTML-Lohnbeleg, Walter-Vorgabe). FLEX zahlt
        // monatlich aus, führt aber einen STEHENDEN Saldo während der Probezeit
        // (Probezeit-Pot) und/oder aus einem importierten Mirus-Alt-Saldo
        // (906-Vortrag) — dann setzt die Engine showFlexThirteenthSaldo=true
        // und die Zeile erscheint auch bei FLEX (Walter-Entscheidung 04.08.2026).
        var modelUpper = (GetString(slip, "employmentModel") ?? "").ToUpperInvariant();
        bool flex13Saldo = modelUpper == "FLEX"
                        && slip.TryGetProperty("showFlexThirteenthSaldo", out var f13)
                        && f13.ValueKind == JsonValueKind.True;
        bool show13Saldo = modelUpper == "MTP" || modelUpper == "FIX" || modelUpper == "FIX-M"
                        || flex13Saldo;

        bool hasSaldi = (ferienTageSaldo ?? 0) != 0 || (ferienGeldSaldo ?? 0) != 0
                     || (feiertagSaldo ?? 0) != 0 || (nachtSaldo ?? 0) != 0
                     || (thirteen ?? 0) != 0 || show13Saldo;
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

            // Nacht-Saldo (Stunden) — alle Vertragstypen
            if ((nachtSaldo ?? 0) != 0 || GetDecimal(slip, "nightBonus") > 0)
            {
                var vor = GetDecimal(slip, "vormonatNachtSaldo");
                var acc = GetDecimal(slip, "nightBonus");
                var bez = GetDecimal(slip, "nachtKompStunden");
                Cell(t.Cell(), "Nacht-Saldo (Stunden)", left: true, color: Dark);
                Cell(t.Cell(), Num(vor), right: true, color: Muted);
                Cell(t.Cell(), acc > 0 ? "+" + Num(acc) : "—", right: true, color: acc > 0 ? Green : Muted);
                Cell(t.Cell(), bez > 0 ? "-" + Num(bez) : "—", right: true, color: bez > 0 ? Red : Muted);
                Cell(t.Cell(), Num(nachtSaldo), right: true, bold: true, color: Dark);
            }

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

            // 13. Monatslohn-Saldo (MTP/FIX/FIX-M) — Auszahlungsmonat-Logik
            // analog HTML: bei payout > 0 zeigt Bezogen den Auszahlungsbetrag
            // und Saldo neu = 0; sonst Vormonat + Zuwachs = Saldo neu.
            if (show13Saldo)
            {
                var payout = GetDecimal(slip, "thirteenthPayout") ?? 0;
                if (payout > 0)
                {
                    var prevDisp    = GetDecimal(slip, "thirteenthPrevForDisplay")    ?? 0;
                    var accrualDisp = GetDecimal(slip, "thirteenthAccrualForDisplay") ?? 0;
                    Cell(t.Cell(), "Rückst. 13. Monatslohn (CHF)", left: true);
                    Cell(t.Cell(), CHF(prevDisp), right: true, color: Muted);
                    Cell(t.Cell(), "+" + CHF(accrualDisp), right: true, color: Green);
                    Cell(t.Cell(), "-" + CHF(payout), right: true, color: Red);
                    Cell(t.Cell(), CHF(0), right: true, bold: true);
                }
                else
                {
                    var monthly = GetDecimal(slip, "thirteenthMonthly") ?? 0;
                    var accumulated = thirteen ?? 0;
                    var prev = Math.Round(accumulated - monthly, 2);
                    if (prev < 0) prev = 0;
                    Cell(t.Cell(), "Rückst. 13. Monatslohn (CHF)", left: true);
                    Cell(t.Cell(), CHF(prev), right: true, color: Muted);
                    Cell(t.Cell(), monthly > 0 ? "+" + CHF(monthly) : "—", right: true,
                        color: monthly > 0 ? Green : Muted);
                    Cell(t.Cell(), "—", right: true, color: Muted);
                    Cell(t.Cell(), CHF(accumulated), right: true, bold: true);
                }
            }
        });
    }

    private static void RenderAuszahlungBlock(IContainer c, JsonElement slip)
    {
        var empfaenger = TryGetArray(slip, "auszahlungEmpfaenger");
        if (!empfaenger.HasValue || empfaenger.Value.GetArrayLength() == 0) return;

        // Format IBAN in 4er-Gruppen für lesbare Anzeige (CH00 1234 5678 ...).
        static string FormatIban(string? iban)
        {
            if (string.IsNullOrWhiteSpace(iban)) return "";
            var clean = new string(iban!.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < clean.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append(' ');
                sb.Append(clean[i]);
            }
            return sb.ToString();
        }

        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(3);   // Empfänger
                cd.RelativeColumn(3);   // IBAN
                cd.RelativeColumn(1);   // Betrag
            });

            // Kein Tabellen-Header, kein Total — nur Empfänger-Zeilen.
            // Schrift kleiner (8.5pt), Beträge nicht fett — reine Info.
            foreach (var entry in empfaenger.Value.EnumerateArray())
            {
                var label   = GetString(entry, "label");
                var iban    = GetString(entry, "iban");
                var betrag  = GetDecimal(entry, "betrag");
                var warning = entry.TryGetProperty("warning", out var w)
                              && w.ValueKind == JsonValueKind.True;
                var referenz = GetString(entry, "referenz");

                AuszahlCell(t.Cell(), label, left: true, color: warning ? "#B00000" : null);
                AuszahlCell(t.Cell(),
                    string.IsNullOrWhiteSpace(iban)
                        ? (warning ? "— keine IBAN —" : "")
                        : FormatIban(iban),
                    left: true,
                    color: warning ? "#B00000" : null);
                AuszahlCell(t.Cell(), CHF(betrag), right: true);

                // Optional: Zahlungsreferenz unter dem Empfänger anzeigen
                if (!string.IsNullOrWhiteSpace(referenz))
                {
                    AuszahlCell(t.Cell().PaddingLeft(0), $"Ref.: {referenz}", left: true, color: Muted);
                    t.Cell().ColumnSpan(2).Text("");
                }
            }
        });
    }

    // Kompakte Variante von Cell() für die Auszahlungs-Sektion: kleinere
    // Schrift (8.5pt), kein Bold, weniger vertikales Padding.
    private static void AuszahlCell(IContainer cell, string text,
        bool left = false, bool right = false, string? color = null)
    {
        var c = cell.PaddingVertical(1);
        if (right) c = c.PaddingRight(2).AlignRight();
        else c = c.AlignLeft();
        var span = c.Text(text ?? "").FontSize(8.5f);
        if (!string.IsNullOrEmpty(color)) span.FontColor(color);
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
