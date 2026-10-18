# DS4ControllerHandler

Een DS4Windows-achtige app voor Windows: je PlayStation-controller (DualShock 4 of DualSense) werkt in games als een Xbox 360-controller. Daarbovenop zit **zelflerende kalibratie** per controller. De app leert tijdens het spelen hoe jouw controller zich gedraagt en corrigeert dat:

- **Versleten triggers.** Komt je R2 bij volledig indrukken maar tot 200 van de 255, dan geeft de app na een paar keer vol indrukken toch 100% door. Handig voor Rocket League, waar je anders nooit op volle snelheid rijdt.
- **Stickdrift.** Het rustpunt van elke stick wordt bijgehouden. Een stick die in rust op (+0,12; -0,08) blijft hangen leest gewoon als nul, en de deadzone groeit alleen zo ver als nodig.
- **Kleiner stickbereik.** Haalt een stick in een richting de rand niet meer, dan wordt die richting bijgeschaald zodat je weer 100% haalt.

Wat geleerd is wordt per controller bewaard (op serienummer), dus de R2 van je vriend en jouw controller hebben elk hun eigen profiel.

## Hoe het leren werkt

Het is een klein online model dat bij elke input-update bijleert, zonder trainingsfase.

**Triggers** (`TriggerCalibrator`). Bij elke druk kijkt het model waar de trigger tot stilstand komt. Een mechanische aanslag is herhaalbaar: druk na druk eindigt op dezelfde waarde, terwijl halve drukken overal terechtkomen. Zodra minstens 3 recente drukken het eens zijn over een eindpunt (binnen 4 stappen), wordt dat het nieuwe 100%. Een eindpunt verlagen nadat het al geleerd is vraagt 5 overeenkomende drukken. Het gaat nooit onder de helft van het bereik, dus een foute gok kan lichte drukken niet als vol gas laten tellen. Ook de rustwaarde (een trigger die in rust op 12 blijft staan) wordt geleerd.

**Sticks** (`StickCalibrator`). Momenten waarop de stick vrijwel stil staat dicht bij het midden tellen als "losgelaten". Daaruit leert het model het echte midden, de ruis en hoe ver het rustpunt per keer verschilt. Daar wordt de deadzone op afgestemd. Per richting (16 sectoren) wordt de verste uitslag onthouden voor het bijschalen van de rand.

Leren kan per controller aan en uit, en "Kalibratie resetten" zet alles terug naar fabriekswaarden.

## Projectstructuur

| Map | Wat |
| --- | --- |
| `src/DS4Handler.Core` | Platformonafhankelijke kern: HID-rapporten parsen, kalibratie, profielen, Xbox-mapping |
| `src/DS4Handler.App` | Windows-app (WinForms, systeemvak): HID-invoer via HidSharp, virtuele Xbox 360-controller via ViGEmBus |
| `tests/DS4Handler.Core.Tests` | Unit tests met gesimuleerde controllers (versleten trigger, driftende stick) |

## Vereisten (Windows)

1. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
2. [ViGEmBus-driver](https://github.com/nefarius/ViGEmBus/releases), dezelfde driver die DS4Windows gebruikt.
3. Optioneel: [HidHide](https://github.com/nefarius/HidHide/releases). Zonder HidHide zien sommige games zowel je echte controller als de virtuele Xbox-controller, wat dubbele input geeft. Zet in HidHide de echte controller verborgen en `DS4Handler.App.exe` op de toegestane lijst.

## Bouwen en testen

```sh
dotnet test                       # kern + tests, werkt ook op Linux/macOS
dotnet build src/DS4Handler.App   # Windows-app
dotnet run --project src/DS4Handler.App   # alleen op Windows
```

Profielen staan in `%APPDATA%\DS4ControllerHandler\profiles`.

## Bekende beperkingen

- Alleen Xbox 360-uitvoer; DS4-emulatie, touchpad als muis, gyro en macro's uit DS4Windows zitten er nog niet in.
- Getest met unit tests en gesimuleerde input; nog niet op echte hardware.
