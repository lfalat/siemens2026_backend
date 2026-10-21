-- ============================================================================
--  Development seed data
--
--  Runs after 01-create-db.sql: the Aspire AppHost mounts configs/postgres/init/ into
--  /docker-entrypoint-initdb.d, and PostgreSQL executes the files in name order, once, against the
--  fresh database. The local database has no volume, so every AppHost run starts with exactly this data.
--
--  Local development only. The test harness loads 01-create-db.sql alone, so tests start empty, and
--  production never runs these scripts.
--
--  Staff live in Entra, not in the database (ADR-D004). The seed rows point at placeholder object ids
--  that belong to no Entra user, so a staff screen resolving them finds nobody:
--    00000000-0000-0000-0000-00000000aaaa  the hiring manager who "created" every post (created_by)
--    00000000-0000-0000-0000-00000000bbbb  the recruiter responsible for them (recruiter_id)
-- ============================================================================

-- ---------------------------------------------------------------------------
-- recruitment.job_post
-- 16 posts: 14 published, enough for two pages of the public list (10 a page), and 2 drafts that only staff
-- see. Between them they use every employment type and payment type. created_at is spread over the past weeks,
-- so the newest-first order looks like a real board rather than one insert.
-- ---------------------------------------------------------------------------
INSERT INTO recruitment.job_post
    (title, location, department, description, is_published, open_positions, salary, payment_type,
     employment_type, recruiter_id, created_by, created_at)
VALUES
    ('Elektrotechnik údržby', 'Bratislava', 'Údržba',
     'Staráte sa o bezporuchový chod elektrických zariadení vo výrobnej hale: preventívne prehliadky, diagnostika porúch a opravy.

Ponúkame prácu na zmeny, príplatky za nočné a víkendové zmeny a hradené školenia.

Požadujeme vyhlášku 508/2009 Z. z. (§ 21 alebo § 22) a aspoň dva roky praxe.',
     true, 2, 1650.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '1 day'),

    ('Operátor CNC strojov', 'Trnava', 'Výroba',
     'Obsluhujete CNC sústruhy a frézy, kontrolujete rozmery dielcov podľa výkresovej dokumentácie a zapisujete výsledky merania.

Zaučíme vás; výhodou je skúsenosť so systémami Sinumerik alebo Heidenhain.',
     true, 4, 9.80, 'hourly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '2 days'),

    ('Vývojár softvéru (.NET)', 'Žilina', 'Technológie',
     'Vyvíjate interné aplikácie na plánovanie výroby v C# a ASP.NET Core, navrhujete REST API a píšete testy.

Pracujeme v malom tíme so zavedeným code review a CI. Dva dni v týždni môžete pracovať z domu.',
     true, 1, 2800.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '3 days'),

    ('Skladník', 'Senec', 'Logistika',
     'Príjem a výdaj tovaru, vychystávanie objednávok so skenerom a práca s vysokozdvižným vozíkom.

Preukaz na VZV je výhodou, nie podmienkou: kurz vám zaplatíme.',
     true, 3, 8.50, 'hourly', 'fixed_term',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '4 days'),

    ('Brigáda: pomocník vo výrobe', 'Nitra', 'Výroba',
     'Jednoduchá montáž a balenie dielcov na linke. Vhodné pre študentov: zmeny si vyberáte podľa rozvrhu.

Podmienkou je status študenta a vek nad 18 rokov.',
     true, 6, 7.20, 'hourly', 'student_work_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '5 days'),

    ('Vedúci výroby', 'Košice', 'Výroba',
     'Riadite tri výrobné linky a tím 40 ľudí, zodpovedáte za plnenie plánu, kvalitu a bezpečnosť práce.

Očakávame skúsenosť s riadením výroby v automotive alebo strojárstve a znalosť princípov lean manufacturingu.',
     true, 1, 42000.00, 'yearly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '6 days'),

    ('Špecialista náboru', 'Bratislava', 'Ľudské zdroje',
     'Vediete náborový proces od inzerátu po nástup: oslovujete kandidátov, vediete pohovory a spolupracujete s manažérmi.

Hľadáme komunikatívneho človeka, ktorý rád pracuje s ľuďmi.',
     true, 1, 1900.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '8 days'),

    ('Účtovník / účtovníčka', 'Banská Bystrica', 'Financie',
     'Spracúvate prijaté a vydané faktúry, bankové výpisy a podklady pre mesačnú uzávierku.

Pracovný čas 6 hodín denne, vhodné aj pre rodičov po materskej dovolenke.',
     true, 1, 1250.00, 'monthly', 'part_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '10 days'),

    ('Kontrolór kvality', 'Trnava', 'Kvalita',
     'Kontrolujete dielce na vstupe aj výstupe z výroby, meriate na súradnicovom meracom stroji a vediete záznamy o nezhodách.

Ide o zástup počas rodičovskej dovolenky na 12 mesiacov s možnosťou predĺženia.',
     true, 1, 1450.00, 'monthly', 'fixed_term',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '12 days'),

    ('Technický prekladateľ (nemčina)', 'Bratislava', 'Technológie',
     'Prekladáte technickú dokumentáciu a návody na obsluhu strojov z nemčiny do slovenčiny, približne 40 strán mesačne.

Prácu robíte z domu, termíny si dohodneme vopred.',
     true, 1, 14.00, 'hourly', 'work_activity_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '14 days'),

    ('Inventúra skladu (jednorazovo)', 'Senec', 'Logistika',
     'Pomoc pri koncoročnej inventúre skladu: počítanie zásob a zápis do skenera. Práca na dva víkendy v decembri.',
     true, 8, 9.00, 'hourly', 'work_performance_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '16 days'),

    ('Elektroprojektant', 'Žilina', 'Projekcia',
     'Navrhujete elektroinštalácie a rozvádzače pre nové výrobné linky v EPLAN-e, od projektu po uvedenie do prevádzky.

Spolupráca je na živnosť s dlhodobým výhľadom, rozsah približne 30 hodín týždenne.',
     true, 1, 28.00, 'hourly', 'self_employed',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '18 days'),

    ('Mechanik strojov', 'Prešov', 'Údržba',
     'Opravujete a nastavujete mechanické časti výrobných strojov: prevodovky, hydrauliku a pneumatiku.

Ponúkame stabilnú prácu, 13. a 14. plat a príspevok na dopravu.',
     true, 2, 1500.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '21 days'),

    ('Asistent/ka nákupu', 'Košice', 'Nákup',
     'Pripravujete objednávky u dodávateľov, sledujete termíny dodania a aktualizujete údaje v ERP systéme.

Výhodou je angličtina na komunikatívnej úrovni.',
     true, 1, 1300.00, 'monthly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '25 days'),

    -- Drafts (koncepty): never in the public list or detail.
    ('Vedúci logistiky', 'Senec', 'Logistika',
     'Koncept: náplň práce ešte dopĺňame s vedením skladu.',
     false, 1, 36000.00, 'yearly', 'full_time',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '2 hours'),

    ('Brigáda: letná výpomoc v sklade', 'Nitra', 'Logistika',
     'Koncept: zverejníme v máji.',
     false, 5, 7.00, 'hourly', 'student_work_agreement',
     '00000000-0000-0000-0000-00000000bbbb', '00000000-0000-0000-0000-00000000aaaa', now() - interval '1 hour');
