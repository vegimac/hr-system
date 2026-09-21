using System.Text.Json;
using System.Text.RegularExpressions;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// QST-Jahresmodell (ESTV / KS 45 / Anhang 1): GE, FR, VD, VS, TI.
/// Satz-Lohn aus periodisch/QST-Tage + aperiodisch÷12; je Tarifcode ein Topf;
/// Steuer je Topf auf 5 Rp. Negativ = Rückerstattung. Walter 19./20.09.2026, O1.
/// </summary>
public static class QstJahresmodell
{
    public readonly record struct Ergebnis(
        decimal SatzLohn,
        decimal SatzPct,
        decimal Jahressteuer,
        decimal QstMonat);

    public readonly record struct TopfErgebnis(
        decimal SatzLohn,
        decimal Jahressteuer,
        decimal QstMonat,
        IReadOnlyDictionary<string, decimal> SteuerJeCode,
        IReadOnlyDictionary<string, decimal> SatzJeCode);

    public readonly record struct SlipZeile(
        decimal QstBezahlt,
        decimal IstBasis,
        decimal? SatzBasis,
        decimal? SatzAperiodisch = null,
        string? TarifCode = null);

    public readonly record struct YtdStand(
        decimal IstBisher,
        decimal BezahltBisher,
        int NMonate,
        decimal SatzBisher = 0,
        decimal AperiodischBisher = 0,
        int QstTageBisher = 0,
        IReadOnlyDictionary<string, decimal>? IstJeCode = null,
        decimal TageChBisher = 0,
        decimal TageEffBisher = 0);

    /// <summary>Vertragsabschnitt (Von inklusiv, Bis inklusiv oder offen).</summary>
    public readonly record struct Zeitraum(DateOnly Von, DateOnly? Bis);

    /// <summary>
    /// Vertragsabschnitte des MA über alle Filialen (ein AHV-Arbeitgeber). Rückfall
    /// auf Eintritt/Austritt am MA, wenn keine Verträge geladen sind.
    /// </summary>
    public static List<Zeitraum> Vertragszeitraeume(Employee? emp)
    {
        var list = new List<Zeitraum>();
        if (emp == null) return list;
        foreach (var e in emp.Employments ?? Enumerable.Empty<Employment>())
        {
            if (e.ContractStartDate.Year <= 1) continue;
            list.Add(new Zeitraum(
                DateOnly.FromDateTime(e.ContractStartDate),
                e.ContractEndDate is { } b && b.Year > 1 ? DateOnly.FromDateTime(b) : null));
        }
        if (list.Count == 0 && emp.EntryDate is { } ed && ed.Year > 1)
            list.Add(new Zeitraum(
                DateOnly.FromDateTime(ed),
                emp.ExitDate is { } xd && xd.Year > 1 ? DateOnly.FromDateTime(xd) : null));
        return list;
    }

    /// <summary>
    /// Frühester Vertragsbeginn, der ins Lohnjahr hineinreicht — Anhang 1 Y1.1:
    /// Austritt 31.3. + Wiedereintritt 1.6. setzen die Töpfe NICHT zurück, der
    /// spätere Eintritt darf den Modell-Start nicht nach hinten schieben (TF41).
    /// </summary>
    public static DateOnly? ErsterEintrittImJahr(IEnumerable<Zeitraum> vertraege, int jahr)
    {
        var jahrBeginn = new DateOnly(jahr, 1, 1);
        DateOnly? best = null;
        foreach (var z in vertraege)
        {
            if (z.Bis is { } b && b < jahrBeginn) continue;
            if (z.Von.Year > jahr) continue;
            if (best == null || z.Von < best.Value) best = z.Von;
        }
        return best;
    }

    /// <summary>
    /// Σ Arbeitstage CH / effektiv der Vormonate (bis exklusiv «bisMonat»). Monate mit
    /// Vertragsdeckung, aber OHNE Erfassung zählen als voll in der Schweiz (20 von 20) —
    /// erfasst werden nur Auslandmonate (Walter 21.09.2026: kein Nachtragen von CH-Monaten,
    /// TF25 Lehmann Mai). Teilmonate ohne Erfassung sind damit eine Näherung (20 statt der
    /// effektiven Tage). Monate ohne Vertrag zählen 0.
    /// </summary>
    public static (decimal Ch, decimal Eff) ArbeitstageBisher(
        IEnumerable<(int Month, decimal TageCh, decimal TageEffektiv)> erfasst,
        IEnumerable<Zeitraum> vertraege, int jahr, int bisMonatExkl)
    {
        var byMonth = erfasst.GroupBy(a => a.Month).ToDictionary(g => g.Key, g => g.First());
        decimal ch = 0, eff = 0;
        for (int m = 1; m < Math.Min(bisMonatExkl, 13); m++)
        {
            if (byMonth.TryGetValue(m, out var a)) { ch += a.TageCh; eff += a.TageEffektiv; continue; }
            if (QstTageDesMonats(jahr, m, vertraege) <= 0) continue;
            ch += 20m; eff += 20m;
        }
        return (ch, eff);
    }

