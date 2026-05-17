-- Theme-Präferenz pro User: 'light' (Default) oder 'dark'
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS theme VARCHAR(20) NOT NULL DEFAULT 'light';

COMMENT ON COLUMN app_user.theme IS
    'UI-Theme-Präferenz: light (Default) oder dark. Wird vom Frontend beim Login geladen und als CSS-Klasse auf <body> angewendet.';
