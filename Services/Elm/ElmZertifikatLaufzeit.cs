namespace HrSystem.Services.Elm;

/// <summary>
/// Laufzeit des SUA-Zertifikats: anzeigen, warnen, erneuern (Walter 15.10.2026, Swissdec-Zertifizierung:
/// «als Benutzer, nicht als Systemtechniker»). Regeln aus ELM 6.0 TransmitterRequirements Anhang C:
/// C.2.3 — der Transmitter erneuert kurz vor Ablauf automatisch; abgelaufen ⇒ keine Erneuerung mehr,
/// neue Registrierung. C.3.1 — Erneuern frühestens 60 Tage vor Ablauf, höchstens 3× hintereinander.
///
/// Die Schwellen für Warnung, Automatik und Rot richten sich nach der Laufzeit: produktiv gilt ein
/// Zertifikat 1 Jahr (60 / 30 / 14 Tage), das RefApps-Testzertifikat nur rund 10 Tage — dort wären
/// feste 60 Tage «immer rot».
/// </summary>
public static class ElmZertifikatLaufzeit
{
    public const int FensterTage = 60;
    public const int MaxErneuerungen = 3;
    private const double WarnTageMax = 60, AutoTageMax = 30, RotTageMax = 14;

    public const string Ok = "ok", Warn = "warn", Rot = "rot";

    /// <summary>Was der Knopf «Zertifikat erneuern» tun wird.</summary>
    public const string AktionKeine = "keine", AktionErneuern = "erneuern", AktionNeuRegistrieren = "neu_registrieren";

    public record Ergebnis(
        string Stufe,
        string Aktion,
        string Text,
        DateTime? GueltigBis,
        int? RestTage,
        DateTime? ErneuernAb,
        int Erneuerungen,
        bool ErneuernErlaubt,
        bool AutoFaellig);

    public static Ergebnis Beurteile(DateTime? notBefore, DateTime? notAfter, bool mitSchluessel,
        int erneuerungen, DateTime jetzt)
    {
        if (notAfter is not DateTime bis)
            return new(Rot, AktionNeuRegistrieren,
                "Kein SUA-Zertifikat vorhanden. Ohne kann OneCrew keine Meldungen im Namen der Firma unterschreiben.",
                null, null, null, erneuerungen, false, false);

        var gesamt = notBefore is DateTime von && von < bis ? (bis - von).TotalDays : 365;
        var warnTage = Math.Min(WarnTageMax, gesamt / 2);
        var autoTage = Math.Min(AutoTageMax, gesamt * 0.2);
        var rotTage = Math.Min(RotTageMax, gesamt * 0.1);
        var rest = bis - jetzt;
        var restTage = (int)Math.Floor(rest.TotalDays);
        var erneuernAb = bis.AddDays(-FensterTage);
        var bisText = bis.ToString("dd.MM.yyyy");

        if (rest <= TimeSpan.Zero)
            return new(Rot, AktionNeuRegistrieren,
                $"Abgelaufen am {bisText}. Erneuern geht nicht mehr — die Firma muss sich neu registrieren; "
                + "Swissdec schickt dafür ein neues Registrierungspasswort per Brief.",
                bis, restTage, erneuernAb, erneuerungen, false, false);

        var restText = restTage >= 1 ? $"noch {restTage} Tag{(restTage == 1 ? "" : "e")}" : $"noch {Math.Max(0, (int)rest.TotalHours)} Stunden";
        var stufe = rest.TotalDays <= rotTage ? Rot : rest.TotalDays <= warnTage ? Warn : Ok;
        var imFenster = jetzt >= erneuernAb;

        if (!mitSchluessel)
            return new(Rot, AktionNeuRegistrieren,
                $"Gültig bis {bisText}, aber der private Schlüssel fehlt — damit lässt es sich weder verwenden "
                + "noch erneuern. Neu registrieren.",
                bis, restTage, erneuernAb, erneuerungen, false, false);

        if (erneuerungen >= MaxErneuerungen)
            return new(stufe, stufe == Ok ? AktionKeine : AktionNeuRegistrieren,
                $"Gültig bis {bisText} ({restText}). Es wurde schon {MaxErneuerungen}× erneuert — "
                + "das nächste Zertifikat gibt es nur über eine neue Registrierung.",
                bis, restTage, erneuernAb, erneuerungen, false, false);

        var autoFaellig = imFenster && rest.TotalDays <= autoTage;
        var text = stufe == Ok
            ? $"Gültig bis {bisText} ({restText})."
            : $"Läuft am {bisText} ab ({restText}) — jetzt erneuern.";
        if (!imFenster)
            text += $" Erneuern ist ab {erneuernAb:dd.MM.yyyy} möglich.";
        return new(stufe, imFenster ? AktionErneuern : AktionKeine, text,
            bis, restTage, erneuernAb, erneuerungen, imFenster, autoFaellig);
    }

    /// <summary>Grund, warum OneCrew eine Erneuerung NICHT schickt — NULL = erlaubt.</summary>
    public static string? ErneuernGesperrt(Ergebnis e)
    {
        if (e.ErneuernErlaubt) return null;
        if (e.GueltigBis is DateTime bis && e.RestTage >= 0 && e.ErneuernAb is DateTime ab
            && e.Erneuerungen < MaxErneuerungen && e.Aktion == AktionKeine)
            return $"Erneuern ist erst ab {ab:dd.MM.yyyy} möglich (60 Tage vor Ablauf am {bis:dd.MM.yyyy}).";
        return e.Text;
    }
}
