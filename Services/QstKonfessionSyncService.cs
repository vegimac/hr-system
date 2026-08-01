using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

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

    public QstKonfessionSyncService(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
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

        var wantKirche = QstTarifVorschlagLogic.IstKirchensteuerPflichtig(newReligion);
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
        var now    = DateTime.Now;

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
    /// Baut {Tarif}{Kinder}{Y|N}. Tarif bevorzugt aus TarifCode, sonst
    /// aus dem bisherigen QstCode (erstes Zeichen).
    /// </summary>
    public static string RebuildQstCode(
        string? tarifCode, int anzahlKinder, bool kirchensteuer, string? previousQstCode)
    {
        var tarif = !string.IsNullOrWhiteSpace(tarifCode)
            ? tarifCode.Trim().ToUpperInvariant()
            : (!string.IsNullOrWhiteSpace(previousQstCode)
                ? previousQstCode.Trim()[..1].ToUpperInvariant()
                : "A");
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
