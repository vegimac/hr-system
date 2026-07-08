using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Controllers;

/// <summary>
/// Alters-Auswertung über ALLE Filialen (Walter-Vorgabe 08.07.2026).
/// Namentliche Listen pro Alterskategorie, Filialen als Spalten
/// (Spaltentitel = RestaurantCode + Ort, z.B. «104 Langenthal»; Zellen kompakt:
/// Name + nur Alter, kein Geburtsdatum):
///   • unter 16 · 16–17 · 18–29 · 30–44 · 45–49 · 50+ (flächendeckend —
///     jeder aktive MA erscheint genau einmal; Pension sticht die Bänder)
///   • Pension in ≤ 1 Jahr (AHV-Referenzalter: Männer 65; Frauen AHV21-Übergang
///     Jahrgang 1961=64¼, 1962=64½, 1963=64¾, ab 1964=65)
///   • ohne Geburtsdatum (Datenqualitäts-Hinweis)
/// Nur AKTIVE MA mit heute laufendem Vertrag; Phantom-MA (ohne Lohn) ausgenommen.
/// GET /api/reports/alter (JSON) + GET /api/reports/alter/pdf (A4 quer).
/// Rein lesend — kein LohnEditLock nötig.
/// </summary>
[ApiController]
[Route("api/reports/alter")]
[Authorize(Roles = "admin,superuser")]
public class AgeReportController : ControllerBase
{
    private readonly AppDbContext _db;
    public AgeReportController(AppDbContext db) => _db = db;

    /// <summary>Namentlich=false → nur Anzahl anzeigen (Walter-Vorgabe 08.07.2026:
    /// 18–29 + 30–44 sind die Masse der Belegschaft, namentlich zu lang).</summary>
    private static readonly (string Key, string Label, string Hint, bool Namentlich)[] Kategorien =
    {
        ("u16",  "Unter 16",              "Jugendschutz ArG — verschärfte Einsatzgrenzen", true),
        ("u18",  "16–17 (unter 18)",      "Jugendschutz L-GAV / ArG beachten",             true),
        ("u30",  "18–29 (unter 30)",      "nur Anzahl",                                    false),
        ("u45",  "30–44 (unter 45)",      "nur Anzahl",                                    false),
        ("u50",  "45–49 (unter 50)",      "",                                              true),
        ("a50",  "50 und älter",          "ohne Pensions-Gruppe",                          true),
        ("pens", "Pension in ≤ 1 Jahr",   "AHV-Referenzalter erreicht oder in weniger als 12 Monaten", true),
        ("ogeb", "Ohne Geburtsdatum",     "Geburtsdatum fehlt — bitte nachtragen",         true),
    };

    // ─────────────────────────────────────────────────────────────────────
    //  Daten
    // ─────────────────────────────────────────────────────────────────────

    private sealed record PersonRow(int BranchId, string Name, int? Alter);

    private async Task<(List<(int Id, string Name)> Branches, Dictionary<string, List<PersonRow>> Rows, List<int> AlterAlle)> LoadAsync()
    {
        var today = DateTime.Today;

        // Spaltentitel „104 Langenthal" = RestaurantCode + Ort (Walter-Vorgabe 08.07.2026).
        var branchesRaw = await _db.CompanyProfiles.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new { b.Id, b.RestaurantCode, b.City, b.BranchName, b.CompanyName })
            .ToListAsync();
        var branches = branchesRaw
            .Select(b => new
            {
                b.Id,
                Name = $"{b.RestaurantCode} {(!string.IsNullOrWhiteSpace(b.City) ? b.City : (b.BranchName ?? b.CompanyName))}".Trim()
            })
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Aktive MA mit heute laufendem Vertrag.
        var raw = await _db.Employments.AsNoTracking()
            .Where(e => e.IsActive && e.CompanyProfileId != null
                     && e.ContractStartDate <= today
                     && (e.ContractEndDate == null || e.ContractEndDate >= today))
            .Join(_db.Employees.AsNoTracking()
                      .Where(p => p.IsActive && !p.IsPayrollExcluded),
                  e => e.EmployeeId, p => p.Id,
                  (e, p) => new
                  {
                      BranchId = e.CompanyProfileId!.Value,
                      e.ContractStartDate,
                      p.Id, p.FirstName, p.LastName, p.DateOfBirth, p.Gender, p.Salutation
                  })
            .ToListAsync();

