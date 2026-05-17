-- ════════════════════════════════════════════════════════════════════
-- Bankverbindungs-Import aus Mirus-Lohnabrechnung Januar 2025
-- Filiale 075 (Sursee), 59 MA mit IBAN
-- Idempotent: skippt MA bei denen die IBAN schon existiert.
-- ════════════════════════════════════════════════════════════════════
BEGIN;
DO $$
DECLARE
  cp_id   INT;
  emp_id  INT;
  cnt     INT;
BEGIN
  SELECT id INTO cp_id FROM company_profile WHERE restaurant_code = '075';
  IF cp_id IS NULL THEN RAISE EXCEPTION 'Filiale 075 nicht gefunden'; END IF;

  -- Agneza Laci: Valiant Bank AG CH5906300504958677835
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Agneza') AND LOWER(e.last_name) = LOWER('Laci') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5906300504958677835';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5906300504958677835', 'Valiant Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Agneza Laci';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Agneza Laci';
  END IF;
  -- Alban Salioski: UBS Switzerland AG CH720027427414655740
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Alban') AND LOWER(e.last_name) = LOWER('Salioski') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH720027427414655740';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH720027427414655740', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Alban Salioski';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Alban Salioski';
  END IF;
  -- Albulena Muja: UBS Switzerland AG CH050028828814472740
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Albulena') AND LOWER(e.last_name) = LOWER('Muja') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH050028828814472740';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH050028828814472740', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Albulena Muja';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Albulena Muja';
  END IF;
  -- Aleksandra Stojkovska: UBS Switzerland AG CH930023123116523040
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Aleksandra') AND LOWER(e.last_name) = LOWER('Stojkovska') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH930023123116523040';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH930023123116523040', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Aleksandra Stojkovska';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Aleksandra Stojkovska';
  END IF;
  -- Amleset Tesfamariam: PostFinance AG CH1709000000160258390
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Amleset') AND LOWER(e.last_name) = LOWER('Tesfamariam') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH1709000000160258390';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH1709000000160258390', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Amleset Tesfamariam';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Amleset Tesfamariam';
  END IF;
  -- Anastasija Ristova: Raiffeisen CH4380808005622262211
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Anastasija') AND LOWER(e.last_name) = LOWER('Ristova') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4380808005622262211';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4380808005622262211', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Anastasija Ristova';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Anastasija Ristova';
  END IF;
  -- Andreja Angjelkoska: Raiffeisen CH6180808006654770142
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Andreja') AND LOWER(e.last_name) = LOWER('Angjelkoska') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH6180808006654770142';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH6180808006654770142', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Andreja Angjelkoska';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Andreja Angjelkoska';
  END IF;
  -- Aneta Tanevska: UBS Switzerland AG CH170028828814799040
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Aneta') AND LOWER(e.last_name) = LOWER('Tanevska') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH170028828814799040';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH170028828814799040', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Aneta Tanevska';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Aneta Tanevska';
  END IF;
  -- Anita Djonlagic: PostFinance AG CH5809000000316361304
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Anita') AND LOWER(e.last_name) = LOWER('Djonlagic') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5809000000316361304';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5809000000316361304', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Anita Djonlagic';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Anita Djonlagic';
  END IF;
  -- Atibe Ponik: Luzerner Kantonalbank AG CH9700778223223302001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Atibe') AND LOWER(e.last_name) = LOWER('Ponik') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9700778223223302001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9700778223223302001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Atibe Ponik';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Atibe Ponik';
  END IF;
  -- Atifete Berisha: Raiffeisen CH3980808008917560736
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Atifete') AND LOWER(e.last_name) = LOWER('Berisha') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH3980808008917560736';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH3980808008917560736', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Atifete Berisha';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Atifete Berisha';
  END IF;
  -- Berina Stankov Seyfedin: Raiffeisen CH6780808007994710477
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Berina Stankov') AND LOWER(e.last_name) = LOWER('Seyfedin') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH6780808007994710477';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH6780808007994710477', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Berina Stankov Seyfedin';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Berina Stankov Seyfedin';
  END IF;
  -- Branislav Jovanovic: UBS Switzerland AG CH770026126112446040
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Branislav') AND LOWER(e.last_name) = LOWER('Jovanovic') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH770026126112446040';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH770026126112446040', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Branislav Jovanovic';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Branislav Jovanovic';
  END IF;
  -- Carmensitta Rattaggis: Raiffeisenbank Luzerner Landschaft Nordwest CH6681214000008369892
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Carmensitta') AND LOWER(e.last_name) = LOWER('Rattaggis') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH6681214000008369892';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH6681214000008369892', 'Raiffeisenbank Luzerner Landschaft Nordwest', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Carmensitta Rattaggis';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Carmensitta Rattaggis';
  END IF;
  -- Daniela Dedaj: Raiffeisen CH4480808004528246141
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Daniela') AND LOWER(e.last_name) = LOWER('Dedaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4480808004528246141';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4480808004528246141', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Daniela Dedaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Daniela Dedaj';
  END IF;
  -- Daniela Nikollajs: PostFinance AG CH5009000000157855203
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Daniela') AND LOWER(e.last_name) = LOWER('Nikollajs') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5009000000157855203';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5009000000157855203', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Daniela Nikollajs';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Daniela Nikollajs';
  END IF;
  -- Dep Nguyen: Migros Bank AG CH9508401000067996868
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Dep') AND LOWER(e.last_name) = LOWER('Nguyen') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9508401000067996868';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9508401000067996868', 'Migros Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Dep Nguyen';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Dep Nguyen';
  END IF;
  -- Dijana Prela: Raiffeisen CH4680808005849145351
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Dijana') AND LOWER(e.last_name) = LOWER('Prela') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4680808005849145351';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4680808005849145351', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Dijana Prela';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Dijana Prela';
  END IF;
  -- Dila Tetaj: PostFinance AG CH6909000000311861179
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Dila') AND LOWER(e.last_name) = LOWER('Tetaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH6909000000311861179';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH6909000000311861179', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Dila Tetaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Dila Tetaj';
  END IF;
  -- Dionis Sejfija: Luzerner Kantonalbank AG CH7900778188546592002
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Dionis') AND LOWER(e.last_name) = LOWER('Sejfija') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH7900778188546592002';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH7900778188546592002', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Dionis Sejfija';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Dionis Sejfija';
  END IF;
  -- Dragana Dimitrova: PostFinance AG CH2309000000153007920
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Dragana') AND LOWER(e.last_name) = LOWER('Dimitrova') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH2309000000153007920';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH2309000000153007920', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Dragana Dimitrova';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Dragana Dimitrova';
  END IF;
  -- Edina Sadikovic: UBS Switzerland AG CH310028828814075340
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Edina') AND LOWER(e.last_name) = LOWER('Sadikovic') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH310028828814075340';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH310028828814075340', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Edina Sadikovic';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Edina Sadikovic';
  END IF;
  -- Emre Er: Luzerner Kantonalbank AG CH4200778218643092001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Emre') AND LOWER(e.last_name) = LOWER('Er') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4200778218643092001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4200778218643092001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Emre Er';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Emre Er';
  END IF;
  -- Francesca Vivianis: UBS Switzerland AG CH070028828814985340
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Francesca') AND LOWER(e.last_name) = LOWER('Vivianis') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH070028828814985340';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH070028828814985340', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Francesca Vivianis';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Francesca Vivianis';
  END IF;
  -- Gazale Jemmo: Luzerner Kantonalbank AG CH9100778224324372001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Gazale') AND LOWER(e.last_name) = LOWER('Jemmo') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9100778224324372001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9100778224324372001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Gazale Jemmo';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Gazale Jemmo';
  END IF;
  -- Giorgio Gjorgjevski: Valiant Bank AG CH4706300503680801409
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Giorgio') AND LOWER(e.last_name) = LOWER('Gjorgjevski') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4706300503680801409';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4706300503680801409', 'Valiant Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Giorgio Gjorgjevski';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Giorgio Gjorgjevski';
  END IF;
  -- Gülvan Benek-Aslan: Luzerner Kantonalbank AG CH7900778220548402001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Gülvan') AND LOWER(e.last_name) = LOWER('Benek-Aslan') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH7900778220548402001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH7900778220548402001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Gülvan Benek-Aslan';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Gülvan Benek-Aslan';
  END IF;
  -- Gylgjan Korllak: Raiffeisen CH1680808002769629855
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Gylgjan') AND LOWER(e.last_name) = LOWER('Korllak') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH1680808002769629855';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH1680808002769629855', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Gylgjan Korllak';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Gylgjan Korllak';
  END IF;
  -- Ikonija Nikolovska: UBS Switzerland AG CH700028828815097740
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Ikonija') AND LOWER(e.last_name) = LOWER('Nikolovska') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH700028828815097740';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH700028828815097740', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Ikonija Nikolovska';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Ikonija Nikolovska';
  END IF;
  -- Jean Can Atay: UBS Switzerland AG CH180027427417528040
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Jean Can') AND LOWER(e.last_name) = LOWER('Atay') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH180027427417528040';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH180027427417528040', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Jean Can Atay';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Jean Can Atay';
  END IF;
  -- Kalyani Lavan: PostFinance AG CH2509000000160347874
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Kalyani') AND LOWER(e.last_name) = LOWER('Lavan') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH2509000000160347874';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH2509000000160347874', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Kalyani Lavan';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Kalyani Lavan';
  END IF;
  -- Karolina Tashkov: PostFinance AG CH8109000000159238544
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Karolina') AND LOWER(e.last_name) = LOWER('Tashkov') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH8109000000159238544';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH8109000000159238544', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Karolina Tashkov';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Karolina Tashkov';
  END IF;
  -- Leonora Karceva: Luzerner Kantonalbank AG CH8100778224547672001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Leonora') AND LOWER(e.last_name) = LOWER('Karceva') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH8100778224547672001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH8100778224547672001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Leonora Karceva';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Leonora Karceva';
  END IF;
  -- Maria Stojmenova: PostFinance AG CH3809000000163200313
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Maria') AND LOWER(e.last_name) = LOWER('Stojmenova') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH3809000000163200313';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH3809000000163200313', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Maria Stojmenova';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Maria Stojmenova';
  END IF;
  -- Marija Koceva: Luzerner Kantonalbank AG CH5700778212267952001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Marija') AND LOWER(e.last_name) = LOWER('Koceva') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5700778212267952001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5700778212267952001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Marija Koceva';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Marija Koceva';
  END IF;
  -- Marija Mitreva: PostFinance AG CH4309000000160434268
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Marija') AND LOWER(e.last_name) = LOWER('Mitreva') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4309000000160434268';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4309000000160434268', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Marija Mitreva';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Marija Mitreva';
  END IF;
  -- Marinica Lenuta-Panciuc: PostFinance AG CH0309000000161787750
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Marinica') AND LOWER(e.last_name) = LOWER('Lenuta-Panciuc') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH0309000000161787750';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH0309000000161787750', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Marinica Lenuta-Panciuc';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Marinica Lenuta-Panciuc';
  END IF;
  -- Martina Bozhinova: PostFinance AG CH5709000000162989287
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Martina') AND LOWER(e.last_name) = LOWER('Bozhinova') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5709000000162989287';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5709000000162989287', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Martina Bozhinova';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Martina Bozhinova';
  END IF;
  -- Mimoza Bytyci: UBS Switzerland AG CH830028828815153440
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Mimoza') AND LOWER(e.last_name) = LOWER('Bytyci') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH830028828815153440';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH830028828815153440', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Mimoza Bytyci';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Mimoza Bytyci';
  END IF;
  -- Natasha Hulaj: Raiffeisen CH7380808001798466187
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Natasha') AND LOWER(e.last_name) = LOWER('Hulaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH7380808001798466187';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH7380808001798466187', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Natasha Hulaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Natasha Hulaj';
  END IF;
  -- Novel Amanuel: Luzerner Kantonalbank AG CH7200778215149522001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Novel') AND LOWER(e.last_name) = LOWER('Amanuel') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH7200778215149522001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH7200778215149522001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Novel Amanuel';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Novel Amanuel';
  END IF;
  -- Oktavia Anggriani: Luzerner Kantonalbank AG CH9300778217373312001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Oktavia') AND LOWER(e.last_name) = LOWER('Anggriani') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9300778217373312001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9300778217373312001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Oktavia Anggriani';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Oktavia Anggriani';
  END IF;
  -- Rabin Yachobi: UBS Switzerland AG CH800028828815086040
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Rabin') AND LOWER(e.last_name) = LOWER('Yachobi') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH800028828815086040';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH800028828815086040', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Rabin Yachobi';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Rabin Yachobi';
  END IF;
  -- Raphael Nierle: Luzerner Kantonalbank AG CH1800778196014872002
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Raphael') AND LOWER(e.last_name) = LOWER('Nierle') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH1800778196014872002';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH1800778196014872002', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Raphael Nierle';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Raphael Nierle';
  END IF;
  -- Rifadije Ajrulli: UBS Switzerland AG CH770028828811213240
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Rifadije') AND LOWER(e.last_name) = LOWER('Ajrulli') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH770028828811213240';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH770028828811213240', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Rifadije Ajrulli';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Rifadije Ajrulli';
  END IF;
  -- Samire Islamaj: Valiant Bank AG CH4806300016901659304
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Samire') AND LOWER(e.last_name) = LOWER('Islamaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH4806300016901659304';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH4806300016901659304', 'Valiant Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Samire Islamaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Samire Islamaj';
  END IF;
  -- Sara Mundruc: PostFinance AG CH9409000000163600594
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Sara') AND LOWER(e.last_name) = LOWER('Mundruc') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9409000000163600594';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9409000000163600594', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Sara Mundruc';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Sara Mundruc';
  END IF;
  -- Senada Imsirovic: Neue Aargauer Bank AG CH1605881093786850000
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Senada') AND LOWER(e.last_name) = LOWER('Imsirovic') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH1605881093786850000';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH1605881093786850000', 'Neue Aargauer Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Senada Imsirovic';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Senada Imsirovic';
  END IF;
  -- Sibela Etemi: Raiffeisenbank Ettiswil CH0681212000001243221
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Sibela') AND LOWER(e.last_name) = LOWER('Etemi') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH0681212000001243221';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH0681212000001243221', 'Raiffeisenbank Ettiswil', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Sibela Etemi';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Sibela Etemi';
  END IF;
  -- Simonida Tomic: UBS Switzerland AG CH950028828813632440
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Simonida') AND LOWER(e.last_name) = LOWER('Tomic') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH950028828813632440';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH950028828813632440', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Simonida Tomic';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Simonida Tomic';
  END IF;
  -- Sonila Lulo: Raiffeisen CH5280808003266555745
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Sonila') AND LOWER(e.last_name) = LOWER('Lulo') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5280808003266555745';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5280808003266555745', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Sonila Lulo';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Sonila Lulo';
  END IF;
  -- Uresa Krasniqi: Luzerner Kantonalbank AG CH0300778222990442001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Uresa') AND LOWER(e.last_name) = LOWER('Krasniqi') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH0300778222990442001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH0300778222990442001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Uresa Krasniqi';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Uresa Krasniqi';
  END IF;
  -- Valbone Velaj: PostFinance AG CH5509000000401943391
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Valbone') AND LOWER(e.last_name) = LOWER('Velaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH5509000000401943391';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH5509000000401943391', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Valbone Velaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Valbone Velaj';
  END IF;
  -- Valentina Bardheci: PostFinance AG CH2309000000165450498
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Valentina') AND LOWER(e.last_name) = LOWER('Bardheci') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH2309000000165450498';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH2309000000165450498', 'PostFinance AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Valentina Bardheci';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Valentina Bardheci';
  END IF;
  -- Vildan Özgür: Bank Cler AG CH1308440259176902001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Vildan') AND LOWER(e.last_name) = LOWER('Özgür') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH1308440259176902001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH1308440259176902001', 'Bank Cler AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Vildan Özgür';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Vildan Özgür';
  END IF;
  -- Virgilla Jasina Von Wyls: Raiffeisen CH8880808008224432483
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Virgilla Jasina Von') AND LOWER(e.last_name) = LOWER('Wyls') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH8880808008224432483';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH8880808008224432483', 'Raiffeisen', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Virgilla Jasina Von Wyls';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Virgilla Jasina Von Wyls';
  END IF;
  -- Vlore Jaha: Neue Aargauer Bank AG CH3305881112178680000
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Vlore') AND LOWER(e.last_name) = LOWER('Jaha') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH3305881112178680000';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH3305881112178680000', 'Neue Aargauer Bank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Vlore Jaha';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Vlore Jaha';
  END IF;
  -- Xhevahire Rramanaj: Luzerner Kantonalbank AG CH9400778215034622001
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Xhevahire') AND LOWER(e.last_name) = LOWER('Rramanaj') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH9400778215034622001';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH9400778215034622001', 'Luzerner Kantonalbank AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Xhevahire Rramanaj';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Xhevahire Rramanaj';
  END IF;
  -- Zelan Bakr: UBS Switzerland AG CH690024824814147740
  emp_id := NULL;
  SELECT e.id INTO emp_id FROM employee e
    INNER JOIN employment emp ON emp.employee_id = e.id AND emp.company_profile_id = cp_id AND emp.is_active = true
    WHERE LOWER(e.first_name) = LOWER('Zelan') AND LOWER(e.last_name) = LOWER('Bakr') LIMIT 1;
  IF emp_id IS NOT NULL THEN
    SELECT COUNT(*) INTO cnt FROM employee_bank_account WHERE employee_id = emp_id AND iban = 'CH690024824814147740';
    IF cnt = 0 THEN
      INSERT INTO employee_bank_account (employee_id, iban, bank_name, kontoinhaber, is_hauptbank, aufteilung_typ, valid_from, created_at, updated_at)
      VALUES (emp_id, 'CH690024824814147740', 'UBS Switzerland AG', NULL, true, 'VOLL', '2025-01-01', now(), now());
      RAISE NOTICE '+ Zelan Bakr';
    END IF;
  ELSE
    RAISE NOTICE '? NICHT GEFUNDEN: Zelan Bakr';
  END IF;
END $$;
COMMIT;

-- 59 Einträge im Script.

-- Kontrolle danach:
SELECT e.first_name || ' ' || e.last_name AS name, e.employee_number, eba.iban, eba.bank_name
  FROM employee e
  INNER JOIN employment emp ON emp.employee_id = e.id AND emp.is_active = true
  INNER JOIN company_profile cp ON cp.id = emp.company_profile_id AND cp.restaurant_code = '075'
  LEFT JOIN employee_bank_account eba ON eba.employee_id = e.id AND eba.is_hauptbank = true AND eba.valid_to IS NULL
 ORDER BY e.first_name, e.last_name;
