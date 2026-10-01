# CLAUDE.md – Planner Widget 3 (C# / WinUI 3)

A `Hobbi\Planner` mappában lévő régi Python/PyQt6 widget újraírása; ez a mappa (`Hobbi\Újplannerc`) önálló, a régire
nem hivatkozik. Felhasználói leírás: `README.md`, képes útmutató: `UTMUTATO.md` (képei: `docs/kepek/`, a bemutató módból
készültek – valódi Planner-adatot tartalmazó képernyőkép ide soha ne kerüljön). A UI-szövegek, kommentek, naplóüzenetek
magyarul vannak – új kódban is így. A gyökérben lévő `Bemutató.cmd` / `Indítás.cmd` / `Telepítés.cmd` a `scripts\*.ps1`-et hívja.

## Parancsok

- Build: `dotnet build PlannerWidget.sln` (a .NET 10 SDK `C:\Program Files\dotnet`-ben; NuGet-forrás a helyi `nuget.config`-ban,
  mert a felhasználói NuGet.Config üres).
- Futtatás: `scripts\run.ps1 [-Demo]` · Teszt: `scripts\test.ps1 [-Pdf <jelenléti.pdf>]` · Telepítés: `scripts\install.ps1`.
- A kimenet a `%LOCALAPPDATA%\PlannerWidget.Build` alá kerül (`Directory.Build.props`: `UseArtifactsOutput`), NEM a OneDrive-ra.
  Debug exe: `...\bin\PlannerWidget.App\debug_win-x64\PlannerWidget.exe`.
- Fut-e már: a build nem tudja felülírni a futó exe-t → előbb `Get-Process PlannerWidget | Stop-Process`.
- PowerShell 5.1: a `.ps1` fájlokat UTF-8 **BOM-mal** kell menteni (ékezetek); `??`/ternary nincs.

## Felépítés

- `src/PlannerWidget.Core` (net10.0, UI-független, tesztelt): `Graph/PlannerClient` (lapozás, 429/503 újrapróbálás,
  412 → friss ETag + 1 újrapróba), `Tasks/` (rendezés, `PlannerService` gyorsítótárakkal), `Attendance/` (jelenléti ív),
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
- x:Bind segédfüggvények a `Ui.cs`-ben (konverterek helyett). Gombokon, ahol a tartalom nem szöveg, legyen
  `AutomationProperties.Name` (UIA-teszteléshez és képernyőolvasóhoz).
- Bemutató mód (`--demo`): `DemoPlannerClient`/`DemoAuthService`, külön adatmappa (`%LOCALAPPDATA%\PlannerWidget\demo`),
  a PDF-et csak olvassa (`ReadOnlyPdfFormService`). UI-változtatás után ezzel érdemes képernyőképpel ellenőrizni.

## Állapot (%LOCALAPPDATA%\PlannerWidget)

`settings.json`, `task-cache.json`, `plan-titles.json`, `msal-browser.cache` / `msal-wam.cache` (DPAPI – ne olvasd/másold),
`logs\app.log`. A régi Python-verzió fájljai (`planner_defaults.json`, `plan_cache.json`) kézzel importálhatók:
Beállítások → Névjegy → *Importálás…* (`SettingsViewModel.ImportLegacyAsync` → `LegacySettingsImporter`, meglévőt nem ír felül).
A felhasználó gépén ez 2026-10-01-én már megtörtént, és a felhasználó be is jelentkezett (valódi adatok működnek).

## Tesztelési tipp

UI-ellenőrzés: `--demo` módban indítani, UI Automationnel (név alapján, `AutomationProperties.Name`) kattintani és
képernyőképet készíteni. Indítás előtt csak a `--demo` parancssorú példányt állítsd le, a felhasználó valódi widgetjét ne.
