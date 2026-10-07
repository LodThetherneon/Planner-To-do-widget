# CLAUDE.md – Planner Widget 3 (C# / WinUI 3)

A `Hobbi\Planner` mappában lévő régi Python/PyQt6 widget újraírása; ez a mappa (`Hobbi\Újplannerc`) önálló, a régire
nem hivatkozik. Felhasználói leírás: `README.md`, képes útmutató: `UTMUTATO.md` (képei: `docs/kepek/`, a bemutató módból
készültek – valódi Planner-adatot tartalmazó képernyőkép ide soha ne kerüljön). A UI-szövegek, kommentek, naplóüzenetek
magyarul vannak – új kódban is így. A gyökérben lévő `Bemutató.cmd` / `Indítás.cmd` / `Telepítés.cmd` a `scripts\*.ps1`-et hívja.

## Parancsok

- Build: `dotnet build PlannerWidget.sln` (a .NET 10 SDK `C:\Program Files\dotnet`-ben; NuGet-forrás a helyi `nuget.config`-ban,
  mert a felhasználói NuGet.Config üres).
- Futtatás: `scripts\run.ps1 [-Demo]` · Teszt: `scripts\test.ps1 [-Pdf <jelenléti.pdf>]` · Telepítés: `scripts\install.ps1`.
- Kiosztás kollégáknak: `scripts\package.ps1` → önálló ZIP (`...\PlannerWidget.Build\dist`), benne a `scripts\dist\` telepítője
  (`Telepites.cmd` → `telepito.ps1`; ASCII fájlnevek a ZIP miatt) és a `docs\TELEPITES_KOLLEGAKNAK.md` + képei.
  A telepítő tesztelése: `telepito.ps1 -InstallDir <ideiglenes> -NoShortcuts -NoStart` (ne a valódi helyre).
- A kimenet a `%LOCALAPPDATA%\PlannerWidget.Build` alá kerül (`Directory.Build.props`: `UseArtifactsOutput`), NEM a OneDrive-ra.
  Debug exe: `...\bin\PlannerWidget.App\debug_win-x64\PlannerWidget.exe`.
- Fut-e már: a build nem tudja felülírni a futó exe-t → előbb `Get-Process PlannerWidget | Stop-Process`.
- PowerShell 5.1: a `.ps1` fájlokat UTF-8 **BOM-mal** kell menteni (ékezetek); `??`/ternary nincs.

## Felépítés

- `src/PlannerWidget.Core` (net10.0, UI-független, tesztelt): `Graph/PlannerClient` (lapozás, 429/503 újrapróbálás,
  412 → friss ETag + 1 újrapróba; `partial`, a To Do része `Graph/TodoClient.cs`: `ITodoClient`), `Sync/` (szinkronhibák),
  `Tasks/` (rendezés, `PlannerService` gyorsítótárakkal), `Attendance/` (jelenléti ív),
  `Settings/` (`AppSettings`, `SettingsStore`, régi Python-beállítások importja), `Storage/AppPaths`, `Logging/Log`.
- `src/PlannerWidget.App` (WinUI 3, unpackaged, `WindowsAppSDKSelfContained`): MVVM CommunityToolkit.Mvvm 8.4
  **partial property** szintaxissal (`[ObservableProperty] public partial T X { get; set; }`). Kompozíciós gyökér: `App.xaml.cs`.
  Saját `Program.Main` (`DISABLE_XAML_GENERATED_MAIN`) az egypéldányos futtatáshoz (`AppInstance`) és a `--demo` kapcsolóhoz.
- `tests/PlannerWidget.Core.Tests` (xUnit 2). A PDF-integrációs teszt csak `PLANNERWIDGET_TEST_PDF` env-vel fut, másolaton.

## Fontos döntések / buktatók

- **Windows App SDK: csak a `Microsoft.WindowsAppSDK.WinUI` komponenscsomag** (1.8) van hivatkozva, nem a metacsomag
  (az AI/ML komponenseket is behúzná). Emiatt nincs `AppNotificationManager` (önálló módban hiányzik a DLL-je) →
  az értesítések a tálcaikonon át mennek (`ShellIntegration.ShowNotification`, `NIF_INFO`).
- Tálcaikon és globális gyorsbillentyű: saját rejtett Win32 ablak (`Services/ShellIntegration.cs`), külső csomag nélkül
  (a H.NotifyIcon a régi 1.6-os SDK-ra épül, ütközne).
- Ablak: `OverlappedPresenter.SetBorderAndTitleBar(true,false)`, `IsShownInSwitchers=false`, húzás a fejlécen
  `InputNonClientPointerSource` Caption-régióval (`UpdateDragRegion`). Méretezés fizikai pixelben, a jobb él rögzített.
- Bejelentkezés: MSAL, alapból **böngésző** (`http://localhost`, a régi regisztrációval biztosan működik). WAM opcionális
  (`AuthMode.Automatic`), plusz redirect URI kell hozzá. `settings.json` SchemaVersion 2-es migrációja böngészőre állít.
