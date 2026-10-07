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
| `scripts\run.ps1 [-Demo] [-StaleHeartbeat]` | Lefordítja (Debug) és elindítja (`-StaleHeartbeat`: bemutató módban leállt szinkron-életjel) |
| `scripts\test.ps1 [-Pdf <útvonal>]` | Automatikus tesztek; `-Pdf`-fel a PDF-kitöltést is teszteli egy **másolaton** |
| `scripts\install.ps1 [-Desktop] [-Autostart] [-SelfContained]` | Kiadási változat telepítése ide: `%LOCALAPPDATA%\Programs\PlannerWidget` |
| `scripts\uninstall.ps1 [-RemoveData]` | Eltávolítás (a `-RemoveData` a beállításokat és a bejelentkezést is törli) |
| `scripts\package.ps1` | **Kiosztható ZIP kollégáknak** (önálló program + fordítás nélküli telepítő), lásd lent |

Követelmény: Windows 10 (2004+) vagy 11, .NET 10 SDK (a fejlesztéshez; a telepített változathoz elég a .NET 10 futtatókörnyezet,
`-SelfContained` esetén az sem kell).

## Mit tud

**Feladatok**
- A neked kiosztott Planner-feladatok, határidő szerint rendezve: lejárt → mai/7 napon belüli → később → határidő nélkül.
- Színsáv a kártya szélén (piros: lejárt, sárga: 7 napon belül, zöld: később), relatív határidő („Ma”, „Holnap”, „2 napja lejárt”).
- Prioritás, terv neve, ellenőrzőlista-állapot (pl. 2/4) és leírás-jelzés a kártyán.
- Kártyára kattintva kinyílnak a **részletek**: leírás és **kipipálható ellenőrzőlista**.
- **Saját sorrend húzással:** az aktív feladatok fel-le húzhatók. Egy szabály van: lejárt (piros) feladat nem kerülhet
  határidő nélküli alá (és fordítva) – ilyenkor a legközelebbi megengedett helyre ugrik. Az új feladatok a határidejük
  szerinti helyre kerülnek. A sorrend ezen a gépen marad meg (`task-order.json`); **⋯ → Sorrend visszaállítása** = vissza
  a határidő szerintire.
- **Ellenőrzőlista szövegének szerkesztése** (ceruza a részleteknél): legfeljebb 100 karakter (a Planner korlátja),
  élő számlálóval („37/100”); `Enter` = mentés, `Esc` = mégse.
- Kész / visszanyitás egy kattintással (hanggal), **szerkesztés** (cím, határidő, prioritás), **törlés megerősítéssel**,
  megnyitás a Planner weben.
- **Új feladat**: terv, oszlop (az utoljára használtat tervenként megjegyzi), határidő, prioritás.
- **Keresés** és **szűrés tervre**; a kész feladatok összecsukható, lapozható listában.
- Azonnali visszajelzés (optimista frissítés): a változás rögtön látszik, a háttérben szinkronizál; hiba esetén visszaáll.
- **Offline**: internet nélkül az utolsó letöltött állapotot mutatja, és magától újrapróbálja.
- **Napi értesítés** (reggel 7 után az első frissítéskor) a mai és lejárt feladatokról.