    /// <summary>
    /// QST-Tage des Monats aus der Vertragsdeckung (30-Tage-Monat, Monatsende = Tag 30,
    /// überlappende Abschnitte zählen einfach). Monate ohne Vertrag = 0 (Y1.1: April/Mai).
    /// </summary>
    public static int QstTageDesMonats(int jahr, int monat, IEnumerable<Zeitraum> vertraege)
    {
        var gedeckt = new bool[31];
        foreach (var z in vertraege)
        {
            if (z.Von.Year > jahr || (z.Von.Year == jahr && z.Von.Month > monat)) continue;
            if (z.Bis is { } b && (b.Year < jahr || (b.Year == jahr && b.Month < monat))) continue;
            int von = 1, bis = 30;
            if (z.Von.Year == jahr && z.Von.Month == monat) von = Math.Min(z.Von.Day, 30);
            if (z.Bis is { } b2 && b2.Year == jahr && b2.Month == monat)
                bis = b2.Day >= DateTime.DaysInMonth(jahr, monat) ? 30 : Math.Min(b2.Day, 30);
            for (int d = von; d <= bis; d++) gedeckt[d] = true;
        }
        int n = 0;
        for (int d = 1; d <= 30; d++) if (gedeckt[d]) n++;
        return n;
    }

    private static readonly Regex CodeRx = new(@"^([A-Z]{1,2})(\d)([YN])$", RegexOptions.IgnoreCase);

    public static bool GiltFuer(string? kanton)
        => QstTarifVorschlagLogic.IstQstJahresmodell(kanton);

    public static string CodeVon(EmployeeQuellensteuer? q)
    {
        if (q == null) return "";
        if (!string.IsNullOrWhiteSpace(q.TarifCode))
            return $"{q.TarifCode}{q.AnzahlKinder}{(q.Kirchensteuer ? 'Y' : 'N')}";
        return (q.QstCode ?? "").Trim();
    }

