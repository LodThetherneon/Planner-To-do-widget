# Planner Widget 3.0 (C# / WinUI 3)

Asztali widget a Microsoft Planner feladataidhoz, munkaidő-nyilvántartással (jelenléti ív PDF).
A régi Python/PyQt6 változat (`Hobbi\Planner`) teljes újraírása natív Windows 11-es felülettel.

> 📘 **Részletes, képes útmutató: [UTMUTATO.md](UTMUTATO.md)**

## Letöltés (kész program, fordítás nélkül)

1. A [legutóbbi kiadás](https://github.com/LodThetherneon/Planner-To-do-widget/releases/latest) oldaláról töltsd le a
   `PlannerWidget-v3.0-win-x64.zip` fájlt.
2. Csomagold ki egy állandó helyre (pl. `Dokumentumok\PlannerWidget`), és indítsd el a `PlannerWidget.exe`-t.
   A program nincs digitálisan aláírva, ezért a Windows első indításkor figyelmeztethet:
   *További információ → Futtatás mindenképp*.
3. Kattints a **Bejelentkezés** gombra, és jelentkezz be a munkahelyi Microsoft 365-fiókoddal a böngészőben.
   (A program a szervezet Entra-alkalmazását használja, ezért más szervezet fiókjával nem működik.)
4. A jelenléti ív PDF-jét az első **Munka kezdete** gombnyomáskor kérdezi meg; a saját havi ívedet válaszd.

Nem kell hozzá semmit telepíteni (a .NET és a Windows App SDK benne van a csomagban). Windows 10 (2004+) vagy 11, 64 bites.
Indítás a Windows-zal: **Beállítások → Indítás a Windows-zal**. Eltávolítás: töröld a mappát, és ha a beállításaidra
sincs szükség, a `%LOCALAPPDATA%\PlannerWidget` mappát is.

A régi (Python) változat a [V2-es kiadásban](https://github.com/LodThetherneon/Planner-To-do-widget/releases/tag/V2) érhető el.

## Gyors kezdés (forráskódból)

Dupla kattintással, ebben a mappában:

| Fájl | Mit csinál |
|---|---|
| `Bemutató.cmd` | Kipróbálás kitalált adatokkal (semmi nem módosul) |
| `Indítás.cmd` | Fordítás + indítás a valódi Planner-feladataiddal (első alkalommal bejelentkezés a böngészőben) |
| `Telepítés.cmd` | Végleges telepítés + Start menü és asztali parancsikon |

A régi Python-verzió beállításai (tervenkénti alapértelmezett oszlop, jelenléti PDF útvonala, mezőbeállítások,
tervnevek) bármikor átvehetők: **Beállítások → Névjegy → Régi (Python) verzió beállításai → Importálás…**

A `.cmd` fájlok mögötti PowerShell-szkriptek (PowerShellben is futtathatók, ebben a mappában):

| Parancs | Mit csinál |
|---|---|
| `scripts\run.ps1 [-Demo]` | Lefordítja (Debug) és elindítja |
| `scripts\test.ps1 [-Pdf <útvonal>]` | Automatikus tesztek; `-Pdf`-fel a PDF-kitöltést is teszteli egy **másolaton** |
| `scripts\install.ps1 [-Desktop] [-Autostart] [-SelfContained]` | Kiadási változat telepítése ide: `%LOCALAPPDATA%\Programs\PlannerWidget` |
| `scripts\uninstall.ps1 [-RemoveData]` | Eltávolítás (a `-RemoveData` a beállításokat és a bejelentkezést is törli) |

Követelmény: Windows 10 (2004+) vagy 11, .NET 10 SDK (a fejlesztéshez; a telepített változathoz elég a .NET 10 futtatókörnyezet,
`-SelfContained` esetén az sem kell).

## Mit tud

**Feladatok**
- A neked kiosztott Planner-feladatok, határidő szerint rendezve: lejárt → mai/7 napon belüli → később → határidő nélkül.
- Színsáv a kártya szélén (piros: lejárt, sárga: 7 napon belül, zöld: később), relatív határidő („Ma”, „Holnap”, „2 napja lejárt”).
- Prioritás, terv neve, ellenőrzőlista-állapot (pl. 2/4) és leírás-jelzés a kártyán.
- Kártyára kattintva kinyílnak a **részletek**: leírás és **kipipálható ellenőrzőlista**.
- Kész / visszanyitás egy kattintással (hanggal), **szerkesztés** (cím, határidő, prioritás), **törlés megerősítéssel**,
  megnyitás a Planner weben.
- **Új feladat**: terv, oszlop (az utoljára használtat tervenként megjegyzi), határidő, prioritás.
- **Keresés** és **szűrés tervre**; a kész feladatok összecsukható, lapozható listában.
- Azonnali visszajelzés (optimista frissítés): a változás rögtön látszik, a háttérben szinkronizál; hiba esetén visszaáll.
- **Offline**: internet nélkül az utolsó letöltött állapotot mutatja, és magától újrapróbálja.
- **Napi értesítés** (reggel 7 után az első frissítéskor) a mai és lejárt feladatokról.

**Munkaidő (jelenléti ív)**
- „Munka kezdete / vége” gomb: az érkezést, távozást, óraszámot, aláírást és a havi összesítést beírja a PDF-be
  (a legközelebbi egész órára kerekítve, mint eddig). Az eltelt idő a gombon / fejlécben látszik.
- A mezőket automatikusan felismeri (vagy kézzel, kereshető listából választhatod ki).
- **Hónapváltáskor magától megkeresi az új havi ívet** (pl. `2026. március\…_március.pdf` → `2026. október\…_október.pdf`).
- A kitöltött mezők pontosan úgy néznek ki, mintha Acrobatban gépelted volna (12 pt Helvetica, középre igazítva).
- Ha a PDF nyitva van egy másik programban, szól, és a munkaidő tovább fut, amíg újra nem próbálod.

**Ablak és rendszer**
- Keret nélküli, akril hátterű widget a jobb felső sarokban; nem foglal helyet a tálcán és az Alt+Tab-ban.
- Három méret: lenyitva / csak fejléc / oldalra csukva (kompakt). A pozíciót és az állapotot megjegyzi.
- **Tálcaikon** (értesítési terület): bal klikk = megjelenítés/elrejtés, jobb klikk = menü (frissítés, új feladat, munkaidő, beállítások, kilépés).
- **Gyorsbillentyű** (alapból `Alt+W`): bárhonnan előhozza, újra megnyomva elrejti.
- Egyszerre csak egy példány fut; a parancsikonra kattintva a meglévő ablak jön elő.
- Sötét / világos téma (vagy a Windows szerint), mindig legfelül, indítás a Windows-zal – mind a Beállításokban.

### Billentyűparancsok (a widgeten belül)

| Billentyű | Művelet |
|---|---|
| `F5` | Frissítés |
| `Ctrl+N` | Új feladat |
| `Ctrl+F` | Keresés |
| `Enter` (a címmezőben) | Feladat hozzáadása |
| `Esc` | Panel bezárása → keresés törlése → összecsukás |

## Bejelentkezés

Alapból a **böngészőben** jelentkezel be (ugyanazzal az Entra-alkalmazással, mint a régi verzió). A bejelentkezés DPAPI-val
titkosítva megmarad, nem kell újra beírni.

**Opcionális – egyszeri bejelentkezés a Windows-fiókkal (WAM):** a Beállítások → Fiók kapcsolóval. Ehhez az Azure Portalon az
alkalmazásregisztrációhoz (`34e2c374-…`) fel kell venni ezt az átirányítási címet
(*Authentication → Mobile and desktop applications*):

```
ms-appx-web://microsoft.aad.brokerplugin/34e2c374-eb9b-4a30-9f19-879117b91660
```

Ha a cím hiányzik, a program magától visszavált böngészős módra.

## Adatok helye

Minden állapot a `%LOCALAPPDATA%\PlannerWidget` mappában van (Beállítások → *Adatmappa megnyitása*):

| Fájl | Tartalom |
|---|---|
| `settings.json` | Beállítások, ablakpozíció, jelenléti ív útvonala és mezői, futó munkaidő |
| `task-cache.json`, `plan-titles.json` | Utolsó letöltött feladatlista (offline indításhoz), tervnevek |
| `msal-*.cache` | Bejelentkezési tokenek (DPAPI-val titkosítva, csak a te Windows-fiókoddal olvasható) |
| `logs\app.log` | Napló hibakereséshez |
| `demo\` | A bemutató mód külön adatai |

A fordítási kimenet szándékosan **nem** a OneDrive-ra kerül, hanem ide: `%LOCALAPPDATA%\PlannerWidget.Build`.

## Hibaelhárítás

- **Nem indul / azonnal bezárul:** nézd meg a `logs\app.log`-ot.
- **„Alt+W foglalt”:** egy másik program (pl. a régi Python-widget) használja. Zárd be, vagy válassz másik gyorsbillentyűt.
- **„A PDF nyitva van egy másik programban”:** zárd be az ívet az Acrobatban/Edge-ben, és nyomd meg újra a „Munka vége” gombot.
- **Rossz mezőkbe ír:** Beállítások → Jelenléti ív → *Mezők újrabeállítása*.
- **Teljes visszaállítás:** `scripts\uninstall.ps1 -RemoveData`, majd újratelepítés.

## Felépítés (fejlesztőknek)

```
Újplannerc/
├─ Bemutató.cmd, Indítás.cmd, Telepítés.cmd   dupla kattintásos indítók
├─ UTMUTATO.md + docs/kepek/      képes útmutató
├─ PlannerWidget.sln              megoldásfájl (Visual Studio / VS Code)
├─ src/PlannerWidget.Core/        UI-független logika (net10.0) – tesztelhető
│  ├─ Graph/                      Planner REST kliens (lapozás, újrapróbálás, ETag-ütközés kezelése)
│  ├─ Tasks/                      Rendezés, határidő-állapotok, PlannerService (gyorsítótárak)
│  ├─ Attendance/                 Jelenléti ív: mezőfelismerés, sablonok, PDF-kitöltés (PDFsharp)
│  ├─ Settings/                   Beállítások + a régi verzió importja
│  └─ Storage/, Logging/          Fájlhelyek, atomikus JSON-mentés, napló
├─ src/PlannerWidget.App/         WinUI 3 alkalmazás (MVVM, CommunityToolkit.Mvvm)
│  ├─ MainWindow.xaml             A widget
│  ├─ SettingsWindow.xaml         Beállítások
│  ├─ ViewModels/                 MainViewModel (+ .Work.cs: munkaidő), TaskItemViewModel, SettingsViewModel
│  ├─ Services/                   Bejelentkezés (MSAL), tálca + gyorsbillentyű (Win32), párbeszédek, demó adatok
│  └─ Interop/                    P/Invoke deklarációk
├─ tests/PlannerWidget.Core.Tests xUnit tesztek
└─ scripts/                       run / test / install / uninstall
```

Technológia: .NET 10, WinUI 3 (Windows App SDK 1.8, csomagolás nélküli, önálló), MSAL.NET, PDFsharp 6, xUnit.

## Változások a Python-verzióhoz képest

- Javítva: 10-e után a napi óraszám nem került be a PDF-be (a régi mezősablon `600220{day:02d}` volt, a helyes `6002200{day}`) –
  a régi beállítás importkor automatikusan kijavul.
- Javítva: a határidő egy napot csúszhatott (UTC → helyi idő átváltás hiányzott).
- Javítva: az állapotfájlok a munkakönyvtártól függtek; a felület lefagyott hálózati műveletek közben;
  nem volt lapozás a Graph-válaszoknál; törlés megerősítés nélkül.
- Új: részletek és ellenőrzőlista, szerkesztett prioritás, keresés/szűrés, offline mód, értesítések, tálcaikon-menü,
  hónapváltás-felismerés, bemutató mód, telepítő.
