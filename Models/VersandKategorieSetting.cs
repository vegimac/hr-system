namespace HrSystem.Models;

/// <summary>
/// Freigabe-Matrix für ausgehende Nachrichten (Walter-Vorgabe 01.09.2026):
/// eine Zeile pro Verteiler-Kategorie, je Kanal ein Haken.
///
/// HAKEN gesetzt  = scharf, die Nachricht geht an den echten Empfänger.
/// KEIN Haken     = Umleitung an die Test-Adresse (smtp_setting.test_redirect_to)
///                  bzw. Test-Nummer (ecall_setting.test_redirect_to).
///
/// Die Test-Adresse bleibt dauerhaft gefüllt; sie ist nur das Ziel der
/// Umleitung, nicht der Schalter. Steht sie leer und die Kategorie ist
/// NICHT scharf, wird der Versand blockiert statt scharf durchgelassen —
/// der sichere Ausgang gewinnt.
///
/// Die Kategorie-Liste selbst steht im Code
/// (<see cref="Services.VersandKategorien"/>); diese Tabelle hält nur die
/// Haken. Eine fehlende Zeile bedeutet: NICHT scharf.
/// </summary>
public class VersandKategorieSetting
{
    /// <summary>Kategorie-Code, z.B. «GRUPPEN_MAIL» — Primärschlüssel.</summary>
    public string Code { get; set; } = "";

    public bool MailScharf { get; set; }
    public bool SmsScharf  { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int? UpdatedByUserId { get; set; }
}