        var result = Kategorien.ToDictionary(k => k.Key, _ => new List<PersonRow>());

        // Jeder MA GENAU EINMAL — in seiner Hauptfiliale (Walter-Vorgabe 08.07.2026).
        // Hauptfiliale = Filiale des ältesten LAUFENDEN Vertrags (das ursprüngliche
        // Restaurant; Zusatzverträge in anderen Filialen kommen später dazu).
        foreach (var g in raw.GroupBy(r => r.Id))
        {
            var p  = g.OrderBy(r => r.ContractStartDate).First();
            // Kompakt (Walter-Vorgabe 08.07.2026): Vorname + 1. Buchstabe Nachname.
            var ln   = (p.LastName ?? "").Trim();
            var name = (ln.Length > 0 ? $"{p.FirstName} {ln[0]}." : p.FirstName ?? "").Trim();

            if (p.DateOfBirth is not DateTime dob)
            {
                result["ogeb"].Add(new PersonRow(p.BranchId, name, null));
                continue;
            }

            var alter   = Age(dob, today);
            var pension = ReferenzAlterDatum(dob, p.Gender, p.Salutation);
            // Flächendeckende Bänder (Walter-Vorgabe 08.07.2026): jeder aktive MA
            // erscheint genau einmal. Pensions-Gruppe sticht die Altersbänder.
            var key =
                alter < 16                          ? "u16"
                : alter < 18                        ? "u18"
                : pension <= today.AddYears(1)      ? "pens"
                : alter < 30                        ? "u30"
                : alter < 45                        ? "u45"
                : alter < 50                        ? "u50"
                : "a50";

            result[key].Add(new PersonRow(p.BranchId, name, alter));
        }

        // Sortierung: Vorname (CLAUDE.md-Konvention) — Name beginnt mit Vorname.
        foreach (var list in result.Values)
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Altersverteilung über alle Filialen: pro PERSON genau einmal
        // (auch wenn sie in mehreren Filialen aktiv ist).
        var alterAlle = raw.GroupBy(r => r.Id)
            .Select(g => g.First().DateOfBirth)
            .Where(d => d.HasValue)
            .Select(d => Age(d!.Value, today))
            .OrderBy(a => a)
            .ToList();

