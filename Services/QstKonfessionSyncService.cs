using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 01.08.2026: Ändert sich die Konfession am MA, muss der
/// aktuelle offene QST-Eintrag die Kirchensteuer nachziehen
/// (C2N ↔ C2Y). Tarifbuchstabe und Kinderzahl bleiben unverändert.
/// </summary>
public class QstKonfessionSyncService
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    private readonly ILogger<QstKonfessionSyncService> _log;

    public QstKonfessionSyncService(
        AppDbContext db,
        LohnEditLockService editLock,
        ILogger<QstKonfessionSyncService> log)
    {
        _db       = db;
        _editLock = editLock;
        _log      = log;
    }

    public sealed record SyncResult(
        bool   Changed,
        string Action,
        string? QstCode,
        bool   Kirchensteuer);

    /// <summary>
    /// Passt den offenen QST-Eintrag an die neue Konfession an.
    /// Kein Eintrag / bereits passend → no-op.
    /// Nicht im Lohn verwendet → in-place Update.
    /// Im Lohn verwendet → neue Version ab FirstAllowed/heute.
    /// </summary>
    public async Task<SyncResult?> SyncAsync(int employeeId, string? newReligion)
    {
        var open = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidTo == null)
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        if (open is null) return null;

        // Walter-Vorgabe 30.08.2026: Y-fähige Konfession ist nur die halbe
        // Miete — der Kanton hat das letzte Wort. In GE/NE/VD/VS/TI wird die
        // Kirchensteuer nicht über die Quellensteuer erhoben, dort bleibt es
        // bei N, auch bei röm.-kath. oder israelitischer Konfession. (Die
        // zusätzliche Prüfung auf Y-Tarife in der ESTV-Datei macht die
        // Vorschlagslogik; hier fehlt die Tarifdatei, darum null = nicht
        // blockend.)
        var wantKirche = QstTarifVorschlagLogic.IstKirchensteuerPflichtig(newReligion)
                      && QstTarifVorschlagLogic.KirchensteuerImKantonMoeglich(open.Steuerkanton, null);
        var newCode    = RebuildQstCode(open.TarifCode, open.AnzahlKinder, wantKirche, open.QstCode);

        if (open.Kirchensteuer == wantKirche
            && string.Equals(open.QstCode, newCode, StringComparison.OrdinalIgnoreCase))
        {
            return new SyncResult(false, "unchanged", open.QstCode, open.Kirchensteuer);
        }

        var branchId = await _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

        DateOnly? firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateForContractsAsync(branchId.Value)
            : null;

        var inLohn = firstAllowed.HasValue && open.ValidFrom < firstAllowed.Value;
        // Unspecified: Spalte ist timestamp without time zone (kein Kind-Zwang).
        var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        if (!inLohn)
        {
            open.Kirchensteuer = wantKirche;
            open.QstCode       = newCode;
            open.UpdatedAt     = now;
            await _db.SaveChangesAsync();
            return new SyncResult(true, "updated", newCode, wantKirche);
        }

        // Soft-Lock: gesperrte Zeile nicht mutieren — neue Version.
        var validFrom = DateOnly.FromDateTime(DateTime.Today);
        if (firstAllowed.HasValue && validFrom < firstAllowed.Value)
            validFrom = firstAllowed.Value;

        // Falls ValidFrom nicht nach dem alten Start liegt, in-place
        // (gleiche Gültigkeit) — sonst bliebe ValidTo vor ValidFrom.
        if (validFrom <= open.ValidFrom)
        {
            open.Kirchensteuer = wantKirche;
            open.QstCode       = newCode;
            open.UpdatedAt     = now;
            await _db.SaveChangesAsync();
            return new SyncResult(true, "updated", newCode, wantKirche);
        }

        open.ValidTo   = validFrom.AddDays(-1);
        open.UpdatedAt = now;

        var clone = CloneForVersion(open, employeeId, validFrom, wantKirche, newCode, now);
        _db.EmployeeQuellensteuer.Add(clone);
        await _db.SaveChangesAsync();
        return new SyncResult(true, "versioned", newCode, wantKirche);
    }

    /// <summary>
    /// Best-effort Variante: Fehler werden geloggt, nie nach oben geworfen —
    /// der MA-Stammdaten-Save darf wegen QST-Nachzug nicht scheitern.
    /// </summary>
    public async Task<SyncResult?> TrySyncAsync(int employeeId, string? newReligion)
    {
        try
        {
            return await SyncAsync(employeeId, newReligion);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "QST-Kirchensteuer-Nachzug für MA {EmployeeId} fehlgeschlagen (Konfession «{Religion}»).",
                employeeId, newReligion ?? "");
            return null;
        }
    }

    /// <summary>
    /// Baut {Tarif}{Kinder}{Y|N}. Tarif = ein Buchstabe (A/B/C/H/…).
    /// </summary>
    public static string RebuildQstCode(
        string? tarifCode, int anzahlKinder, bool kirchensteuer, string? previousQstCode)
    {
        string tarif;
        if (!string.IsNullOrWhiteSpace(tarifCode))
            tarif = tarifCode.Trim().ToUpperInvariant();
        else if (!string.IsNullOrWhiteSpace(previousQstCode))
            tarif = previousQstCode.Trim().ToUpperInvariant();
        else
            tarif = "A";

        // Nur der Tarifbuchstabe — falls irgendwo der volle Code (C2N) in
        // tarif_code liegt, sonst würde «C2N2Y» entstehen.
        tarif = new string(tarif.TakeWhile(char.IsLetter).ToArray());
        if (string.IsNullOrEmpty(tarif))
            tarif = "A";
        else
            tarif = tarif[..1];

        var kinder = Math.Max(0, anzahlKinder);
        return $"{tarif}{kinder}{(kirchensteuer ? "Y" : "N")}";
    }

    private static EmployeeQuellensteuer CloneForVersion(
        EmployeeQuellensteuer src,
        int employeeId,
        DateOnly validFrom,
        bool kirchensteuer,
        string qstCode,
        DateTime now)
        => new()
        {
            EmployeeId                   = employeeId,
            ValidFrom                    = validFrom,
            ValidTo                      = null,
            Steuerkanton                 = src.Steuerkanton,
            SteuerkantonName             = src.SteuerkantonName,
            QstGemeinde                  = src.QstGemeinde,
            QstGemeindeBfsNr             = src.QstGemeindeBfsNr,
            TarifvorschlagQst            = src.TarifvorschlagQst,
            TarifCode                    = src.TarifCode,
            TarifBezeichnung             = src.TarifBezeichnung,
            AnzahlKinder                 = src.AnzahlKinder,
            Kirchensteuer                = kirchensteuer,
            QstCode                      = qstCode,
            SpezielBewilligt             = src.SpezielBewilligt,
            Kategorie                    = src.Kategorie,
            Prozentsatz                  = src.Prozentsatz,
            MindestlohnSatzbestimmung    = src.MindestlohnSatzbestimmung,
            PartnerEmployeeId            = src.PartnerEmployeeId,
            PartnerEinkommenVon          = src.PartnerEinkommenVon,
            PartnerEinkommenBis          = src.PartnerEinkommenBis,
            ArbeitsortKanton             = src.ArbeitsortKanton,
            WeitereBeschaftigungen       = src.WeitereBeschaftigungen,
            GesamtpensumWeitereAg        = src.GesamtpensumWeitereAg,
            GesamteinkommenWeitereAg     = src.GesamteinkommenWeitereAg,
            Halbfamilie                  = src.Halbfamilie,
            WohnsitzAusland              = src.WohnsitzAusland,
            Wohnsitzstaat                = src.Wohnsitzstaat,
            AdresseAusland               = src.AdresseAusland,
            LivesInKonkubinat            = src.LivesInKonkubinat,
            HasJointParentalCare         = src.HasJointParentalCare,
            PaysAlimonyAdultChildren     = src.PaysAlimonyAdultChildren,
            HasHigherIncomeThanPartner   = src.HasHigherIncomeThanPartner,
            IsGrenzgaenger               = src.IsGrenzgaenger,
            IsWochenaufenthalter         = src.IsWochenaufenthalter,
            CreatedAt                    = now,
            UpdatedAt                    = now
        };
}
