namespace HrSystem.Services.LohnFormelwerk;

/// <summary>Metadaten einer Lohnformel — Registry und Entwicklung-UI teilen dieselbe Liste.</summary>
public sealed record LohnFormelDefinition(
    string Id,
    string Titel,
    string FormelText,
    string ParameterQuellen,
    string CodeAnker,
    string? SchemaArt = null,
    string? SwissdecCode = null
);

/// <summary>Ergebnis einer reinen Formelberechnung.</summary>
public readonly record struct LohnFormelErgebnis(decimal Betrag, string? Hinweis = null);
