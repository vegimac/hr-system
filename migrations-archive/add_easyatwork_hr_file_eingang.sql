-- Eingang aus easy@work (Walter 08.09.2026): MA-Uploads aus der easy@work-App
-- («an HR senden») werden NIE direkt ins Dossier gelegt, sondern ins HR-Postfach.
-- Diese Tabelle merkt sich pro easy@work-Anhang, dass er schon geholt wurde.
-- Läuft automatisch beim Start (Program.cs); hier nur als Kopie für TablePlus.
-- In BEIDE Datenbanken: hr_system_test und hrsystem.

CREATE TABLE IF NOT EXISTS easyatwork_hr_file_eingang (
    id                          serial PRIMARY KEY,
    employee_id                 integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    easyatwork_customer_id      integer NOT NULL,
    easyatwork_employee_id      integer NOT NULL,
    easyatwork_file_id          bigint  NOT NULL,
    easyatwork_attachment_id    bigint  NOT NULL,
    dokument_name               text    NOT NULL DEFAULT '',
    datei_name                  text    NOT NULL DEFAULT '',
    mime_type                   text,
    file_size_bytes             bigint,
    hochgeladen_von_eaw_user_id bigint,
    hochgeladen_am              timestamp without time zone,
    mailbox_document_id         integer REFERENCES mailbox_document(id) ON DELETE SET NULL,
    geholt_am                   timestamp without time zone NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_eaw_hr_file_eingang_attachment ON easyatwork_hr_file_eingang (easyatwork_attachment_id);
CREATE INDEX IF NOT EXISTS ix_eaw_hr_file_eingang_emp ON easyatwork_hr_file_eingang (employee_id);

-- Freigabe-Matrix: dritter Kanal easy@work (Walter 08.09.2026)
ALTER TABLE versand_kategorie ADD COLUMN IF NOT EXISTS eaw_scharf BOOLEAN NOT NULL DEFAULT FALSE;

-- Versandprotokoll easy@work-Mitteilung (Walter 08.09.2026)
CREATE TABLE IF NOT EXISTS easyatwork_mitteilung_log (
    id                    serial PRIMARY KEY,
    gesendet_am           timestamp without time zone NOT NULL DEFAULT now(),
    gesendet_von_user_id  integer REFERENCES app_user(id) ON DELETE SET NULL,
    betreff               text NOT NULL DEFAULT '',
    filiale               text NOT NULL DEFAULT '',
    modelle               text NOT NULL DEFAULT '',
    funktionen            text NOT NULL DEFAULT '',
    unterzeichner         text,
    anhang_name           text,
    mit_text              boolean NOT NULL DEFAULT false,
    scharf                boolean NOT NULL DEFAULT false,
    anzahl_gesendet       integer NOT NULL DEFAULT 0,
    anzahl_fehlgeschlagen integer NOT NULL DEFAULT 0,
    anzahl_ohne_eaw_id    integer NOT NULL DEFAULT 0,
    anzahl_umgeleitet     integer NOT NULL DEFAULT 0,
    details_json          text
);
