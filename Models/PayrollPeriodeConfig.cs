// Walter-Vorgabe 20.05.2026: Periodenregel-Konfiguration entfernt.
// Die Lohnperiode ist jetzt IMMER der Kalendermonat (1.–letzter Tag) —
// gesetzliche Berechnungen (QST, ALV, AHV) laufen ohnehin kalendermonatlich,
// und der Akonto-Lauf deckt die Zahlung vor Monatsende ab. Die frühere
// flexible Periode (Starttag 21/1) + Übergangs-Lohnläufe sind ersatzlos weg.
//
// Diese Datei ist absichtlich leer (kein Typ mehr) — die Tabelle
// payroll_periode_config wird per Migration gedroppt. Datei kann bei
// Gelegenheit ganz aus dem Projekt entfernt werden.
