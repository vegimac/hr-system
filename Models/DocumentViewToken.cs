namespace HrSystem.Models;

/// <summary>
/// Kurzlebiger Link auf ein Dokument, damit die Vorschau in einem NEUEN TAB
/// über eine echte Server-URL läuft statt über eine Blob-URL
/// (Walter-Vorgabe 30.08.2026, Fall Treuhänder).
///
/// Warum überhaupt: ein normaler Browser-Tab kann keinen Bearer-Header
/// mitschicken, darum wurde das PDF bisher im JavaScript geholt und als Blob
/// angezeigt. Eine Blob-URL lebt aber nur, solange die erzeugende Seite sie
/// nicht freigibt — und beim Speichern lädt der Browser sie ein zweites Mal.
/// Mit diesem Token kommt die Datei direkt vom Server: richtiger Dateiname aus
/// Content-Disposition, beliebig oft speicherbar, unabhängig vom Tab.
///
/// Wie die anderen Share-Tokens: Klartext nur im Link, in der DB der SHA-256.
/// Kurze Gültigkeit (Minuten), an ein einzelnes Dokument gebunden.
/// </summary>
public class DocumentViewToken
{
    public int Id { get; set; }

    public int DokumentId { get; set; }

    /// <summary>SHA-256 des Tokens — der Klartext steht nur im Link.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>true = Office-Dokument serverseitig nach PDF wandeln.</summary>
    public bool AsPdf { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    /// <summary>Erster Abruf — rein informativ fürs Audit.</summary>
    public DateTime? OpenedAt { get; set; }
}
