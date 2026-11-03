using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Adresse des Ehepartners an einem Stichtag (Walter 03.11.2026). Der Verlauf liegt in den
/// Zusatzadressen des Mitarbeiters vom Typ «Adresse Ehepartner» mit Gültig-ab — wie die
/// Wohnort-Historie des Mitarbeiters: ein Umzug ist eine NEUE Adresse ab dem Umzugsdatum,
/// nicht das Überschreiben der alten (Quality Tool Juni 2025: TF36 Maldini, Partnerin bis
/// August in Bern, ab September in Como). Rein, ohne DB.
/// </summary>
public static class PartnerAdresse
{
    public const string Typ = "Adresse Ehepartner";

    /// <summary>
    /// Gültige Partner-Adresse am Stichtag: jüngste mit Gültig-ab ≤ Stichtag (leer = seit jeher).
    /// Liegt der Stichtag vor allen datierten Einträgen, gilt der älteste. Ohne Verlauf die
    /// verknüpfte Adresse des Familienmitglieds. Null = lebt im Haushalt bzw. keine Adresse.
    /// </summary>
    public static EmployeeAddress? Am(EmployeeFamilyMember? partner, IEnumerable<EmployeeAddress> adressen, DateOnly stichtag)
    {
        // Im Haushalt ohne eigene verknüpfte Adresse = Adresse des Mitarbeiters (LebtImHaushalt ist
        // standardmässig true, darum zählt erst die fehlende Verknüpfung).
        if (partner == null || (partner.LebtImHaushalt && partner.AlternativeAddressId == null)) return null;
        return AmVerlauf(partner.EmployeeId, adressen, stichtag)
            ?? (partner.AlternativeAddressId is int id ? adressen.FirstOrDefault(a => a.Id == id) : null);
    }

    /// <summary>Nur der Verlauf «Adresse Ehepartner» dieses Mitarbeiters am Stichtag (null = kein Verlauf).</summary>
    public static EmployeeAddress? AmVerlauf(int employeeId, IEnumerable<EmployeeAddress> adressen, DateOnly stichtag)
    {
        var verlauf = adressen
            .Where(a => a.EmployeeId == employeeId && a.AddressType == Typ)
            .OrderBy(a => a.ValidFrom == null ? 0 : 1).ThenBy(a => a.ValidFrom).ThenBy(a => a.Id)
            .ToList();
        if (verlauf.Count == 0) return null;
        return verlauf.LastOrDefault(a => a.ValidFrom == null || a.ValidFrom <= stichtag) ?? verlauf[0];
    }

    /// <summary>Gleiche Adresse (Strasse, PLZ, Ort) — dann kein neuer Verlaufseintrag.</summary>
    public static bool Gleich(EmployeeAddress a, string? strasse, string? plz, string? ort)
        => string.Equals((a.Street ?? "").Trim(), (strasse ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals((a.ZipCode ?? "").Trim(), (plz ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals((a.City ?? "").Trim(), (ort ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
