using System.Globalization;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Monatsblatt «Stundenkontrolle» pro MA (Walter-Vorgabe 01.08.2026).
/// Analog Mirus «Monatsblatt effektive Zeiten»: Tagesraster mit Stempelzeiten,
/// Absenzen, Stunden-/Nacht-Saldi sowie Ferien-Geld / 13. ML in CHF —
/// plus Unterschriftsfeld, damit der MA seine Stunden kontrolliert und
/// unterschreibt. Geht zusammen mit dem Lohnzettel ins MA-Postfach.
/// </summary>
public class StundenkontrollePdfService
{
    private readonly AppDbContext _db;

    public StundenkontrollePdfService(AppDbContext db) => _db = db;

    private const string Dark  = "#000000";
    private const string Muted = "#404040";
    private const string Line  = "#c8c8c8";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static readonly string[] MonatsNamen =
    {
        "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember"
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Ch  = CultureInfo.GetCultureInfo("de-CH");

    private static string Num(decimal? v, int dec = 2) =>
        v == null ? "" : v.Value.ToString($"F{dec}", Inv);

    private static string Chf(decimal? v)
    {
        if (v == null) return "0.00";
        return v.Value.ToString("N2", Ch);
    }

    private static string KstLabel(string? model) => (model ?? "").ToUpperInvariant() switch
    {
        "FLEX"  => "200 Crew Flex",
        "MTP"   => "100 Crew Fix",
        "FIX"   => "100 Crew Fix",
        "FIX-M" => "300 Management",
        _       => model ?? "—"
    };

    private static string AbsenzLabel(string type) => (type ?? "").ToUpperInvariant() switch
    {
        "FERIEN"       => "Ferien",
        "FEIERTAG"     => "Feier",
        "KRANK"        => "Krank",
        "UNFALL"       => "Unfall",
        "SCHULUNG"     => "Schulung",
        "MUTTERSCHAFT" => "Mutterschaft",
        "NACHT_KOMP"   => "Nacht-Komp",
        "FREI"         => "Frei",
        _              => string.IsNullOrWhiteSpace(type) ? "Abw." : type
    };

    public async Task<byte[]> GenerateAsync(
        int employeeId,
        int year,
        int month,
        int companyProfileId,
        JsonElement? slip = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var emp = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException($"Mitarbeiter {employeeId} nicht gefunden.");

        var company = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyProfileId)
            ?? throw new InvalidOperationException($"Filiale {companyProfileId} nicht gefunden.");

        var periodFrom = new DateOnly(year, month, 1);
        var periodTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var periodFromDt = periodFrom.ToDateTime(TimeOnly.MinValue);
        var periodToDt   = periodTo.ToDateTime(TimeOnly.MaxValue);

        var employment = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId
                     && e.CompanyProfileId == companyProfileId
                     && e.ContractStartDate <= periodToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodFromDt))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();

        // MIT Rand geladen (±3 Tage): die Ruhetag-Klassifizierung (L-GAV Art. 16,
        // Walter-Vorgabe 04.08.2026) braucht das letzte Arbeitsende VOR bzw. den
        // ersten Arbeitsbeginn NACH der Periode, um das 35-Stunden-Fenster über
        // die Monatsgrenze zu rechnen. Angezeigt werden nur periodFrom..periodTo.
        var timeEntriesExt = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => t.EmployeeId == employeeId
                     && t.EntryDate >= periodFrom.AddDays(-3)
                     && t.EntryDate <= periodTo.AddDays(3))
            .OrderBy(t => t.EntryDate)
            .ThenBy(t => t.TimeIn)
            .ToListAsync();
        var timeEntries = timeEntriesExt
            .Where(t => t.EntryDate >= periodFrom && t.EntryDate <= periodTo)
            .ToList();

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= periodTo
                     && a.DateTo >= periodFrom)
            .ToListAsync();

        // Saldi: aktuelle + Vormonats-Periode (Vortrag).
        var currSaldo = await _db.PayrollSaldos.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId
                                   && s.CompanyProfileId == companyProfileId
                                   && s.PeriodYear == year
                                   && s.PeriodMonth == month);

        var (prevY, prevM) = month == 1 ? (year - 1, 12) : (year, month - 1);
        var prevSaldo = await _db.PayrollSaldos.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId
                                   && s.CompanyProfileId == companyProfileId
                                   && s.PeriodYear == prevY
                                   && s.PeriodMonth == prevM);

        var model = employment?.EmploymentModel
                 ?? (slip.HasValue ? GetString(slip.Value, "employmentModel") : null)
                 ?? "";

        // Ruhetag-Klassifizierung (L-GAV Art. 16) über die ERWEITERTEN Einträge
        // (Fenster über die Monatsgrenze); offene Stempel (ohne TimeOut) zählen
        // nicht als Arbeitsintervall.
        var workIntervals = timeEntriesExt
            .Where(t => t.TimeOut.HasValue)
            .Select(t => (Start: t.TimeIn, End: t.TimeOut!.Value))
            .OrderBy(w => w.Start)
            .ToList();
        var ruhetage = ClassifyRuhetage(periodFrom, periodTo, workIntervals,
            d => !string.IsNullOrEmpty(FormatAbsencesForDay(d, absences)));
        int ruheGanz = ruhetage.Count(x => x.Value == RuhetagArt.Ganzer);
        int ruheHalb = ruhetage.Count(x => x.Value is RuhetagArt.HalberVormittag or RuhetagArt.HalberNachmittag);

        var dayRows = BuildDayRows(periodFrom, periodTo, timeEntries, absences, ruhetage);
        var totalHours = dayRows.Sum(r => r.TotalHours);
        var totalNight = dayRows.Sum(r => r.NightHours);

        var monatLabel = (month >= 1 && month <= 12)
            ? $"{MonatsNamen[month - 1]} {year}"
            : $"{year}-{month:D2}";

        var printDate = slip.HasValue
            ? (GetString(slip.Value, "printDate") ?? DateTime.Now.ToString("dd.MM.yyyy"))
            : DateTime.Now.ToString("dd.MM.yyyy");

        var kst = KstLabel(model);
        var verhaeltnis = FormatVerhaeltnis(employment);

        // Saldi-Zahlen: Slip bevorzugen (konsistent mit Lohnzettel), sonst DB-Saldi.
        var saldi = BuildSaldi(slip, currSaldo, prevSaldo, totalHours, totalNight, model);

        var headerLeft = BuildCompanyHeader(company);
        var fullName = $"{emp.LastName} {emp.FirstName}".Trim();
        var empNr = emp.EmployeeNumber ?? emp.Id.ToString();
        var companyCity = company.City ?? "";

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginLeft(1.5f, Unit.Centimetre);
                page.MarginRight(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(8.5f).LineHeight(1.15f).FontColor(Dark));

                page.Header().Height(36).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer()
                        .PaddingTop(9)
                        .AlignCenter()
                        .Text($"Monatsblatt Stundenkontrolle  ·  {monatLabel}")
                        .Bold().FontSize(11f).FontColor(Dark);
                });

                page.Content().PaddingTop(6).Column(col =>
                {
                    // Filiale + Druckdatum
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(p =>
                        {
                            for (int i = 0; i < headerLeft.Count; i++)
                            {
                                var line = headerLeft[i];
                                if (i == 0) p.Item().Text(line).Bold().FontSize(9.5f);
                                else        p.Item().Text(line).FontSize(8.5f);
                            }
                        });
                        r.ConstantItem(120).AlignRight().Text($"Datum  {printDate}").FontSize(8f).FontColor(Muted);
                    });

                    // MA-Kopf
                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(110);
                            cd.RelativeColumn(2);
                            cd.ConstantColumn(110);
                            cd.RelativeColumn(2);
                        });
                        MetaRow(t, "Name / Vorname", fullName, "Personalnummer", empNr);
                        // Arbeitsverhältnis (Beginn – unbefristet) bewusst
                        // NICHT zeigen (Walter 04.08.2026).
                        MetaRow(t, "Kostenstelle", kst, "", "");
                    });

                    // Saldi-Übersicht (Stunden + CHF)
                    col.Item().PaddingTop(8).Text("Saldi (Kontrolle)").Bold().FontSize(9f);
                    col.Item().PaddingTop(2).Element(e => RenderSaldiTable(e, saldi, model));

                    // Tagesraster
                    col.Item().PaddingTop(8).Text("Arbeitszeiten / Absenzen").Bold().FontSize(9f);
                    col.Item().PaddingTop(2).Element(e => RenderDayTable(e, dayRows, totalHours, totalNight));

                    // Ruhetag-Bilanz für die L-GAV-Kontrolle (Art. 16: 2 Ruhe-
                    // tage/Woche, davon mind. 1 ganzer; 2 halbe = 1 Ruhetag).
                    col.Item().PaddingTop(3).Text(
                        $"Ruhetage: {ruheGanz} ganze · {ruheHalb} halbe" +
                        " — L-GAV Art. 16: 2 Ruhetage/Woche, davon mind. 1 ganzer; 2 halbe = 1 ganzer.")
                        .FontSize(7.5f).FontColor(Muted);

                    // Unterschrift
                    col.Item().PaddingTop(14).BorderTop(0.5f).BorderColor(Line).PaddingTop(8).Column(sig =>
                    {
                        sig.Item().Text(
                            "Ich bestätige, dass ich die oben aufgeführten Arbeitszeiten und Absenzen " +
                            "kontrolliert habe und diese korrekt sind.")
                            .FontSize(8.5f);
                        // Walter 04.08.2026: links nur «Datum:» (kein Ort, keine
                        // Striche); rechts KEINE Linie, Name des MA weiter unten,
                        // damit Platz für die Unterschrift bleibt.
                        sig.Item().PaddingTop(14).Row(r =>
                        {
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text("Datum:").FontSize(8.5f);
                            });
                            r.ConstantItem(20);
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text("Unterschrift Mitarbeiter/in:").FontSize(8.5f);
                                c2.Item().PaddingTop(72).Text(fullName).FontSize(7.5f).FontColor(Muted);
                            });
                        });
                    });
                });

                // Ohne Seitenzahl (Walter 04.08.2026) — im Sammel-PDF aller MA
                // wäre eine fortlaufende Seitennummer ohnehin irreführend.
                page.Footer().AlignCenter()
                    .Text("Bitte unterschriebenes Blatt dem GF / HR zurückgeben")
                    .FontSize(7.5f).FontColor(Muted);
            });
        }).GeneratePdf();
    }

    // ── Datenaufbereitung ──────────────────────────────────────────────

    private sealed record DayRow(
        DateOnly Date,
        string Weekday,
        string Arbeitszeit,
        decimal TotalHours,
        decimal NightHours,
        string Abwesenheit,
        string Bemerkung);

    private sealed record SaldiBlock(
        decimal StundenVortrag,
        decimal StundenIst,
        decimal StundenBezug,
        decimal StundenSaldo,
        decimal NachtVortrag,
        decimal NachtIst,
        decimal NachtBezug,
        decimal NachtSaldo,
        decimal FerienTageVortrag,
        decimal FerienTageAccrual,
        decimal FerienTageBezug,
        decimal FerienTageSaldo,
        decimal FeiertagVortrag,
        decimal FeiertagAccrual,
        decimal FeiertagBezug,
        decimal FeiertagSaldo,
        decimal FerienGeldVortrag,
        decimal FerienGeldAccrual,
        decimal FerienGeldBezug,
        decimal FerienGeldSaldo,
        decimal ThirteenVortrag,
        decimal ThirteenAccrual,
        decimal ThirteenBezug,
        decimal ThirteenSaldo,
        decimal FeiertagGeldAuszahlung,
        decimal AuszahlungLohn,
        bool ShowThirteen,
        bool ShowFerienGeld,
        bool ShowFeiertag,
        bool ShowFeiertagGeld,
        bool ShowAuszahlung,
        bool IsFlex);

    // ── Ruhetag-Klassifizierung L-GAV Art. 16 (Walter-Vorgabe 04.08.2026) ──
    //
    // Für die L-GAV-Kontrolle werden «Frei»-Tage auf dem Kontrollblatt
    // klassifiziert. Regeln (Walter, nach L-GAV-Kontrollstelle):
    //
    //   GANZER Ruhetag    — im Anschluss an die tägliche Nachtruhe (11 Std)
    //                       weitere 24 Std vollständig arbeitsfrei, d.h.
    //                       mind. 35 zusammenhängende freie Stunden zwischen
    //                       letztem Arbeitsende und nächstem Arbeitsbeginn.
    //                       Bei mehreren freien Tagen am Stück qualifiziert
    //                       der k-te freie Tag, wenn das Fenster
    //                       ≥ 11 + 24×k Stunden misst (Bsp: Fr 22:00 → Mo
    //                       06:00 = 56 Std → Sa ganzer Ruhetag, So nicht —
    //                       für den zweiten fehlen 59 Std).
    //   HALBER Ruhetag    — Vormittag frei: Arbeitsbeginn frühestens 12:00,
    //                       Tagesarbeitszeit höchstens 5 Std. ODER
    //                       Nachmittag frei: Arbeitsende spätestens 14:30,
    //                       Tagesarbeitszeit höchstens 5 Std.
    //                       (Gegenbeispiel der Kontrollstelle: 10:00–15:00 =
    //                       5 Std, aber KEIN halber Ruhetag.)
    //   KEIN Ruhetag      — keine der Bedingungen erfüllt.
    //
    // Fenster über den Datenrand hinaus (keine Arbeit im geladenen Bereich
    // davor/danach) gelten als offen → qualifizieren. Absenz-Tage (Ferien,
    // Krank, …) werden NICHT klassifiziert — sie tragen ihre eigene Kategorie.

    public enum RuhetagArt { Keiner, Ganzer, FreiOhneRuhetag, HalberVormittag, HalberNachmittag }

    /// <summary>Statisch + seiteneffektfrei — unit-testbar (Tests/RuhetagKlassifizierungTests).</summary>
    public static Dictionary<DateOnly, RuhetagArt> ClassifyRuhetage(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<(DateTime Start, DateTime End)> work,
        Func<DateOnly, bool> istAbwesend)
    {
        var sorted = work.OrderBy(w => w.Start).ToList();
        var result = new Dictionary<DateOnly, RuhetagArt>();

        bool OverlapsDay(DateOnly d)
        {
            var s = d.ToDateTime(TimeOnly.MinValue);
            var e = s.AddDays(1);
            return sorted.Any(w => w.Start < e && w.End > s);
        }

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (istAbwesend(d)) continue;

            var dayStart = d.ToDateTime(TimeOnly.MinValue);
            var dayEnd   = dayStart.AddDays(1);
            var dayIv    = sorted.Where(w => DateOnly.FromDateTime(w.Start) == d).ToList();

            if (dayIv.Count > 0)
            {
                // Arbeitstag → halber Ruhetag prüfen. Tagesarbeitszeit = Summe
                // der Einsatz-Dauern (Pausen zwischen Einsätzen zählen nicht).
                var total        = (decimal)dayIv.Sum(w => (w.End - w.Start).TotalHours);
                var earliestIn   = dayIv.Min(w => w.Start);
                var latestOut    = dayIv.Max(w => w.End);
                bool maxFuenf    = total <= 5.0m;

                if (maxFuenf && earliestIn.TimeOfDay >= new TimeSpan(12, 0, 0))
                    result[d] = RuhetagArt.HalberVormittag;
                else if (maxFuenf && latestOut <= d.ToDateTime(new TimeOnly(14, 30)))
                    result[d] = RuhetagArt.HalberNachmittag;
                else
                    result[d] = RuhetagArt.Keiner;
                continue;
            }

            if (OverlapsDay(d))
            {
                // Nur der Auslauf einer Nachtschicht vom Vortag — kein voller
                // freier Kalendertag, keine Klassifizierung.
                result[d] = RuhetagArt.Keiner;
                continue;
            }

            // Vollständig freier Tag → Fenster letztes Arbeitsende … nächster
            // Arbeitsbeginn. Kein Eintrag im geladenen Bereich = offenes Fenster.
            var prevEnd   = sorted.Where(w => w.End <= dayStart).Select(w => (DateTime?)w.End).Max();
            var nextStart = sorted.Where(w => w.Start >= dayEnd).Select(w => (DateTime?)w.Start).Min();

            if (prevEnd is null || nextStart is null)
            {
                result[d] = RuhetagArt.Ganzer;
                continue;
            }

            // k = wievielter zählbarer freier Tag seit dem letzten Arbeitsende
            // (Absenz-Tage zählen nicht mit — sie verbrauchen keinen Ruhetag-Slot).
            int k = 1;
            for (var p = DateOnly.FromDateTime(prevEnd.Value); p < d; p = p.AddDays(1))
            {
                if (p <= DateOnly.FromDateTime(prevEnd.Value)) continue;
                if (!istAbwesend(p) && !OverlapsDay(p)) k++;
            }

            var windowHours = (decimal)(nextStart.Value - prevEnd.Value).TotalHours;
            result[d] = windowHours >= 11m + 24m * k
                ? RuhetagArt.Ganzer
                : RuhetagArt.FreiOhneRuhetag;
        }
        return result;
    }

    private static List<DayRow> BuildDayRows(
        DateOnly from,
        DateOnly to,
        List<EmployeeTimeEntry> entries,
        List<Absence> absences,
        IReadOnlyDictionary<DateOnly, RuhetagArt>? ruhetage = null)
    {
        var byDate = entries.GroupBy(e => e.EntryDate).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<DayRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var dayEntries);
            dayEntries ??= new List<EmployeeTimeEntry>();

            var segments = dayEntries
                .Select(e =>
                {
                    var tin  = e.TimeIn.ToString("HH:mm");
                    var tout = e.TimeOut.HasValue ? e.TimeOut.Value.ToString("HH:mm") : "…";
                    return $"{tin}-{tout}";
                })
                .ToList();
            var arbeitszeit = string.Join("  ", segments);
            var total = dayEntries.Sum(e => e.TotalHours ?? e.DurationHours ?? 0m);
            var night = dayEntries.Sum(e => e.NightHours ?? 0m);
            var bem = string.Join("; ", dayEntries
                .Select(e => e.Comment)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()!) ?? "";

            var abw = FormatAbsencesForDay(d, absences);

            // Ruhetag-Klassifizierung einblenden (L-GAV-Kontrolle). Knappe
            // Schreibweise (Walter 04.08.2026): nur «1.0 Frei» (ganzer Ruhetag)
            // bzw. «0.5 Frei» (halber) in der Abwesenheits-Spalte — KEINE
            // Bemerkung. Freie Tage OHNE gültigen Ruhetag (< 35 Std Fenster)
            // bleiben bewusst leer: sie zählen für die Kontrolle nicht.
            if (ruhetage != null && ruhetage.TryGetValue(d, out var art))
            {
                switch (art)
                {
                    case RuhetagArt.Ganzer:
                        abw = string.IsNullOrEmpty(abw) ? "1.0 Frei" : abw + ", 1.0 Frei";
                        break;
                    case RuhetagArt.HalberVormittag:
                    case RuhetagArt.HalberNachmittag:
                        abw = string.IsNullOrEmpty(abw) ? "0.5 Frei" : abw + ", 0.5 Frei";
                        break;
                }
            }

            rows.Add(new DayRow(
                d,
                d.ToString("ddd", Ch).Replace(".", ""),
                arbeitszeit,
                total,
                night,
                abw,
                bem));
        }
        return rows;
    }

    private static string FormatAbsencesForDay(DateOnly day, List<Absence> absences)
    {
        var parts = new List<string>();
        foreach (var a in absences)
        {
            if (!AbsenceCoversDay(a, day)) continue;
            var label = AbsenzLabel(a.AbsenceType);
            var pct = a.Prozent <= 0 || a.Prozent >= 100 ? 1.0m : a.Prozent / 100m;
            parts.Add($"{Num(pct, 1)} {label}");
        }
        return string.Join(", ", parts);
    }

    private static bool AbsenceCoversDay(Absence a, DateOnly day)
    {
        if (!string.IsNullOrWhiteSpace(a.WorkedDays))
        {
            try
            {
                using var doc = JsonDocument.Parse(a.WorkedDays);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var iso = day.ToString("yyyy-MM-dd");
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.GetString() == iso) return true;
                    }
                    // Wenn WorkedDays gesetzt ist, gilt nur die explizite Liste
                    // (nicht der volle DateFrom–DateTo-Bereich).
                    return false;
                }
            }
            catch { /* Fallback auf Datumsbereich */ }
        }
        return a.DateFrom <= day && a.DateTo >= day;
    }

    private static SaldiBlock BuildSaldi(
        JsonElement? slip,
        PayrollSaldo? curr,
        PayrollSaldo? prev,
        decimal istHours,
        decimal istNight,
        string model)
    {
        var modelU = (model ?? "").ToUpperInvariant();
        bool isFlex = modelU == "FLEX";
        bool showFerienGeld = modelU is "FLEX" or "MTP";
        bool showFeiertag = modelU is "FIX" or "FIX-M";

        if (slip.HasValue)
        {
            var s = slip.Value;
            var payout13 = GetDecimal(s, "thirteenthPayout") ?? 0;
            var monthly13 = GetDecimal(s, "thirteenthMonthly") ?? 0;
            var acc13     = GetDecimal(s, "thirteenthAccumulated") ?? 0;
            decimal tVortrag, tAccrual, tBezug, tSaldo;
            if (payout13 > 0)
            {
                // Auszahlung (MTP/FIX Auszahlungsmonat oder FLEX monatlich).
                tVortrag = GetDecimal(s, "thirteenthPrevForDisplay") ?? 0;
                tAccrual = GetDecimal(s, "thirteenthAccrualForDisplay") ?? 0;
                if (tAccrual <= 0) tAccrual = isFlex ? payout13 : monthly13;
                tBezug = payout13;
                tSaldo = isFlex ? 0 : acc13;
            }
            else
            {
                tVortrag = Math.Max(0, Math.Round(acc13 - monthly13, 2));
                tAccrual = monthly13;
                tBezug   = 0;
                tSaldo   = acc13;
            }

            // 13. ML: MTP/FIX/FIX-M immer; FLEX wenn dieser Monat ausbezahlt/akkumuliert.
            bool show13 = modelU is "MTP" or "FIX" or "FIX-M"
                       || (isFlex && (payout13 > 0 || monthly13 > 0 || acc13 > 0 || tAccrual > 0));

            var worked = GetDecimal(s, "workedHours") ?? istHours;
            // FLEX: gestempelte Stunden werden mit dem Lohn ausbezahlt → Bezug,
            // kein Stundensaldo. MTP/FIX: Zuwachs = Ist, Bezug nur bei Saldo-Auszahlung (hier 0).
            decimal stundenBezug = isFlex ? worked : 0;
            decimal stundenSaldo = isFlex
                ? 0
                : (GetDecimal(s, "neuerHourSaldo") ?? curr?.HourSaldo ?? 0);

            var nachtBezug = GetDecimal(s, "nachtKompStunden") ?? 0;
            var feiertagGeld = FindLohnLineBetrag(s, "Feiertagentschädigung");
            var auszahlung = GetDecimal(s, "auszahlungsbetrag")
                          ?? GetDecimal(s, "nettolohn")
                          ?? 0;

            return new SaldiBlock(
                StundenVortrag: GetDecimal(s, "vormonatHourSaldo") ?? prev?.HourSaldo ?? 0,
                StundenIst:     worked,
                StundenBezug:   stundenBezug,
                StundenSaldo:   stundenSaldo,
                NachtVortrag:   GetDecimal(s, "vormonatNachtSaldo") ?? prev?.NachtSaldo ?? 0,
                NachtIst:       GetDecimal(s, "nightBonus") ?? Math.Round(istNight * 0.10m, 2),
                NachtBezug:     nachtBezug,
                NachtSaldo:     GetDecimal(s, "neuerNachtSaldo") ?? curr?.NachtSaldo ?? 0,
                FerienTageVortrag: GetDecimal(s, "vormonatFerienTage") ?? prev?.FerienTageSaldo ?? 0,
                FerienTageAccrual: GetDecimal(s, "ferienTageAccrual") ?? 0,
                FerienTageBezug:   GetDecimal(s, "ferienTageGenommen") ?? 0,
                FerienTageSaldo:   GetDecimal(s, "ferienTageSaldoNeu") ?? curr?.FerienTageSaldo ?? 0,
                FeiertagVortrag: GetDecimal(s, "vormonatFeiertagTage") ?? prev?.FeiertagTageSaldo ?? 0,
                FeiertagAccrual: GetDecimal(s, "feiertagTageAccrual") ?? 0,
                FeiertagBezug:   GetDecimal(s, "feiertagTageGenommen") ?? 0,
                FeiertagSaldo:   GetDecimal(s, "feiertagTageSaldoNeu") ?? curr?.FeiertagTageSaldo ?? 0,
                FerienGeldVortrag: GetDecimal(s, "vormonatFerienGeld") ?? prev?.FerienGeldSaldo ?? 0,
                FerienGeldAccrual: GetDecimal(s, "ferienGeldAccrual") ?? 0,
                FerienGeldBezug:   GetDecimal(s, "ferienGeldAuszahlung") ?? 0,
                FerienGeldSaldo:   GetDecimal(s, "ferienGeldSaldoNeu") ?? curr?.FerienGeldSaldo ?? 0,
                ThirteenVortrag: tVortrag,
                ThirteenAccrual: tAccrual,
                ThirteenBezug:   tBezug,
                ThirteenSaldo:   tSaldo,
                FeiertagGeldAuszahlung: feiertagGeld,
                AuszahlungLohn: auszahlung,
                ShowThirteen: show13,
                ShowFerienGeld: showFerienGeld || (GetDecimal(s, "ferienGeldSaldoNeu") ?? 0) != 0
                                             || (GetDecimal(s, "ferienGeldAccrual") ?? 0) != 0,
                ShowFeiertag: showFeiertag || (GetDecimal(s, "feiertagTageSaldoNeu") ?? 0) != 0,
                ShowFeiertagGeld: isFlex && feiertagGeld > 0,
                ShowAuszahlung: auszahlung > 0,
                IsFlex: isFlex);
        }

        // Ohne Slip: Nacht-Saldo = 10 % der Nachtstunden (wie PayrollCalculationEngine).
        var nachtKomp = Math.Round(istNight * 0.10m, 2);
        return new SaldiBlock(
            StundenVortrag: prev?.HourSaldo ?? 0,
            StundenIst:     istHours,
            StundenBezug:   isFlex ? istHours : 0,
            StundenSaldo:   isFlex ? 0 : (curr?.HourSaldo ?? (prev?.HourSaldo ?? 0)),
            NachtVortrag:   prev?.NachtSaldo ?? 0,
            NachtIst:       nachtKomp,
            NachtBezug:     0,
            NachtSaldo:     curr?.NachtSaldo ?? Math.Round((prev?.NachtSaldo ?? 0) + nachtKomp, 2),
            FerienTageVortrag: prev?.FerienTageSaldo ?? 0,
            FerienTageAccrual: 0,
            FerienTageBezug:   0,
            FerienTageSaldo:   curr?.FerienTageSaldo ?? prev?.FerienTageSaldo ?? 0,
            FeiertagVortrag: prev?.FeiertagTageSaldo ?? 0,
            FeiertagAccrual: 0,
            FeiertagBezug:   0,
            FeiertagSaldo:   curr?.FeiertagTageSaldo ?? prev?.FeiertagTageSaldo ?? 0,
            FerienGeldVortrag: prev?.FerienGeldSaldo ?? 0,
            FerienGeldAccrual: 0,
            FerienGeldBezug:   0,
            FerienGeldSaldo:   curr?.FerienGeldSaldo ?? prev?.FerienGeldSaldo ?? 0,
            ThirteenVortrag: prev?.ThirteenthMonthAccumulated ?? 0,
            ThirteenAccrual: curr?.ThirteenthMonthMonthly ?? 0,
            ThirteenBezug:   0,
            ThirteenSaldo:   curr?.ThirteenthMonthAccumulated ?? prev?.ThirteenthMonthAccumulated ?? 0,
            FeiertagGeldAuszahlung: 0,
            AuszahlungLohn: 0,
            ShowThirteen: modelU is "MTP" or "FIX" or "FIX-M",
            ShowFerienGeld: showFerienGeld,
            ShowFeiertag: showFeiertag,
            ShowFeiertagGeld: false,
            ShowAuszahlung: false,
            IsFlex: isFlex);
    }

    /// <summary>Sucht eine Lohnpositions-Zeile im Slip nach Bezeichnung-Substring.</summary>
    private static decimal FindLohnLineBetrag(JsonElement slip, string bezeichnungContains)
    {
        if (!slip.TryGetProperty("lohnLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return 0;
        decimal sum = 0;
        foreach (var line in lines.EnumerateArray())
        {
            var bez = line.TryGetProperty("bezeichnung", out var b) ? b.GetString() : null;
            if (string.IsNullOrEmpty(bez)) continue;
            if (bez.Contains(bezeichnungContains, StringComparison.OrdinalIgnoreCase)
                && line.TryGetProperty("betrag", out var betrag)
                && betrag.ValueKind == JsonValueKind.Number)
            {
                sum += betrag.GetDecimal();
            }
        }
        return sum;
    }

    private static List<string> BuildCompanyHeader(CompanyProfile c)
    {
        var lines = new List<string>();
        var title = string.Join(" ", new[] { c.RestaurantCode, c.CompanyName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(title)) lines.Add(title!);
        if (!string.IsNullOrWhiteSpace(c.BranchName)) lines.Add(c.BranchName!);
        else if (!string.IsNullOrWhiteSpace(c.City)) lines.Add(c.City!);
        if (!string.IsNullOrWhiteSpace(c.Street)) lines.Add(c.Street!);
        var zipCity = string.Join(" ", new[] { c.ZipCode, c.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(zipCity)) lines.Add(zipCity);
        if (lines.Count == 0) lines.Add("—");
        return lines;
    }

    private static string FormatVerhaeltnis(Employment? e)
    {
        if (e == null) return "—";
        var from = e.ContractStartDate.ToString("dd.MM.yyyy");
        var to   = e.ContractEndDate.HasValue
            ? e.ContractEndDate.Value.ToString("dd.MM.yyyy")
            : "unbefristet";
        return $"{from} – {to}";
    }

    // ── Render ─────────────────────────────────────────────────────────

    private static void MetaRow(TableDescriptor t, string l1, string v1, string l2, string v2)
    {
        t.Cell().PaddingVertical(1).Text(l1).FontSize(8f).FontColor(Muted);
        t.Cell().PaddingVertical(1).Text(v1).Bold().FontSize(9f);
        t.Cell().PaddingVertical(1).Text(l2).FontSize(8f).FontColor(Muted);
        t.Cell().PaddingVertical(1).Text(v2).Bold().FontSize(9f);
    }

    private static void RenderSaldiTable(IContainer c, SaldiBlock s, string model)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(2.4f);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
            });

            void Head(string txt) =>
                t.Cell().BorderBottom(0.6f).BorderColor(Dark).Padding(2)
                    .AlignRight().Text(txt).Bold().FontSize(7.5f);
            void HeadL(string txt) =>
                t.Cell().BorderBottom(0.6f).BorderColor(Dark).Padding(2)
                    .Text(txt).Bold().FontSize(7.5f);
            void CellR(string txt, bool bold = false)
            {
                var cell = t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2).AlignRight();
                if (bold) cell.Text(txt).Bold().FontSize(8f);
                else      cell.Text(txt).FontSize(8f);
            }
            void CellL(string txt) =>
                t.Cell().BorderBottom(0.3f).BorderColor(Line).Padding(2)
                    .Text(txt).FontSize(8f);

            HeadL("");
            Head("Vortrag");
            Head("Zuwachs");
            Head("Bezug");
            Head("Saldo");

            // Stunden: FLEX → mit Lohn ausbezahlt (Bezug = Ist, Saldo 0).
            // MTP/FIX → Überstunden-Saldo (Bezug nur bei Auszahlung aus Saldo).
            if (s.IsFlex)
            {
                CellL("Stunden (effektiv)");
                CellR("—");
                CellR(Num(s.StundenIst));
                CellR(s.StundenBezug > 0 ? Num(s.StundenBezug) : "—");
                CellR(Num(s.StundenSaldo), bold: true);
            }
            else
            {
                CellL("Stunden (Überstunden)");
                CellR(Num(s.StundenVortrag));
                CellR(Num(s.StundenIst));
                CellR(s.StundenBezug > 0 ? Num(s.StundenBezug) : "—");
                CellR(Num(s.StundenSaldo), bold: true);
            }

            // Saldo = Kompensationsstunden (10 % der Nachtstunden), analog Lohnzettel.
            CellL("Nacht-Komp. (h, 10%)");
            CellR(Num(s.NachtVortrag));
            CellR(Num(s.NachtIst));
            CellR(s.NachtBezug > 0 ? Num(s.NachtBezug) : "—");
            CellR(Num(s.NachtSaldo), bold: true);

            CellL("Ferien (Tage)");
            CellR(Num(s.FerienTageVortrag));
            CellR(Num(s.FerienTageAccrual));
            CellR(s.FerienTageBezug > 0 ? Num(s.FerienTageBezug) : "—");
            CellR(Num(s.FerienTageSaldo), bold: true);

            if (s.ShowFeiertag)
            {
                CellL("Feiertag (Tage)");
                CellR(Num(s.FeiertagVortrag));
                CellR(Num(s.FeiertagAccrual));
                CellR(s.FeiertagBezug > 0 ? Num(s.FeiertagBezug) : "—");
                CellR(Num(s.FeiertagSaldo), bold: true);
            }

            // FLEX: Feiertagentschädigung wird monatlich mit dem Lohn ausbezahlt.
            if (s.ShowFeiertagGeld)
            {
                CellL("Feiertag-Geld (CHF)");
                CellR("—");
                CellR(Chf(s.FeiertagGeldAuszahlung));
                CellR(Chf(s.FeiertagGeldAuszahlung));
                CellR(Chf(0), bold: true);
            }

            if (s.ShowFerienGeld)
            {
                CellL("Ferien-Geld (CHF)");
                CellR(Chf(s.FerienGeldVortrag));
                CellR(Chf(s.FerienGeldAccrual));
                CellR(s.FerienGeldBezug > 0 ? Chf(s.FerienGeldBezug) : "—");
                CellR(Chf(s.FerienGeldSaldo), bold: true);
            }

            if (s.ShowThirteen)
            {
                CellL("13. Monatslohn (CHF)");
                CellR(Chf(s.ThirteenVortrag));
                CellR(Chf(s.ThirteenAccrual));
                CellR(s.ThirteenBezug > 0 ? Chf(s.ThirteenBezug) : "—");
                CellR(Chf(s.ThirteenSaldo), bold: true);
            }

            // Lohnauszahlung (Netto an MA) — was mit diesem Lohnlauf ausbezahlt wird.
            if (s.ShowAuszahlung)
            {
                CellL("Lohnauszahlung (CHF)");
                CellR("—");
                CellR("—");
                CellR(Chf(s.AuszahlungLohn));
                CellR("—");
            }
        });
    }

    private static void RenderDayTable(IContainer c, List<DayRow> rows, decimal totalH, decimal totalN)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.ConstantColumn(52);   // Datum
                cd.ConstantColumn(22);   // WD
                cd.RelativeColumn(3.2f); // Arbeitszeit
                cd.ConstantColumn(40);   // Total
                cd.ConstantColumn(36);   // Zeitz
                cd.RelativeColumn(1.6f); // Abw.
                cd.RelativeColumn(1.4f); // Bemerkung
            });

            void H(string txt, bool right = false)
            {
                var cell = t.Cell().Background("#f0f0f0").BorderBottom(0.6f).BorderColor(Dark).Padding(2);
                (right ? cell.AlignRight() : cell).Text(txt).Bold().FontSize(7.5f);
            }

            H("Datum");
            H("");
            H("Arbeitszeit");
            H("Total", right: true);
            H("Zeitz", right: true);
            H("Abwesenheit", right: true);   // rechtsbündig (Walter 04.08.2026)
            H("Bemerkung");

            foreach (var r in rows)
            {
                var isWeekend = r.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                void D(string txt, bool right = false, bool bold = false)
                {
                    var cell = t.Cell().BorderBottom(0.25f).BorderColor(Line).PaddingVertical(1).PaddingHorizontal(2);
                    if (isWeekend) cell = cell.Background("#fafafa");
                    var text = (right ? cell.AlignRight() : cell).Text(txt).FontSize(7.5f);
                    if (bold) text.Bold();
                    if (isWeekend) text.FontColor(Muted);
                }

                D(r.Date.ToString("dd.MM."));
                D(r.Weekday);
                D(r.Arbeitszeit);
                D(r.TotalHours > 0 ? Num(r.TotalHours) : "", right: true);
                D(r.NightHours > 0 ? Num(r.NightHours) : "", right: true);
                D(r.Abwesenheit, right: true);
                D(r.Bemerkung);
            }

            // TOTAL
            void T(string txt, bool right = false)
            {
                var cell = t.Cell().BorderTop(0.7f).BorderColor(Dark).Padding(2);
                (right ? cell.AlignRight() : cell).Text(txt).Bold().FontSize(8f);
            }
            T("TOTAL");
            T("");
            T("");
            T(Num(totalH), right: true);
            T(Num(totalN), right: true);
            T("");
            T("");
        });
    }

    // ── Slip-JSON Helfer ───────────────────────────────────────────────

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : (el.TryGetProperty(name, out p) && p.ValueKind != JsonValueKind.Null && p.ValueKind != JsonValueKind.Undefined
                ? p.ToString()
                : null);

    private static decimal? GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(p.GetString(), NumberStyles.Any, Inv, out var d) => d,
            _ => null
        };
    }
}