    /// <summary>
    /// Tarifcode-Topf eines Vormonats nach heutigem Wissensstand (Anhang 1 Y40):
    /// Version, die am Monatsende gilt und bis «bekanntBis» erfasst wurde —
    /// rückwirkende Versionen inklusive. null = keine Version oder anderer Kanton
    /// (Monat gehört nicht zur Kette). Der Slip-Code ist nur Rückfall ohne Version.
    /// </summary>
    public static string? TopfCodeFuerMonat(
        IEnumerable<EmployeeQuellensteuer> versionen,
        int jahr, int monat, DateOnly bekanntBis, string kanton)
    {
        var stichtag = new DateOnly(jahr, monat, 1).AddMonths(1).AddDays(-1);
        var v = QstVersionWahl.WaehleRueckwirkend(versionen, stichtag, bekanntBis);
        if (v == null) return null;
        if (!string.Equals((v.Steuerkanton ?? "").Trim(), (kanton ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        var code = CodeVon(v);
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    public static bool TryParseCode(string? code, out string tarif, out int kinder, out bool kirche)
    {
        tarif = "";
        kinder = 0;
        kirche = false;
        var m = CodeRx.Match((code ?? "").Trim());
        if (!m.Success) return false;
        tarif = m.Groups[1].Value.ToUpperInvariant();
        kinder = int.Parse(m.Groups[2].Value);
        kirche = m.Groups[3].Value.Equals("Y", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// Erster Tag des Monats, ab dem dieser Kanton in der zusammenhängenden
    /// QST-Kette gilt — frühestens 1.1. des Lohnjahrs, frühestens Eintritt.
    /// Tarifwechsel im selben Kanton (A→C) setzt den Start NICHT zurück.
    /// </summary>
    public static DateOnly ModellStart(
        int jahr,
        DateOnly? eintritt,
        IEnumerable<EmployeeQuellensteuer> versionen,
        string kanton,
        DateOnly periodTo)
    {
        var start = new DateOnly(jahr, 1, 1);
        if (eintritt is { } e && e > start)
            start = new DateOnly(e.Year, e.Month, 1);

        var kantonAb = KantonBeginn(versionen, kanton, periodTo);
        if (kantonAb is { } kb)
        {
            var kbMonat = new DateOnly(kb.Year, kb.Month, 1);
            if (kbMonat > start) start = kbMonat;
        }
        return start;
    }

    /// <summary>Inklusive Anzahl Kalendermonate von Start bis Periodenbeginn.</summary>
    public static int AnzahlMonate(DateOnly von, DateOnly periodFrom)
    {
        int n = (periodFrom.Year - von.Year) * 12 + periodFrom.Month - von.Month + 1;
        return n < 1 ? 1 : n;
    }

    /// <summary>
    /// SV-/QST-Tage des Monats (30-Tage-Monat, Ein-/Austritt tagesgenau).
    /// Anhang 1; bei ganzen Monaten = 30.
    /// </summary>
    public static int QstTageDesMonats(int jahr, int monat, DateOnly? eintritt, DateOnly? austritt)
    {
        int von = 1, bis = 30;
        if (eintritt is { } e)
        {
            if (e.Year > jahr || (e.Year == jahr && e.Month > monat)) return 0;
            if (e.Year == jahr && e.Month == monat) von = Math.Min(e.Day, 30);
        }
        if (austritt is { } a)
        {
            if (a.Year < jahr || (a.Year == jahr && a.Month < monat)) return 0;
            if (a.Year == jahr && a.Month == monat) bis = Math.Min(a.Day, 30);
        }
        return Math.Max(0, bis - von + 1);
    }

    public static int QstTageKumuliert(
        int jahr, int vonMonat, int bisMonat, DateOnly? eintritt, DateOnly? austritt)
    {
        int s = 0;
        for (int m = vonMonat; m <= bisMonat; m++)
            s += QstTageDesMonats(jahr, m, eintritt, austritt);
        return s;
    }

    public static int QstTageKumuliertAusVertraegen(
        int jahr, int vonMonat, int bisMonat, IEnumerable<Zeitraum> vertraege)
    {
        var liste = vertraege as IList<Zeitraum> ?? vertraege.ToList();
        int s = 0;
        for (int m = vonMonat; m <= bisMonat; m++)
            s += QstTageDesMonats(jahr, m, liste);
        return s;
    }

    /// <summary>
    /// Beginn der aktuellen Kantons-Kette (ValidFrom der ältesten Version
    /// desselben Kantons ohne Unterbruch). null = keine Version.
    /// </summary>
    public static DateOnly? KantonBeginn(
        IEnumerable<EmployeeQuellensteuer> versionen,
        string kanton,
        DateOnly periodTo)
    {
        var aktuell = QstVersionWahl.Waehle(versionen, periodTo);
        if (aktuell == null) return null;
        var kt = (kanton ?? "").Trim();
        var begin = aktuell.ValidFrom;
        foreach (var v in versionen
                     .Where(v => v.ValidFrom <= aktuell.ValidFrom)
                     .OrderByDescending(v => v.ValidFrom)
                     .ThenByDescending(v => v.Id))
        {
            if (!string.Equals((v.Steuerkanton ?? "").Trim(), kt, StringComparison.OrdinalIgnoreCase))
                break;
            begin = v.ValidFrom;
        }
        return begin;
    }

    /// <summary>
    /// Satz-Lohn bei ganzen Monaten: Round05(YTD periodisch ÷ n + YTD aperiodisch ÷ 12).
    /// </summary>
    public static decimal SatzLohn(decimal ytdPeriodic, int nMonate, decimal ytdAperiodisch = 0)
        => SatzLohnAusTagen(ytdPeriodic, Math.Max(1, nMonate) * 30, ytdAperiodisch);

    /// <summary>
    /// Anhang 1: (Σ periodisch ÷ QST-Tage × 360 + Σ aperiodisch) ÷ 12.
    /// </summary>
    public static decimal SatzLohnAusTagen(decimal ytdPeriodic, int qstTage, decimal ytdAperiodisch = 0)
    {
        if (qstTage < 1) qstTage = 30;
        if (ytdPeriodic < 0) ytdPeriodic = 0;
        if (ytdAperiodisch < 0) ytdAperiodisch = 0;
        return PayrollCalculations.Round05((ytdPeriodic / qstTage * 360m + ytdAperiodisch) / 12m);
    }

    public static decimal SatzDesMonats(SlipZeile z) => z.SatzBasis ?? z.IstBasis;

    public static decimal AperiodischDesMonats(SlipZeile z) => z.SatzAperiodisch ?? 0;

    /// <summary>
    /// Ein Topf: Jahressteuer = Satz% × YTD-IST, auf 5 Rp. Monat = Jahres − bezahlt.
    /// </summary>
    public static Ergebnis Rechne(
        decimal ytdIst,
        decimal bereitsBezahlt,
        int nMonate,
        decimal satzPct,
        decimal? ytdSatzPeriodic = null,
        decimal ytdAperiodisch = 0)
    {
        if (nMonate < 1) nMonate = 1;
        if (ytdIst < 0) ytdIst = 0;
        var satzLohn = SatzLohn(ytdSatzPeriodic ?? ytdIst, nMonate, ytdAperiodisch);
        var t = RechneToepfe(satzLohn, new Dictionary<string, decimal> { ["_"] = ytdIst }, _ => satzPct, bereitsBezahlt);
        return new Ergebnis(satzLohn, satzPct, t.Jahressteuer, t.QstMonat);
    }

    /// <summary>
    /// Anhang Y15/Y23: je Code Steuer kumuliert = Satz(Code, Satz-Lohn) × Topf (5 Rp.).
    /// Monatsabzug = Σ Töpfe − bereits bezahlt (nicht extra runden).
    /// </summary>
    public static TopfErgebnis RechneToepfe(
        decimal satzLohn,
        IReadOnlyDictionary<string, decimal> istJeCode,
        Func<string, decimal> satzPctFuerCode,
        decimal bereitsBezahlt)
    {
        var steuer = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var saetze = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal jahres = 0;
        foreach (var kv in istJeCode.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value == 0 || string.IsNullOrWhiteSpace(kv.Key)) continue;
            var pct = satzPctFuerCode(kv.Key);
            saetze[kv.Key] = pct;
            var topf = PayrollCalculations.Round05(kv.Value * pct / 100m);
            steuer[kv.Key] = topf;
            jahres += topf;
        }
        // Monatsabzug nicht runden — sonst laufen die Töpfe gegen Swissdec (Walter 20.09.2026).
        var monat = jahres - bereitsBezahlt;
        return new TopfErgebnis(satzLohn, jahres, monat, steuer, saetze);
    }

    /// <summary>
    /// QST-Zeile aus SlipJson. QstBezahlt vorzeichenbehaftet
    /// (positiv = Abzug, negativ = Rückerstattung). IST = Bemessung der Zeile.
    /// </summary>
    public static SlipZeile LeseSlip(string? slipJson)
    {
        if (string.IsNullOrWhiteSpace(slipJson))
            return new SlipZeile(0, 0, null);
        try
        {
            using var doc = JsonDocument.Parse(slipJson);
            decimal brutto = 0;
            if (doc.RootElement.TryGetProperty("totalLohn", out var b)
                && b.ValueKind == JsonValueKind.Number)
                brutto = b.GetDecimal();

            if (doc.RootElement.TryGetProperty("abzugLines", out var lines)
                && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (!line.TryGetProperty("categoryCode", out var cc)
                        || cc.ValueKind != JsonValueKind.String
                        || cc.GetString() != "QST")
                        continue;
                    decimal betrag = line.TryGetProperty("betrag", out var be)
                        && be.ValueKind == JsonValueKind.Number
                        ? be.GetDecimal() : 0;
                    decimal basis = line.TryGetProperty("basis", out var ba)
                        && ba.ValueKind == JsonValueKind.Number
                        ? ba.GetDecimal() : brutto;
                    decimal? satzBasis = line.TryGetProperty("satzBasis", out var sb)
                        && sb.ValueKind == JsonValueKind.Number
                        ? sb.GetDecimal() : null;
                    decimal? satzAper = line.TryGetProperty("satzAperiodisch", out var sa)
                        && sa.ValueKind == JsonValueKind.Number
                        ? sa.GetDecimal() : null;
                    string? tarif = null;
                    if (line.TryGetProperty("qstCode", out var qc) && qc.ValueKind == JsonValueKind.String)
                        tarif = qc.GetString();
                    if (string.IsNullOrWhiteSpace(tarif)
                        && line.TryGetProperty("bezeichnung", out var bez)
                        && bez.ValueKind == JsonValueKind.String)
                    {
                        var tm = Regex.Match(bez.GetString() ?? "", @"Quellensteuer\s+([A-Za-z]{1,2}\d[YNyn])");
                        if (tm.Success) tarif = tm.Groups[1].Value.ToUpperInvariant();
                    }
                    return new SlipZeile(-betrag, basis, satzBasis, satzAper, tarif);
                }
            }
            return new SlipZeile(0, brutto, null);
        }
        catch (JsonException)
        {
            return new SlipZeile(0, 0, null);
        }
    }
}
