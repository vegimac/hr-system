namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Liest aus easy@work-Custom-Fields die Schwangerschafts-Daten
/// (Walter-Vorgabe 27.07.2026):
///   • Feld «Schwanger» (Key enthält schwanger/pregnant/…) mit Wert Ja
///   • <c>from</c> = gemeldet am
///   • <c>to</c>   = errechneter Geburtstermin
/// Schwangerschaftsbeginn = ET − 280 Tage (siehe <c>PregnancyFristCalculator</c>).
/// </summary>
public static class EasyAtWorkPregnancyMapper
{
    public static (DateOnly? Meldedatum, DateOnly? ErrechneterTermin) PickDates(
        IEnumerable<EawProperty> props)
    {
        static bool IsPregnantKey(string? key)
        {
            var k = (key ?? "").ToLowerInvariant();
            if (k.Contains("night_work")) return false;
            return k.Contains("schwanger")
                || k.Contains("pregnant")
                || k.Contains("pregnancy")
                || k.Contains("maternity")
                || k.Contains("mutterschaft");
        }
        static bool PropJa(EawProperty p)
        {
            var v = (p.Value ?? "").Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "ja";
        }
        static DateOnly? ParseEt(string? toRaw)
        {
            if (string.IsNullOrWhiteSpace(toRaw)) return null;
            // Primär Kalendertag Zürich; Fallback exklusives Mitternacht → Vortag
            // (easy speichert «to» inkonsistent — Walter 26.07.2026).
            return EawDateUtil.ParseSwissDate(toRaw)
                ?? EawDateUtil.ParseSwissInclusiveEndDate(toRaw);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var preg = props
            .Where(p => IsPregnantKey(p.Key))
            .Where(PropJa)
            .Where(p => ParseEt(p.ToRaw).HasValue) // ET Pflicht
            .OrderByDescending(p =>
            {
                var et = ParseEt(p.ToRaw)!.Value;
                var from = p.From ?? DateOnly.MinValue;
                // Heute gültig (gemeldet ≤ heute ≤ ET) zuerst, sonst jüngstes Von.
                return from <= today && et >= today ? 1 : 0;
            })
            .ThenByDescending(p => p.From ?? DateOnly.MinValue)
            .FirstOrDefault();
        if (preg is null) return (null, null);
        return (preg.From, ParseEt(preg.ToRaw));
    }
}