        return (branches.Select(b => (b.Id, b.Name ?? "")).ToList(), result, alterAlle);
    }

    private static int Age(DateTime dob, DateTime today)
    {
        var a = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-a)) a--;
        return a;
    }

    /// <summary>AHV-Referenzalter-Datum: Männer 65; Frauen AHV21-Übergang
    /// (Jahrgang ≤1960: 64 · 1961: 64+3 Mt. · 1962: 64+6 Mt. · 1963: 64+9 Mt. · ab 1964: 65).</summary>
    private static DateTime ReferenzAlterDatum(DateTime dob, string? gender, string? salutation)
    {
        var g = (gender ?? "").ToLowerInvariant();
        var isFemale = g == "female" || g.StartsWith("w") || g == "f"
                       || string.Equals(salutation, "Frau", StringComparison.OrdinalIgnoreCase);
        if (!isFemale) return dob.AddYears(65);
        return dob.Year switch
        {
            <= 1960 => dob.AddYears(64),
            1961    => dob.AddYears(64).AddMonths(3),
            1962    => dob.AddYears(64).AddMonths(6),
            1963    => dob.AddYears(64).AddMonths(9),
            _       => dob.AddYears(65),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  JSON — für die Bildschirm-Tabelle
    // ─────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (branches, rows, alterAlle) = await LoadAsync();
        return Ok(new
        {
            stichtag = DateTime.Today.ToString("yyyy-MM-dd"),
            alterVerteilung = alterAlle, // pro Person einmal, für die Verteilkurve
            branches = branches.Select(b => new { id = b.Id, name = b.Name }),
            kategorien = Kategorien.Select(k => new
            {
                key = k.Key,
                label = k.Label,
                hint = k.Hint,
                namentlich = k.Namentlich,
                total = rows[k.Key].Count,
                counts = branches.ToDictionary(
                    b => b.Id.ToString(),
                    b => rows[k.Key].Count(r => r.BranchId == b.Id)),
                perBranch = branches.ToDictionary(
                    b => b.Id.ToString(),
                    b => (k.Namentlich ? rows[k.Key].Where(r => r.BranchId == b.Id) : Enumerable.Empty<PersonRow>())
                        .Select(r => new { r.Name, alter = r.Alter }))
            }),
            // Total-Zeile (Walter-Vorgabe 08.07.2026): Summe pro Filiale + gesamt.
            // Jeder MA zählt genau einmal (Hauptfiliale).
            totalPerBranch = branches.ToDictionary(
                b => b.Id.ToString(),
                b => rows.Values.Sum(list => list.Count(r => r.BranchId == b.Id))),
            totalAll = rows.Values.Sum(list => list.Count),
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PDF — A4 HOCH (Walter-Vorgabe 08.07.2026, komprimiert), Filialen als
    //  Spalten, Kategorien als Zeilen, unten die Altersverteilungs-Kurve.
    // ─────────────────────────────────────────────────────────────────────

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var (branches, rows, alterAlle) = await LoadAsync();
        var today = DateTime.Today;
        var title = $"Altersstruktur alle Filialen — Stichtag {today:dd.MM.yyyy}";

        var pdf = Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginLeft(1.0f, Unit.Centimetre);
                page.MarginRight(1.0f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(6.5f).LineHeight(1.15f).FontColor("#000000"));

                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer().PaddingTop(10).AlignCenter()
                        .Text(title).Bold().FontSize(12f);
                });

                page.Content().PaddingTop(8).Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(72); // Kategorie
                            foreach (var _ in branches) cd.RelativeColumn(1);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(HeadCell).Text("Alter").Bold();
                            foreach (var b in branches)
                                h.Cell().Element(HeadCell).Text(b.Name).Bold();
                        });

                        foreach (var k in Kategorien)
                        {
                            var alle = rows[k.Key];
                            if (k.Key == "ogeb" && alle.Count == 0) continue; // Hinweis-Zeile nur bei Bedarf

                            table.Cell().Element(BodyCell).Column(col =>
                            {
                                col.Item().Text($"{k.Label} ({alle.Count})").Bold().FontSize(7f);
                                if (!string.IsNullOrEmpty(k.Hint))
                                    col.Item().PaddingTop(1).Text(k.Hint).FontSize(5.5f).FontColor("#404040");
                            });

                            foreach (var b in branches)
                            {
                                var list = alle.Where(r => r.BranchId == b.Id).ToList();
                                table.Cell().Element(BodyCell).Column(col =>
                                {
                                    if (list.Count == 0) { col.Item().Text("—").FontColor("#909090"); return; }
                                    if (!k.Namentlich)
                                    {
                                        // nur Anzahl (Walter-Vorgabe 08.07.2026: 18–29 / 30–44)
                                        col.Item().Text($"{list.Count}").SemiBold().FontSize(7.5f);
                                        return;
                                    }
                                    foreach (var r in list)
                                    {
                                        col.Item().Text(t =>
                                        {
                                            t.Span(r.Name).SemiBold();
                                            if (r.Alter is int a)
                                                t.Span($" {a}").FontColor("#404040");
                                        });
                                    }
                                });
                            }
                        }

                        // Total-Zeile (Walter-Vorgabe 08.07.2026): Summe pro Filiale + gesamt.
                        var totalAll = rows.Values.Sum(l => l.Count);
                        table.Cell().Element(TotalCell)
                            .Text($"Total ({totalAll})").Bold().FontSize(7f);
                        foreach (var b in branches)
                        {
                            var n = rows.Values.Sum(l => l.Count(r => r.BranchId == b.Id));
                            table.Cell().Element(TotalCell).Text($"{n}").Bold().FontSize(7.5f);
                        }
                    });

                    // ── Altersverteilungs-Kurve über alle Filialen (Walter-Vorgabe
                    //    08.07.2026): Histogramm pro Altersjahr, jede Person einmal. ──
                    if (alterAlle.Count > 0)
                    {
                        content.Item().PaddingTop(14).Text($"Altersverteilung über alle Filialen ({alterAlle.Count} Personen, jede genau einmal)")
                            .Bold().FontSize(8f);
                        content.Item().PaddingTop(4)
                            .Svg(QuestPDF.Infrastructure.SvgImage.FromText(BuildAltersKurveSvg(alterAlle)))
                            .FitWidth();
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span($"OneCrew · erstellt {DateTime.Now:dd.MM.yyyy HH:mm} · nur aktive MA mit laufendem Vertrag · Seite ").FontSize(7f).FontColor("#404040");
                    t.CurrentPageNumber().FontSize(7f).FontColor("#404040");
                    t.Span(" / ").FontSize(7f).FontColor("#404040");
                    t.TotalPages().FontSize(7f).FontColor("#404040");
                });
            });
        }).GeneratePdf();

        return File(pdf, "application/pdf", $"altersstruktur_{today:yyyyMMdd}.pdf");

        static IContainer HeadCell(IContainer x) => x
            .Background("#efeae2").BorderBottom(1).BorderColor("#c8c0b2")
            .PaddingVertical(4).PaddingHorizontal(5);
        static IContainer BodyCell(IContainer x) => x
            .BorderBottom(0.5f).BorderColor("#ddd6c9")
            .PaddingVertical(4).PaddingHorizontal(5);
        static IContainer TotalCell(IContainer x) => x
            .Background("#efeae2").BorderTop(1).BorderColor("#c8c0b2")
            .PaddingVertical(4).PaddingHorizontal(5);
    }

    /// <summary>Altersverteilungs-KURVE als SVG (Walter-Vorgabe 08.07.2026):
    /// X-Achse fix 15–65, Ticks alle 5 Jahre, Punkte = Anzahl Personen pro
    /// 5-Jahres-Band ([15–20) … [60–65), letzter Punkt = 65+). Alter unter 15
    /// zählt zum ersten Band. Fläche + Linie + Punkt-Labels.</summary>
    private static string BuildAltersKurveSvg(List<int> ages)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.#", ci);

        // Bänder: die KRITISCHEN Gruppen <16 und 16–17 haben eigene Punkte
        // (Walter-Vorgabe 08.07.2026), dann 18–19 und ab 20 5-Jahres-Bänder.
        // Punkt sitzt in der BAND-MITTE, sonst liest man den Wert als «genau
        // dieses Alter». Kritisch = rot.
        var bands = new List<(double From, double To, string Label, bool Kritisch)>
        {
            (15, 16, "<16",  true),
            (16, 18, "16–17", true),
            (18, 20, "", false),
        };
        for (var t = 20; t < 65; t += 5) bands.Add((t, t + 5, "", false));
        bands.Add((65, 70, "65+", false));

        var counts = new int[bands.Count];
        foreach (var a in ages)
        {
            var v = Math.Clamp((double)a, 15, 69.9);
            for (var i = 0; i < bands.Count; i++)
                if (v >= bands[i].From && v < bands[i].To) { counts[i]++; break; }
        }
        var maxCount = Math.Max(1, counts.Max());

        const double W = 530, H = 140, ml = 14, mr = 14, top = 18, bottom = 20;
        double XA(double age) => ml + (W - ml - mr) * (age - 15) / (70.0 - 15);
        double Y(int c) => top + (H - top - bottom) * (1.0 - (double)c / maxCount);

        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(W)}\" height=\"{F(H)}\" viewBox=\"0 0 {F(W)} {F(H)}\">");

        // Gitter an den Band-Grenzen + X-Beschriftung (16, 18, 20, 25 … 65)
        var gridTicks = new List<int> { 16, 18 };
        for (var t = 20; t <= 65; t += 5) gridTicks.Add(t);
        foreach (var t in gridTicks)
        {
            sb.Append($"<line x1=\"{F(XA(t))}\" y1=\"{F(top)}\" x2=\"{F(XA(t))}\" y2=\"{F(H - bottom)}\" stroke=\"#e7e1d8\" stroke-width=\"0.6\"/>");
            sb.Append($"<text x=\"{F(XA(t))}\" y=\"{F(H - 7)}\" font-family=\"Arial\" font-size=\"7\" fill=\"#8b8b8b\" text-anchor=\"middle\">{t}{(t == 65 ? "+" : "")}</text>");
        }
        sb.Append($"<line x1=\"{F(ml)}\" y1=\"{F(H - bottom)}\" x2=\"{F(W - mr)}\" y2=\"{F(H - bottom)}\" stroke=\"#c8c0b2\" stroke-width=\"1\"/>");

        double Mid(int i) => (bands[i].From + bands[i].To) / 2.0;

        // Fläche unter der Kurve
        sb.Append("<path d=\"M").Append($"{F(XA(Mid(0)))} {F(H - bottom)} ");
        for (var i = 0; i < bands.Count; i++) sb.Append($"L{F(XA(Mid(i)))} {F(Y(counts[i]))} ");
        sb.Append($"L{F(XA(Mid(bands.Count - 1)))} {F(H - bottom)} Z\" fill=\"#b8ab93\" fill-opacity=\"0.30\"/>");

        // Kurve (Linie)
        sb.Append("<polyline points=\"");
        for (var i = 0; i < bands.Count; i++) sb.Append($"{F(XA(Mid(i)))},{F(Y(counts[i]))} ");
        sb.Append("\" fill=\"none\" stroke=\"#8a7d63\" stroke-width=\"1.8\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");

        // Punkte + Anzahl-Labels (kritische Bänder rot)
        for (var i = 0; i < bands.Count; i++)
        {
            var farbe = bands[i].Kritisch ? "#b91c1c" : "#8a7d63";
            sb.Append($"<circle cx=\"{F(XA(Mid(i)))}\" cy=\"{F(Y(counts[i]))}\" r=\"2.4\" fill=\"{farbe}\"/>");
            if (counts[i] > 0 || bands[i].Kritisch)
                sb.Append($"<text x=\"{F(XA(Mid(i)))}\" y=\"{F(Y(counts[i]) - 5)}\" font-family=\"Arial\" font-size=\"8\" fill=\"{(bands[i].Kritisch ? "#b91c1c" : "#404040")}\" text-anchor=\"middle\" font-weight=\"bold\">{counts[i]}</text>");
            if (bands[i].Kritisch)
            {
                // ACHTUNG: «<» ist in XML/SVG ein Steuerzeichen → escapen,
                // sonst ist das SVG ungültig (HTTP-500-Bug 08.07.2026).
                var lbl = bands[i].Label.Replace("<", "&lt;");
                sb.Append($"<text x=\"{F(XA(Mid(i)))}\" y=\"{F(top - 6)}\" font-family=\"Arial\" font-size=\"6.5\" fill=\"#b91c1c\" text-anchor=\"middle\">{lbl}</text>");
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