**Beérkező ügyek (SharePoint-ticketek)**
- Az MFI-RFK „Beérkező ügyek” listájának ticketjeit Power Automate flow-k teszik ki Planner-feladatként egy külön tervbe
  („[#123] Tárgy” címmel, a Felelős mezőben szereplőknek kiosztva). A widget ezeket a feladatokat **ügyként** jelöli:
  lila „Ügy #123” címke a kártyán, „N ügy” a fejlécben, és a ⋯ menüben **Ticket megnyitása** (a SharePoint-űrlap).
- **Ügyterv:** alapból az MFÜI - RFK csoport **„Ügyek”** terve (`LWXZVkNKt0GrJU8T6PDftpYAEcV0`) – új telepítésnél és régi
  beállításoknál is. Beállítások → *Beérkező ügyek* → *Ügyterv*: másik terv vagy **„Nincs”** (ezt a választást megjegyzi,
  újraindítás után sem állítja vissza; ilyenkor a widget pontosan úgy működik, mint az ügyek előtt). A link sablonja
  ugyanitt módosítható (`{id}` helyőrző).
- **Lezárás:** egy ügy késznek jelölése után egy flow a ticket Státuszát „Done”-ra állítja, egy másik pedig archiválja a hozzá
  tartozó e-maileket – ez gyakorlatilag nem vonható vissza, ezért a widget előtte rákérdez (kikapcsolható).
  A **visszanyitás nem jut vissza** a SharePointba: a ticket lezárva marad, erről a widget figyelmeztet.
- **Értesítés új ügyről:** frissítéskor, ha az ügytervben új, nyitott feladat jelenik meg (az első betöltéskor csak megjegyzi
  a meglévőket). Amiről már szólt, arról újraindítás után sem szól újra.
- **Felelős nélküli ticket:** a flow ilyenkor törli a Planner-feladatot, és ha a ticket újra kap felelőst, új feladatként
  hozza létre. A widget a törölt ügyet egyszerűen kiveszi a listából; ha épp azzal dolgoztál (pl. lezárnád), hiba helyett
  tájékoztat (és jelzi, hogy a ticket nem zárult le). Ha ugyanaz a #ID **24 órán belül** tér vissza, nem jön róla
  „Új ügy” értesítés (pl. csak átírták a Felelős mezőt); később visszatérve már igen.
- **Ügykártya és állapotváltás:** az ügy csempéje halványan lilás, a cím lila, alatta kicsiben a „#123”. Az ügyterv
  gyűjtői (bucketjei) = a lista státuszai (New, Processed, SOS, In progress, KÉRDÉSES, Done, Revisit; üres/ismeretlen
  gyűjtő = New), a kártyán a státusz színes címkéje. **Előre a karikával:** a bal oldali karika egy menüt nyit –
  *Átvettem → Processed* (New-ból), *Elkezdem → In progress* (Processed/SOS/KÉRDÉSES-ből, +50% készültség),
  *Folytatom → In progress* (Revisit-ből, +50%), *Kész – lezárás* (In progress- vagy Revisit-ből: Done gyűjtő + 100% egyetlen
  PATCH-ben). **A „…” menü:** SOS, KÉRDÉSES, Vissza New-ba (az aktuális tiltva; Revisit és Done kézzel nem választható).
  **Revisit = „Új válasz érkezett”:** ha egy Done ügyre külsős válasz jön, a folyamat újranyitja (100% → 0%) és Revisit-be
  teszi – a widget újra megjeleníti, jelöli, és értesít („Új válasz érkezett: [#ID] …”). Revisit-ből a *Folytatom* mellett
  közvetlenül *Kész* is választható (ha a válasz után nincs teendő).
  A widget csak a feladat gyűjtőjét és készültségét módosítja (PATCH, If-Match, 412-nél egy újrapróba); a listát egy
  folyamat hétköznap 7–19 között néhány perc (legfeljebb kb. 10 perc) késéssel követi. Ügyet csak *In progress* vagy *Revisit*
  állapotban lehet késznek jelölni (előtte a karika menüjében a „Kész” halvány).
- **Felelőst csak a Beérkező ügyek listában** lehet módosítani (a Plannerben levettet a folyamat visszaállítja). Ezért
  ügyet a widgetből nem lehet törölni: a ⋯ menüben a *Törlés* helyett **Felelős módosítása…** van, ami ezt elmagyarázza
  és megnyitja a ticketet. Új feladatot sem lehet az ügytervbe felvenni (ügyet a folyamat hoz létre), ezért az ügyterv
  nem szerepel az új feladat tervlistájában.
- A widget csak a **neked kiosztott** ügyeket látja, és nem kér új jogosultságot: a SharePointot közvetlenül nem éri el.
- **Szinkronhibák:** a Power Automate-folyamatok a hibáikat a Microsoft To Do **„Szinkronhibák”** listájába írják
  („HIBA | <folyamat neve>” címmel, a leírásban a hibás futás linkjével). Ha van nyitott hiba, a fejlécben egy piros
  **⚠ N** jelzés jelenik meg (N = a hibák száma); rákattintva egy lista mutatja őket (folyamat, időpont, az azonos
  hibák összevonva „2×”). Soronként: **Megnyitás** (a hibás futás a böngészőben) és **Megoldva** (a To Do-feladat
  lezárása – a csoport összes bejegyzése). Ha nincs hiba, a jelzés nem látszik. A **„Szinkron életjel”** feladatot a
  folyamat 5 percenként frissíti (6:00–21:00); ha 6:20 és 21:00 között 20 percnél régebben frissült, narancs
  figyelmeztetés: „A szinkron nem fut (utolsó: HH:mm) – ellenőrizd a Power Automate-et”. Az életjelet a widget soha
  nem zárja le és nem mutatja hibaként. A lista a Planner-frissítéssel együtt frissül; hálózati hibánál nem szól.
  Ehhez sem kell új jogosultság (a meglévő Tasks.ReadWrite elég); ha nincs ilyen lista, nem jelenik meg semmi.

- **Havi összegzés (Wrapped):** a profilképre kattintva nyílik (◀ ▶ a korábbi hónapokhoz): hány feladat és ügy érkezett és
  mennyi lett kész, a leghosszabb ideje nyitott feladat, a leggyorsabban elintézett, a legtermékenyebb nap. A hónap első
  frissítésekor elkészül az előző hónapé, és a profilkép monogramja helyett „W” látszik piros körrel, amíg meg nem nyitod.
  A feladatokat a widget maga jegyzi fel (`wrapped.json`), így csak a használata óta látott adatokból tud számolni.

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
- **Elmosott (akril) háttér** külön kapcsolható használat közben és arra az esetre, ha máshová kattintasz
  (alapból: használat közben elmosott, máshová kattintva egyszínű – mint eddig).

### Billentyűparancsok (a widgeten belül)

| Billentyű | Művelet |
|---|---|
| `F5` | Frissítés |
| `Ctrl+N` | Új feladat |
| `Ctrl+F` | Keresés |
| `Enter` (a címmezőben) | Feladat hozzáadása |
| `Esc` | Panel bezárása → keresés törlése → összecsukás |

## Kiosztás kollégáknak

A `Telepítés.cmd` / `install.ps1` **forráskódból fordít** (.NET 10 SDK és a teljes mappa kell hozzá) – ez a saját gépre
való. Kollégáknak csomagot kell készíteni:

1. Itt, a fejlesztői gépen: `scripts\package.ps1` → `%LOCALAPPDATA%\PlannerWidget.Build\dist\PlannerWidget-<verzió>-win-x64.zip`
   (kb. 80 MB; önálló: a .NET és a Windows App SDK is benne van; a verzió a `Directory.Build.props`-ból jön).
2. A ZIP-et oszd meg (Teams / OneDrive), mellé a [docs/TELEPITES_KOLLEGAKNAK.md](docs/TELEPITES_KOLLEGAKNAK.md) útmutatót
   (ez a ZIP-ben is benne van a képeivel).
3. A kolléga kibontja, és duplán kattint a `Telepites.cmd`-re: `%LOCALAPPDATA%\Programs\PlannerWidget` alá telepít,
   Start menü + asztali parancsikon, rendszergazdai jog nem kell. Frissítés: új ZIP, ugyanígy (a beállítások megmaradnak).

A kollégák gépén **semmi más nem kell** (se .NET, se NuGet). Amire figyelni kell:

- **Entra-alkalmazás (`34e2c374-…`):** egybérlős, a böngészős átirányítás (`http://localhost`) már be van állítva. Ha a
  bérlő nem engedi, hogy a felhasználók maguk adjanak hozzájárulást (`User.Read`, `Tasks.ReadWrite`), az első
  bejelentkezéskor rendszergazdai jóváhagyást kér – ilyenkor egy Entra-adminnak egyszer meg kell adnia a szervezeti
  szintű hozzájárulást. Ha a vállalati alkalmazásnál be van kapcsolva a *hozzárendelés kötelező*, a kollégákat hozzá
  kell rendelni.
- **Aláírás:** a program nincs digitálisan aláírva → SmartScreen-figyelmeztetés lehet („További információ → Futtatás
  mindenképp”). A telepítő a letöltött fájlok blokkolását feloldja (`Unblock-File`), így később nem kérdez.
- **Ügyek:** mindenki csak a neki kiosztott ügyeket látja (a flow a Felelős mező alapján oszt ki).

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
| `task-order.json` | A húzással beállított feladatsorrend (üres = határidő szerinti) |
| `ticket-state.json` | Az ügytervben már látott feladatok (az „Új ügy” értesítéshez) |
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
│  ├─ Graph/                      Planner + To Do REST kliens (lapozás, újrapróbálás, ETag-ütközés kezelése)
│  ├─ Tasks/                      Rendezés, kézi sorrend (ManualOrder), határidő-állapotok, PlannerService
│  ├─ Attendance/                 Jelenléti ív: mezőfelismerés, sablonok, PDF-kitöltés (PDFsharp)
│  ├─ Tickets/                    Beérkező ügyek: ügyfelismerés („[#123]”), ticket-link, új ügyek követése
│  ├─ Sync/                       Szinkronhibák (To Do „Szinkronhibák” lista) és a szinkron életjel
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
