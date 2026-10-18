# DS4ControllerHandler

Een DS4Windows-achtige app voor Windows: je PlayStation-controller (DualShock 4 of DualSense) werkt in games als een Xbox 360-controller. Daarbovenop zit **zelflerende kalibratie** per controller. De app leert tijdens het spelen hoe jouw controller zich gedraagt en corrigeert dat:

- **Versleten triggers.** Komt je R2 bij volledig indrukken maar tot 200 van de 255, dan geeft de app na een paar keer vol indrukken toch 100% door. Handig voor Rocket League, waar je anders nooit op volle snelheid rijdt.
- **Stickdrift.** Het rustpunt van elke stick wordt bijgehouden. Een stick die in rust op (+0,12; -0,08) blijft hangen leest gewoon als nul, en de deadzone groeit alleen zo ver als nodig.
- **Kleiner stickbereik.** Haalt een stick in een richting de rand niet meer, dan wordt die richting bijgeschaald zodat je weer 100% haalt.

Wat geleerd is wordt per controller bewaard (op serienummer), dus de R2 van je vriend en jouw controller hebben elk hun eigen profiel.

## Downloaden

1. Ga naar **[Releases](https://github.com/TijsD01/DS4ControllerHandler/releases/latest)** en download `DS4Handler-Setup-<versie>.exe`.
2. Dubbelklik en volg de stappen. De installer zet ook **ViGEmBus** (nodig) en **HidHide** (aanbevolen) op je pc als die nog ontbreken; die vragen zelf om beheerdersrechten. .NET hoef je niet apart te installeren.
3. Start *DS4 Handler* via het Startmenu en sluit je controller aan.

Liever zonder installatie? Download dan `DS4Handler-<versie>-portable.exe`: één bestand dat je overal kunt neerzetten. ViGEmBus en HidHide moet je dan zelf installeren; ontbreekt er een, dan zegt de app dat bovenin met een downloadknop.

Windows SmartScreen kan "Onbekende uitgever" tonen, omdat de app niet digitaal ondertekend is. Klik dan op *Meer informatie* en *Toch uitvoeren*.

## Hoe het leren werkt

Het is een klein online model dat bij elke input-update bijleert, zonder trainingsfase.

**Triggers** (`TriggerCalibrator`). Bij elke druk kijkt het model waar de trigger tot stilstand komt en hoe hij daar aankomt. Een mechanische aanslag raak je hard en is herhaalbaar: druk na druk eindigt op precies dezelfde waarde. Een vastgehouden half gaspedaal komt rustig aan en wiebelt meer. Pas als genoeg harde, vlakke drukken het eens zijn over een eindpunt (5 heel vlakke of 8 iets rommeligere), wordt dat het nieuwe 100%. Het eindpunt komt nooit lager dan de hoogste stand die de trigger ooit heeft gehaald, en nooit onder 160 van de 255. Verder slijten wordt gevolgd in stapjes van maximaal 12. Ook de rustwaarde wordt geleerd, bijvoorbeeld een trigger die na loslaten op 12 blijft hangen, maar een licht vastgehouden trigger telt niet als rust.

**Sticks** (`StickCalibrator`). Elke keer dat je een stick na een flinke uitslag loslaat en hij een halve seconde stil staat, telt dat als één "loslating". De eerste 5 bepalen het midden (mediaan), daarna schuift elke loslating het midden een klein beetje bij. Een vastgehouden richtstand telt niet mee, omdat die te ver van het geleerde midden ligt. Landen 5 loslatingen op rij op een nieuwe plek, dan springt het midden daarheen: zo wordt drift die later ontstaat ook opgepikt. De deadzone groeit mee met de ruis en met hoe ver het rustpunt per keer verschilt. Per richting (16 sectoren) wordt de verste uitslag onthouden voor het bijschalen van de rand, en korte uitschieters tellen niet mee.

**Trilling** (`StickJitterFilter`). Ook terwijl je een stick ergens vasthoudt, meet de app hoeveel hij trilt. Hij kijkt steeds naar ongeveer een kwart seconde aan metingen en trekt daar een vloeiende curve door, zodat langzaam richten en het natuurlijke trillen van je duim niet als trilling tellen, maar een trage wiebel van een versleten potmeter wel. Dat leren gaat live tijdens het spelen: na ongeveer een seconde stilhouden is de trilling bekend, en ze wordt per controller bewaard. Daarmee wordt een aanpassend filter (One Euro-filter) ingesteld: bij stilhouden en langzaam richten wordt de stick rustig, en hoe sneller je beweegt, hoe minder er gefilterd wordt. Een flinke sprong telt meteen als echte beweging, dus een snelle flick komt binnen 1 à 2 metingen (4–8 ms) door en een draai van 4 keer per seconde loopt maar zo'n 2–3 ms achter. Bij stilhouden ligt er een heel klein meeschuivend dood gebied (ongeveer 0,3× de trilling) om de laatste stand, zodat heel langzaam richten niet plakt. Een gezonde stick (±1 telling) wordt nooit gefilterd; een stick die ±3 tellingen trilt, staat stilgehouden zo'n 4–5× rustiger. Ook bij een trillende stick wordt loslaten herkend, omdat de stilstand-test meegroeit met de geleerde trilling; drift wordt dus ook dan geleerd. Losgelaten blijft precies 0 en helemaal duwen precies 100%. Het filter staat per controller aan of uit met de schakelaar "Trillingsfilter".

**Kalibratie-assistent.** Voor een snelle correctie kun je ook "Kalibreer R2" kiezen en de trigger 3 keer helemaal indrukken, of "Sticks opnieuw centreren" en de sticks loslaten. Een met de assistent geleerd eindpunt wordt daarna nooit automatisch verlaagd.

Leren kan per controller aan en uit, en "Kalibratie resetten" zet alles terug naar fabriekswaarden.

## De Windows-app

Het venster heeft drie tabbladen: **Controllers**, **Profielen** en **Instellingen**. Over elke knop, schakelaar en waarde geeft een tooltip (muis erboven houden) uitleg in gewone taal.

- **Een kaart per controller** met type, MAC-adres, USB/Bluetooth, batterij, updates per seconde en een statuslabel ("Actief als Xbox-controller" / "Actief als DualShock 4-controller").
- **Live weergave.** Per stick een cirkel met de echte stand (grijs), wat de game krijgt (blauw) en de geleerde deadzone (oranje). Per trigger een balk met de echte stand en het geleerde eindpunt, en daaronder wat de game krijgt (groen = 100%).
- **Kalibratie in gewone woorden**, bijvoorbeeld "R2 komt tot 78% — wordt opgeschaald naar 100%", inclusief of dat automatisch of met de assistent is geleerd. Knoppen *Kalibreer L2/R2* ("Druk R2 nog 3× helemaal in") en *Sticks opnieuw centreren* voor snelle handmatige correctie.
- **Per controller:** Leren aan/uit, Trillingsfilter aan/uit, Verberg voor games (HidHide), Uitvoer Xbox 360 of DualShock 4, Kalibratie resetten (met bevestiging).
- **Instellingen:** start met Windows, geminimaliseerd starten (systeemvak), HidHide gebruiken, controllers weer zichtbaar maken bij afsluiten, profielmap en logbestand openen.
- **Systeemvak:** Tonen / Leren / Afsluiten. Het venster sluiten laat de app doordraaien.

**HidHide.** Staat HidHide aan, dan zet de app `DS4Handler.exe` op de uitzonderingslijst en verbergt de aangesloten controllers, zodat games alleen de virtuele controller zien. Een game die al draaide moet je opnieuw starten (of de controller opnieuw aansluiten). Ontbreekt HidHide of is het bezet (HidHide-configuratie of DS4Windows open), dan zegt de app dat bovenin met een downloadlink. Start de app als `DS4Handler.exe`, niet via `dotnet DS4Handler.dll`: dotnet.exe wordt bewust niet op de uitzonderingslijst gezet.

**Profielen.** Een controller wordt herkend aan zijn MAC-adres (ook via USB), dus USB en Bluetooth delen één profiel. Het tabblad *Profielen* toont elke bekende controller met een zelfgekozen naam (bijvoorbeeld "Controller van Sem"; de naam verandert de bestandsnaam niet), het MAC-adres (of "(geen MAC gevonden)"), het type, "nu verbonden" of "laatst gezien" en kort wat er is geleerd. Per profiel: *Hernoemen*, *Kopiëren naar…* (kalibratie naar een andere controller), *Exporteren* / *Importeren* (JSON-bestand, zodat een vriend zijn profiel kan meenemen; het hoort bij zijn MAC-adres) en *Verwijderen* (alleen als het niet in gebruik is). Bij *Profiel kiezen* laat je een aangesloten controller in zijn eigen of in een gedeeld profiel van hetzelfde type leren; de kaart toont dan "Gebruikt profiel: …". Twee aangesloten controllers kunnen nooit tegelijk in hetzelfde profiel schrijven: zo'n keuze wordt geweigerd, en een controller waarvan het profiel al in gebruik is draait tijdelijk zonder opslaan. Profielen worden elke 30 seconden (alleen bij wijzigingen) en bij loskoppelen/afsluiten opgeslagen. Instellingen staan in `%APPDATA%\DS4ControllerHandler\settings.json`, het log in `log.txt` in dezelfde map.

**Opbouw.** `Engine/` (apparaten zoeken, hot-plug, één sessie per controller met een eigen leesdraad met hoge prioriteit, ViGEm-uitvoer, trillen) staat los van de UI (`ViewModels/`, `Views/`, `Controls/`). Het leespad alloceert niets per rapport en stuurt alleen wijzigingen naar de driver; de UI leest ongeveer 30× per seconde een momentopname, en 1× per seconde als het venster verborgen is.

## Projectstructuur

| Map | Wat |
| --- | --- |
| `src/DS4Handler.Core` | Platformonafhankelijke kern: HID-rapporten parsen, kalibratie, profielen, Xbox 360- en DS4-mapping |
| `src/DS4Handler.HidHide` | Koppeling met de HidHide-driver om de echte controller voor games te verbergen |
| `src/DS4Handler.App` | Windows-app (WPF, systeemvak): HID-invoer via HidSharp, virtuele Xbox 360- of DualShock 4-controller via ViGEmBus, HidHide-koppeling. Zie [De Windows-app](#de-windows-app) |
| `tests/DS4Handler.Core.Tests` | Unit tests met gesimuleerde controllers (versleten trigger, driftende stick) |

## Vereisten (Windows)

De installer uit [Downloaden](#downloaden) regelt dit allemaal. Bouw je de app zelf, dan heb je nodig:

1. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (niet nodig voor de downloads van de Releases-pagina, daar zit .NET al in)
2. [ViGEmBus-driver](https://github.com/nefarius/ViGEmBus/releases), dezelfde driver die DS4Windows gebruikt.
3. Aanbevolen: [HidHide](https://github.com/nefarius/HidHide/releases). Zonder HidHide zien sommige games zowel je echte controller als de virtuele controller, wat dubbele input geeft. De app regelt HidHide zelf (zie hieronder); beheerdersrechten zijn niet nodig, maar sluit de HidHide-configuratie zolang DS4 Handler draait.

## Bouwen en testen

```sh
dotnet test                       # kern + tests, werkt ook op Linux/macOS
dotnet build src/DS4Handler.App   # Windows-app
dotnet run --project src/DS4Handler.App   # alleen op Windows
```

Profielen staan in `%APPDATA%\DS4ControllerHandler\profiles`.

### Een nieuwe versie uitbrengen

```sh
git tag v1.0.0
git push origin v1.0.0
```

De workflow `Release` bouwt dan de installer (`installer/DS4Handler.iss`, Inno Setup) en de portable exe, en zet beide op de Releases-pagina. Een tag met een streepje, zoals `v1.1.0-beta1`, wordt een pre-release.

Maak je een release met de hand op GitHub (*Releases → Draft a new release*), dan zet dezelfde workflow de exe-bestanden er na een paar minuten bij, ook als de tag geen versienummer is. Het bestand `.iss` in de broncode-zip is alleen het recept voor de installer; de echte installer is `DS4Handler-Setup-<versie>.exe` bij de release. Via *Actions → Release → Run workflow* kun je de bestanden ook bouwen: vul een tag in om ze bij die release te zetten, of laat het leeg en ze staan als artifact bij die run.

## Bekende beperkingen

- Uitvoer als Xbox 360 of DualShock 4; de virtuele DualShock 4 krijgt nog geen gyro- of touchpaddata. Touchpad als muis en macro's uit DS4Windows zitten er nog niet in.
- Getest met unit tests en gesimuleerde input; nog niet op echte hardware.
