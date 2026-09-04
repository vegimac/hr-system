using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// QST-Kantonswechsel bei Umzug (Walter 04.09.2026, «Umzug läuft noch nicht
/// sauber»): Kommt aus easy@work eine neue Adresse in einem anderen Kanton,
/// wird die laufende QST-Version SOFORT per Monatsende beendet und eine neue
/// mit dem neuen Kanton ab dem 1. des Folgemonats angelegt (KS 45: der
/// angebrochene Monat zahlt im alten Kanton; Umzug am 1. → ab diesem Tag).
/// Umzugsdatum = Sync-Tag als Annahme; bestätigt Walter später ein anderes
/// Datum, verschiebt <see cref="VerschiebenAsync"/> die Schnittstelle —
/// solange kein definitiv abgeschlossener Lohn die Versionen verwendet hat.
/// Gleiche Logik für den manuellen Weg (EmployeeWohnortController).
/// Wirft nicht; arbeitet auf dem übergebenen Kontext OHNE SaveChanges.
/// </summary>
public class QstKantonswechselService
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public QstKantonswechselService(AppDbContext db, LohnEditLockService editLock) { _db = db; _editLock = editLock; }

    public sealed record Ergebnis(bool Kantonswechsel, bool Angelegt, bool Verschoben, bool Gesperrt, string Info,
                                  DateOnly? NeuAb = null, string? AlterKanton = null, string? NeuerKanton = null);

    public static DateOnly FolgeMonatErster(DateOnly umzug)
        => umzug.Day == 1 ? umzug : new DateOnly(umzug.Year, umzug.Month, 1).AddMonths(1);

    private async Task<DateOnly?> FirstAllowedAsync(int employeeId)
    {
        var branchId = await _db.Employees.Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments).Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId).FirstOrDefaultAsync();
        return branchId.HasValue ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value) : null;
    }

    /// <summary>
    /// Laufende QST-Version per Monatsende beenden + Folge-Version mit neuem
    /// Kanton anlegen. Idempotent: existiert die Folge-Version schon, wird sie
    /// bei abweichendem Umzugsdatum verschoben (<see cref="VerschiebenAsync"/>).
    /// </summary>
    public async Task<Ergebnis> SplitAsync(int employeeId, DateOnly umzug, string neuerKanton, string? neuerOrt, string quelle)
    {
        neuerKanton = (neuerKanton ?? "").Trim().ToUpperInvariant();
        if (neuerKanton.Length == 0) return new Ergebnis(false, false, false, false, "Neuer Kanton unbekannt.");
        var folge = FolgeMonatErster(umzug);

        // Schon versioniert? → nur ggf. verschieben.
        var vorhanden = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.Steuerkanton == neuerKanton && q.ValidFrom >= umzug.AddMonths(-2))
            .OrderByDescending(q => q.ValidFrom).FirstOrDefaultAsync();
        if (vorhanden != null)
            return await VerschiebenAsync(employeeId, vorhanden, folge, neuerKanton);

        var alt = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidFrom < folge && (q.ValidTo == null || q.ValidTo >= umzug))
            .OrderByDescending(q => q.ValidFrom).FirstOrDefaultAsync();
        var alterKanton = (alt?.Steuerkanton ?? "").Trim().ToUpperInvariant();
        if (alt == null)
            return new Ergebnis(alterKanton != neuerKanton, false, false, false, "Kantonswechsel ohne aktive QST-Erfassung — keine QST-Version angelegt.", folge, alterKanton, neuerKanton);
        if (alterKanton == neuerKanton)
            return new Ergebnis(false, false, false, false, $"QST-Version läuft bereits im Kanton {neuerKanton}.", folge, alterKanton, neuerKanton);

        var firstAllowed = await FirstAllowedAsync(employeeId);
        if (firstAllowed.HasValue && folge < firstAllowed.Value)
            return new Ergebnis(true, false, false, true,
                $"QST-Kantonswechsel ab {folge:dd.MM.yyyy} liegt in einer verarbeiteten Lohnperiode (frei ab {firstAllowed:dd.MM.yyyy}) — bitte über eine QST-Korrektur lösen.",
                folge, alterKanton, neuerKanton);

        var monatsende = folge.AddDays(-1);
        alt.ValidTo = monatsende;
        alt.UpdatedAt = DateTime.Now;
        _db.EmployeeQuellensteuer.Add(new EmployeeQuellensteuer
        {
            EmployeeId = employeeId,
            ValidFrom = folge,
            ValidTo = null,
            Steuerkanton = neuerKanton,
            SteuerkantonName = KantonName(neuerKanton),
            QstGemeinde = neuerOrt,
            QstGemeindeBfsNr = null,
            TarifvorschlagQst = alt.TarifvorschlagQst,
            TarifCode = alt.TarifCode,
            TarifBezeichnung = alt.TarifBezeichnung,
            AnzahlKinder = alt.AnzahlKinder,
            Kirchensteuer = alt.Kirchensteuer,
            QstCode = alt.QstCode,
            SpezielBewilligt = alt.SpezielBewilligt,
            Kategorie = alt.Kategorie,
            Prozentsatz = alt.Prozentsatz,
            MindestlohnSatzbestimmung = alt.MindestlohnSatzbestimmung,
            PartnerEmployeeId = alt.PartnerEmployeeId,
            PartnerEinkommenVon = alt.PartnerEinkommenVon,
            PartnerEinkommenBis = alt.PartnerEinkommenBis,
            ArbeitsortKanton = alt.ArbeitsortKanton,
            WeitereBeschaftigungen = alt.WeitereBeschaftigungen,
            GesamtpensumWeitereAg = alt.GesamtpensumWeitereAg,
            GesamteinkommenWeitereAg = alt.GesamteinkommenWeitereAg,
            Halbfamilie = alt.Halbfamilie,
            WohnsitzAusland = alt.WohnsitzAusland,
            Wohnsitzstaat = alt.Wohnsitzstaat,
            AdresseAusland = alt.AdresseAusland,
            LivesInKonkubinat = alt.LivesInKonkubinat,
            HasJointParentalCare = alt.HasJointParentalCare,
            PaysAlimonyAdultChildren = alt.PaysAlimonyAdultChildren,
            HasHigherIncomeThanPartner = alt.HasHigherIncomeThanPartner,
            IsGrenzgaenger = alt.IsGrenzgaenger,
            IsWochenaufenthalter = alt.IsWochenaufenthalter,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        });
        return new Ergebnis(true, true, false, false,
            $"QST: bis {monatsende:dd.MM.yyyy} Kanton {alterKanton}, ab {folge:dd.MM.yyyy} Kanton {neuerKanton} (Tarif {alt.QstCode ?? alt.TarifCode} übernommen — bitte prüfen; {quelle}).",
            folge, alterKanton, neuerKanton);
    }

    /// <summary>
    /// Bestätigtes Umzugsdatum weicht von der Annahme ab: Schnittstelle
    /// zwischen Vorgänger (ValidTo) und Folge-Version (ValidFrom) verschieben.
    /// </summary>
    public async Task<Ergebnis> VerschiebenAsync(int employeeId, EmployeeQuellensteuer neu, DateOnly folge, string neuerKanton)
    {
        if (neu.ValidFrom == folge)
            return new Ergebnis(true, false, false, false, $"QST-Kantonswechsel war bereits erfasst ({neuerKanton} ab {folge:dd.MM.yyyy}).", folge, null, neuerKanton);
        var vorgaenger = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.Id != neu.Id && q.ValidTo == neu.ValidFrom.AddDays(-1))
            .OrderByDescending(q => q.ValidFrom).FirstOrDefaultAsync();
        if (vorgaenger == null || vorgaenger.ValidFrom >= folge)
            return new Ergebnis(true, false, false, false, $"QST-Version {neuerKanton} ab {neu.ValidFrom:dd.MM.yyyy} besteht — Vorgänger nicht eindeutig, Datum nicht verschoben.", neu.ValidFrom, null, neuerKanton);

        // Sperre: verarbeitete Lohnperioden im betroffenen Bereich
        var firstAllowed = await FirstAllowedAsync(employeeId);
        var fruehester = folge < neu.ValidFrom ? folge : neu.ValidFrom;
        if (firstAllowed.HasValue && fruehester < firstAllowed.Value)
            return new Ergebnis(true, false, false, true,
                $"Umzugsdatum kann nicht mehr verschoben werden — der Bereich ab {fruehester:dd.MM.yyyy} liegt in einer verarbeiteten Lohnperiode (frei ab {firstAllowed:dd.MM.yyyy}).",
                neu.ValidFrom, null, neuerKanton);

        vorgaenger.ValidTo = folge.AddDays(-1);
        vorgaenger.UpdatedAt = DateTime.Now;
        neu.ValidFrom = folge;
        neu.UpdatedAt = DateTime.Now;
        return new Ergebnis(true, false, true, false,
            $"QST-Kantonswechsel auf das bestätigte Datum verschoben: {vorgaenger.Steuerkanton} bis {vorgaenger.ValidTo:dd.MM.yyyy}, {neuerKanton} ab {folge:dd.MM.yyyy}.",
            folge, vorgaenger.Steuerkanton, neuerKanton);
    }

    public static string? KantonName(string code) => code switch
    {
        "AG" => "Aargau", "AI" => "Appenzell Innerrhoden", "AR" => "Appenzell Ausserrhoden",
        "BE" => "Bern", "BL" => "Basel-Landschaft", "BS" => "Basel-Stadt", "FR" => "Freiburg",
        "GE" => "Genf", "GL" => "Glarus", "GR" => "Graubünden", "JU" => "Jura",
        "LU" => "Luzern", "NE" => "Neuenburg", "NW" => "Nidwalden", "OW" => "Obwalden",
        "SG" => "St. Gallen", "SH" => "Schaffhausen", "SO" => "Solothurn", "SZ" => "Schwyz",
        "TG" => "Thurgau", "TI" => "Tessin", "UR" => "Uri", "VD" => "Waadt",
        "VS" => "Wallis", "ZG" => "Zug", "ZH" => "Zürich",
        _ => null,
    };
}