- PDF: PDFsharp 6.2. A megjelenést saját kód rajzolja (`Attendance/TextFieldAppearance.cs`) a mező /DA betűjével és /Q
  igazításával; nem Latin-1 szövegnél (ő, ű) PDFsharp-tartalék + `NeedAppearances`. A napi mezősablonokat a valódi
  mezőlistán pontozva választja (`FieldTemplate.Derive/Repair`) – a jelenléti ívben 1–9: `60022001`, 10-től `600220010`.
- Határidő: Graph UTC → helyi nap (`DueDateConverter`); küldéskor helyi dél UTC-ben.
- **Aktív lista = `ListView`** (húzással átrendezés; a „Kész feladatok” a láblécében, `VerticalAlignment=Top`, hogy rövid
  listánál ne kerüljön az aljára). A sorrend helyi (`ManualOrder` + `task-order.json`, nem a Planner `orderHint`-je);
  szabály: lejárt nem kerülhet határidő nélküli alá (és fordítva), sértéskor a legközelebbi megengedett hely. A ListView
  lista-cserekor lejjebb csúszhat → `KeepListAtTop` (`LayoutUpdated` után vissza a tetejére, ha ott álltál).
  UIA-val húzni nem lehet: valódi abszolút `mouse_event` kell (a `SetCursorPos` nem indít WinUI-húzást).
- Ellenőrzőlista-elem szövege szerkeszthető (`SetChecklistItemTitleAsync`), korlát: `ChecklistItem.MaxTitleLength` = 100.
- Háttér: saját `Services/WidgetBackdrop` (DesktopAcrylicController); `BlurWhenActive`/`BlurWhenInactive` beállítás
  (az `IsInputActive=false` adja az egyszínű tartalék színt). A bemutató feladat-azonosítói állandók (`demo-001`…), csak a
  #106 véletlen.
- x:Bind segédfüggvények a `Ui.cs`-ben (konverterek helyett). Gombokon, ahol a tartalom nem szöveg, legyen
  `AutomationProperties.Name` (UIA-teszteléshez és képernyőolvasóhoz).
