-- Walter 26.07.2026: Flag wenn easy@work-Nachtarbeit-Bis ≠ Soll-Ende
-- (Beginn + 2 Jahre − 1 Tag / ab 45: + 1 Jahr − 1 Tag).
-- OneCrew speichert das gerechnete Ende; dieses Flag steuert Warn-Chip + ToDo.
-- In TablePlus ausführen:

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS night_work_exam_easy_mismatch BOOLEAN NOT NULL DEFAULT false;

COMMENT ON COLUMN employee.night_work_exam_easy_mismatch IS
    'true = easy@work cf_night_work_doctors_note.to fehlt oder ≠ Soll-Ende; OneCrew nutzt trotzdem das gerechnete gültig-bis.';