- **Beérkező ügyek** (`Core/Tickets/`): a widget NEM éri el a SharePointot, és nem kér új jogosultságot (`GraphConfig.Scopes`
  változatlan). Szerződés a (nem általunk készített) Power Automate flow-kkal: ticket = feladat a `Tickets.PlanId` tervben,
  cím „[#<ID>] <Tárgy>”, felelősök = a Felelős mező; percentComplete 100 → a ticket „Done” + e-mailek archiválása
  (visszafordíthatatlan, ezért megerősítés); 100 → 0 NEM jut vissza. Felelős nélküli ticket → a flow TÖRLI a feladatot,
  újra felelőssel → ÚJ feladat-azonosítóval hozza létre. A widget 404-nél (`IsGone` → `RemoveGoneTask`) kiveszi a listából
  és Info-sávval tájékoztat; a `TicketTracker` a #ID-kat is megjegyzi (`RecentTicketIds`), 24 órán (`ReturnWindow`) belül
  visszatérő #ID-ról nem értesít. A flow minden felelőssel bíró ügyet kezel (auto. és egyedi is); az új
  feladat a New gyűjtőben jön létre (átvenni a karika „Átvettem” pontjával lehet); felelős nélkül törli a feladatot. Felelőst csak a SharePoint-listában szabad módosítani (a Planner
  felől nem szinkronizál, és visszaállítja) → ügynél a widget nem töröl (`CanDelete`, `ExplainTicketAssigneeAsync`),
  és az ügyterv nem szerepel az új feladat tervlistájában (`LoadAddPlansAsync`).
- **Ügy-státusz = gyűjtő** (`Core/Tickets/TicketWorkflow.cs`): az ügyterv bucketjei = a lista státuszai (pontos név:
  New, Processed, SOS, In progress, KÉRDÉSES, Done, Revisit; üres/ismeretlen bucket = New: `TicketBuckets.StatusOf`).
  Kártya (a felhasználó állította be, NE rendezd át): lila csempe (`Ui.TicketCardTint`), lila cím + alatta „#123”, nincs
  „Ügy” jelvény/tervnév. **Előre a karikával**: nyitott ügynél a karika `MenuFlyout`-ot nyit (`TicketWorkflow.ForwardOptions`):
  New → Átvettem (Processed); Processed/SOS/KÉRDÉSES → Elkezdem (In progress **+ percentComplete 50** egy PATCH-ben,
  `PercentForMove`); Revisit → Folytatom (ugyanígy); In progress és Revisit → „Kész” = **Done gyűjtő + 100% egy PATCH-ben**
  (`ToggleCompleteAsync`, `CanComplete`), máshol tiltva. **„…”**: SOS/KÉRDÉSES/New (`MenuOptions`; Revisit/Done kézzel soha). Közös szabály:
  `CanMoveTo`. **Revisit = „Új válasz érkezett”** (`IsNewReply`, sárga címke): a rendszer egy Done ügyet 100→0%-ra nyit és
  Revisit-be tesz; a `TicketTracker.Track` a kész-listából felismeri (`TicketChanges.Reopened`) → értesítés (csak ha a
  gyűjtő Revisit, a kézi visszanyitás nem). 404/412 után a frissítés a művelet végén fut (`TryEnqueue`), különben a még
  „dolgozó” kártya miatt kimaradna. A lista a widgetet hétköznap 7–19
  között néhány perc (max. ~10 perc) késéssel követi; több felelős = egy közös feladat, közös státusz. `TicketBuckets` név↔id (sosem égetjük be; `PlannerService.GetBucketsAsync` gyorsítótár, ismeretlen
  bucketId → `InvalidateBuckets` + újratöltés, `MainViewModel.EnsureTicketBucketsAsync`). Gyűjtőváltás: `TaskPatch.BucketId`
  (If-Match, 412 → friss ETag + 1 újrapróba a meglévő `UpdateTaskAsync`-ban), optimista, hibánál vissza. A kártya gombja
  `Tapped`-et kezeltnek jelöli (`OnCardButtonTapped`), különben kinyitná a részleteket. Demó: az ügyterv saját gyűjtői.
- `WidgetBackdrop`: az `OnDefaultSystemBackdropConfigurationChanged` szándékosan üres – az alaposztályé saját konfigurációnál
  témaváltáskor ArgumentException-t dob (E2E-ben derült ki).
- E2E (2026-10-05): a ZIP-ből telepített programon, bemutató módban, UIA + valódi egér (húzás) – 37 lépés, kétszer hibátlan.
  Buktatók a szkriptíráshoz: a „Kész feladatok” elemei csak leggörgetve vannak a UIA-fában; művelet közben a kártya gombjai
  rejtve/tiltva (várni kell); a fejléc X-e és a párbeszédek gombja is lehet „Bezárás”; PowerShellben a „ ” idézőjelnek számít. Link: `Tickets.UrlTemplate` (`{id}`), alapból a
  `.../Lists/Berkez%20gyek/DispForm.aspx?ID={id}`. Új ügyek: `TicketTracker` + `ticket-state.json` (első alkalommal csak alapállapot).
  Alap-ügyterv: „Ügyek” (MFÜI - RFK), `TicketSettings.DefaultPlanId`; a `SettingsStore.Normalize` állítja be, ha a PlanId
  hiányzik – kivéve `NoPlan == true` (tudatos „Nincs”). A PlanId-nak szándékosan nincs mezőszintű alapértéke (a null nem
  kerül a JSON-ba, betöltéskor felülírná a „Nincs”-et). Bemutató módban az alaptervet a demó tervre cseréli.
  Ügyterv nélkül minden a régi; a fejléc „N ügy” pillje szűk helyen előbb a „mára”-t, majd magát rejti (`FitTicketPill`).
- **Szinkronhibák** (`Core/Sync/SyncErrors.cs`, `ViewModels/MainViewModel.Sync.cs`): a Power Automate a hibáit a To Do
  „Szinkronhibák” listájába írja („HIBA | <folyamat>”, a body HTML-jében `make.powerautomate.com` link). Lista-id név alapján,
  gyorsítótárazva, 404 → újrakeresés; nyitott feladatok `$filter`-rel (400/501 → szűrő nélkül) + kliensoldali szűrés.
  Azonos címek csoportosítva; „Megoldva” = a csoport összes To Do-feladatára PATCH `status=completed`. **„Szinkron életjel”**
  feladat: SOSE zárjuk le/töröljük/mutatjuk hibaként; riasztás csak Budapest szerint 6:20–21:00 között, ha a
  lastModified > 20 perc. Scope NEM változott (Tasks.ReadWrite). Frissítés a Planner-frissítés végén, hiba csendben
  (Log.Warn). Fejléc: `SyncButton` (`HasSyncIssues`, piros szám / narancs „!”), a `FitTicketPill` ezt is figyelembe veszi.
  A widget To Do-feladatokat máshol nem listáz, így kizárni sem kell. Demó: 2 azonos + 1 másik hiba, `--demo-stale-heartbeat`
  (`run.ps1 -Demo -StaleHeartbeat`) leállt életjellel.
- **Havi Wrapped** (`Core/Wrapped/WrappedService.cs`, `MainViewModel.Wrapped.cs`, profilkép-gomb a `MainWindow.xaml`-ben): a Planner nem őrzi a régi
  feladatokat, ezért minden frissítés a `wrapped.json` főkönyvbe jegyzi őket (ügy kulcsa a #szám – a flow újralétrehozása nem számít kétszer);
  új hónap első frissítésekor az előző hónap jelentése elkészül, `Unseen` → piros kör + „W” monogram. Demóban `SeedDemoHistory`.
  Az „F5” csak a Grid billentyűparancsa, a gomb tooltipjéből kivettük a „(F5)”-öt.
- Bemutató mód (`--demo`): `DemoPlannerClient`/`DemoAuthService`, külön adatmappa (`%LOCALAPPDATA%\PlannerWidget\demo`),
  a PDF-et csak olvassa (`ReadOnlyPdfFormService`). Van „Beérkező ügyek” terve (`plan-ugyek`, induláskor ügytervnek állítja,
  ha nincs beállítva). Forgatókönyv a letöltések sorszáma szerint (`DemoPlannerClient.NextTaskList`): 2. – új #106 (értesítés,
  de a 24 órás szabály miatt naponta csak egyszer; újra: `demo\ticket-state.json` törlése), 3. – utána a #103 törlődik
  (rá kattintva 404), 4. – eltűnik, 5. – új azonosítóval visszajön (értesítés nélkül). Hiányzó elemre a demó kliens is 404-et dob. UI-változtatás után ezzel érdemes képernyőképpel ellenőrizni.

## Állapot (%LOCALAPPDATA%\PlannerWidget)

`settings.json`, `task-cache.json`, `plan-titles.json`, `ticket-state.json`, `task-order.json`, `msal-browser.cache` / `msal-wam.cache` (DPAPI – ne olvasd/másold),
`logs\app.log`. A régi Python-verzió fájljai (`planner_defaults.json`, `plan_cache.json`) kézzel importálhatók:
Beállítások → Névjegy → *Importálás…* (`SettingsViewModel.ImportLegacyAsync` → `LegacySettingsImporter`, meglévőt nem ír felül).
A felhasználó gépén ez 2026-10-01-én már megtörtént, és a felhasználó be is jelentkezett (valódi adatok működnek).

## Tesztelési tipp

UI-ellenőrzés: `--demo` módban indítani, UI Automationnel (név alapján, `AutomationProperties.Name`) kattintani és
képernyőképet készíteni. Indítás előtt csak a `--demo` parancssorú példányt állítsd le, a felhasználó valódi widgetjét ne.
