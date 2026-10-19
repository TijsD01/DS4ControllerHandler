namespace DS4Handler.App.ViewModels;

/// <summary>
/// All fixed tooltip texts in one place (Dutch, plain language, for gamers without technical
/// background). Used from XAML with {x:Static vm:Tips.Name}. Tooltips that depend on live values are
/// built in the view models.
/// </summary>
public static class Tips
{
    // ----- Navigation / header -----
    public const string NavControllers =
        "Overzicht van je aangesloten controllers: live weergave van sticks en triggers en wat de app heeft geleerd.";
    public const string NavSettings =
        "Opstarten met Windows, systeemvak en HidHide (je echte controller verbergen voor games).";

    // ----- Controller card -----
    public const string ControllerName =
        "Het type PlayStation-controller. De app laat hem in games werken als een Xbox 360-controller.";
    public const string ControllerId =
        "Het unieke adres van deze controller. Daarop wordt zijn kalibratie bewaard, dus via USB en Bluetooth gebruikt hij hetzelfde profiel.";
    public const string ControllerIdUnstable =
        "Het adres van deze controller kon niet worden gelezen. Zijn kalibratie hangt daarom aan deze USB-poort: op een andere poort begint hij opnieuw met leren.";
    public const string Usb =
        "Verbonden met een USB-kabel. Dat geeft de snelste reactie en laadt de controller ondertussen op.";
    public const string Bluetooth =
        "Draadloos verbonden via Bluetooth. Merk je vertraging? Gebruik dan een kabel of zet de controller dichter bij je pc.";
    public const string ReportRate =
        "Hoe vaak per seconde de controller zijn standen doorgeeft. Hoger is vloeiender; 250 of meer is prima.";

    // ----- Live view -----
    public const string StickView =
        "Live weergave van de stick.\n" +
        "• Grijs rondje: waar de stick werkelijk staat.\n" +
        "• Blauwe stip: wat de game krijgt na correctie.\n" +
        "• Oranje cirkel: de deadzone rond het geleerde rustpunt.";
    public const string Deadzone =
        "Binnen deze cirkel wordt stickbeweging genegeerd, zodat drift je personage niet laat bewegen. De grootte wordt automatisch geleerd: zo klein als kan, zo groot als nodig.";
    public const string TriggerBar =
        "Bovenste balk: hoe ver je de trigger echt indrukt. Het oranje streepje is het geleerde eindpunt (jouw 100%).\n" +
        "Onderste balk: wat de game krijgt. Groen betekent volle kracht (100%).";
    public const string LegendRaw = "Grijs: de echte stand van de controller, zonder correctie.";
    public const string LegendOutput = "Blauw: wat de game krijgt, na de correctie voor drift en slijtage.";
    public const string LegendDeadzone = "Oranje: deadzone (sticks) of geleerd eindpunt (triggers).";

    // ----- Calibration -----
    public const string CalibrationSection =
        "Hier staat in gewone woorden wat de app over deze controller heeft geleerd. Dat gaat vanzelf tijdens het spelen en wordt per controller bewaard.";
    public const string Learning =
        "Aan: de app blijft tijdens het spelen bijleren hoe deze controller zich gedraagt (drift, versleten triggers).\n" +
        "Uit: wat al geleerd is blijft gewoon werken, maar er verandert niets meer.";
    public const string JitterFilter =
        "Aan: trillen van een stick wordt weggefilterd. De app meet zelf hoeveel een stick trilt als je hem stilhoudt " +
        "(los of op een vaste stand) en filtert precies zoveel als nodig. Een goede stick merkt er niets van.\n" +
        "Snelle bewegingen gaan er zonder merkbare vertraging doorheen; alleen stilhouden en heel langzaam richten wordt rustiger.\n" +
        "Uit: de stick gaat ongefilterd naar de game, net als vroeger.";
    public const string HideFromGames =
        "Verbergt je echte PlayStation-controller voor games, zodat ze alleen de Xbox-controller van deze app zien. Dat voorkomt dubbele input.\n" +
        "Werkt via HidHide. Een game die al draait moet je opnieuw starten (of sluit de controller opnieuw aan).";
    public const string HideUnavailable =
        "Verbergen werkt via het gratis programma HidHide. Zet HidHide aan bij Instellingen, of installeer het eerst.";
    public const string Output =
        "Als welk soort controller games deze controller zien.";
    public const string OutputXbox =
        "Xbox 360: werkt in vrijwel elke pc-game. Kies dit als je twijfelt. Games tonen dan Xbox-knoppen (A, B, X, Y).";
    public const string OutputDs4 =
        "DualShock 4: voor games die PlayStation-controllers zelf ondersteunen (bijvoorbeeld via de PlayStation-ondersteuning van Steam); die tonen dan PlayStation-knoppen.\n" +
        "Let op: bewegingssensor en touchpad worden (nog) niet doorgegeven. Zet 'Verberg voor games' aan, zodat games alleen de virtuele DualShock 4 zien en niet ook je echte controller.";
    public const string Reset =
        "Vergeet alles wat voor deze controller is geleerd en begin opnieuw met fabriekswaarden. Handig na een reparatie of als de correctie niet meer klopt.";
    public const string Recenter =
        "Gebruik dit als je stick ineens veel meer drift heeft dan eerst. Laat daarna beide sticks los; het nieuwe rustpunt wordt direct gemeten.\n" +
        "Gewone drift wordt ook vanzelf geleerd, daarvoor hoef je niets te doen.";
    public const string CancelGuided = "Stopt de assistent. Wat al geleerd was blijft staan.";
    public const string CalibrateTrigger =
        "De snelste manier om een trigger te repareren die niet meer tot 100% komt: druk hem een paar keer helemaal in en dat punt wordt je nieuwe 100%.\n" +
        "Automatisch leren werkt ook, maar heeft meer drukken nodig.";
    public const string StickSummary =
        "Wat er voor deze stick is geleerd: waar hij in rust staat (drift), hoe groot de deadzone is en of hij in alle richtingen de rand haalt.";
    public const string TriggerSummary =
        "Wat er voor deze trigger is geleerd: tot hoever hij nog komt en of dat naar 100% wordt opgeschaald.";

    // ----- Empty state -----
    public const string EmptyState =
        "Zodra je een DualShock 4 of DualSense aansluit verschijnt hij hier vanzelf. Bluetooth? Koppel de controller eerst in de Windows-instellingen (houd PS + Share ingedrukt tot het lampje knippert).";

    // ----- Settings -----
    public const string StartWithWindows =
        "Start DS4 Handler automatisch als je inlogt, zodat je controller meteen werkt in games.";
    public const string StartMinimized =
        "Start zonder venster, alleen met een icoon in het systeemvak (rechtsonder bij de klok). Dubbelklik op dat icoon om het venster te openen.";
    public const string HidHideEnabled =
        "Gebruik HidHide om je echte controllers te verbergen voor games. Zonder verbergen zien sommige games twee controllers en reageren knoppen dubbel.\n" +
        "DS4 Handler zet zichzelf op de uitzonderingslijst, zodat de app de controller zelf wel blijft zien.\n" +
        "Let op: hierbij gaat ook de algemene verberg-schakelaar van HidHide aan. Die geldt ook voor apparaten die je eerder zelf in HidHide hebt verborgen.";
    public const string UnhideOnExit =
        "Aan: als je DS4 Handler afsluit, zien games en andere programma's (zoals Steam of een emulator) je controller weer gewoon.\n" +
        "Uit: de controller blijft ook na afsluiten verborgen. Handig als de app altijd draait, maar zonder de app werkt hij dan in games niet.";
    public const string RecheckHidHide =
        "Kijk opnieuw of HidHide beschikbaar is, bijvoorbeeld nadat je het hebt geïnstalleerd of de HidHide-configuratie hebt gesloten.";
    public const string DownloadHidHide = "Opent de downloadpagina van HidHide (gratis, van dezelfde maker als ViGEmBus).";
    public const string DownloadViGEm =
        "Opent de downloadpagina van ViGEmBus, de driver die de virtuele Xbox-controller maakt. Start de app na installatie opnieuw.";
    public const string OpenProfiles = "Opent de map waarin per controller de geleerde kalibratie staat.";
    public const string CheckForUpdates =
        "Kijkt bij het opstarten (en daarna twee keer per dag) op GitHub of er een nieuwe versie van DS4 Handler is. Je ziet dan bovenin wat er nieuw is en kunt met één klik bijwerken. Er wordt nooit iets geïnstalleerd zonder dat je erop klikt.";
    public const string CheckNow = "Kijk nu meteen of er een nieuwe versie is.";
    public const string UpdateNow =
        "Downloadt de nieuwe versie en installeert die over de oude heen. De app sluit even en start daarna vanzelf opnieuw. Je profielen, kalibratie en instellingen blijven bewaard.";
    public const string UpdateLater = "Verberg deze melding tot de volgende keer dat de app start.";
    public const string UpdateDownloadPage = "Opent de downloadpagina van de nieuwe versie op GitHub.";
    public const string OpenLog = "Opent het logbestand. Handig om mee te sturen als iets niet werkt.";
    public const string Quit = "Sluit DS4 Handler helemaal af. De virtuele Xbox-controllers verdwijnen dan.";

    // ----- Header chips -----
    public const string DriverReady = "ViGEmBus is geïnstalleerd: games zien je controller als een Xbox 360-controller.";
    public const string DriverMissing = "ViGEmBus ontbreekt. Zonder deze driver kan de app geen Xbox-controller maken en krijgen games geen input.";
    public const string DriverChecking = "Bezig met controleren of de Xbox-driver (ViGEmBus) aanwezig is.";

    // ----- Profielen -----
    public const string NavProfiles =
        "Alle controllers die deze pc kent, met hun geleerde kalibratie. Geef ze een naam, deel of kopieer een profiel, of neem het profiel van een vriend mee.";
    public const string ProfilesIntro =
        "Elke controller krijgt een eigen profiel, herkend aan zijn MAC-adres. Via USB en Bluetooth gebruikt hij dus hetzelfde profiel.";
    public const string ProfileName =
        "Een naam die je zelf kiest, bijvoorbeeld 'Controller van Sem'. Leeg laten toont het type controller. De naam verandert niets aan de kalibratie.";
    public const string ProfileRename = "Geef dit profiel een andere naam. Druk op Enter of klik ergens anders om op te slaan.";
    public const string ProfileMac =
        "Het MAC-adres: het unieke adres van deze controller. Hieraan herkent de app hem, via USB en via Bluetooth.";
    public const string ProfileNoMac =
        "Van deze controller kon geen MAC-adres worden gelezen. Het profiel hangt daarom aan de USB-poort waarop hij zat.";
    public const string ProfileModel = "Het type controller van dit profiel.";
    public const string ProfileConnected = "Deze controller is nu aangesloten.";
    public const string ProfileLastSeen = "Wanneer deze controller voor het laatst met deze pc verbonden was.";
    public const string ProfileSummary =
        "Kort wat er voor deze controller is geleerd: tot hoever de triggers komen en of de sticks drift hebben.";
    public const string ProfileUsedBy =
        "Andere aangesloten controllers die dit profiel delen: ze leren samen in dezelfde kalibratie.";
    public const string ProfileCopy =
        "Kopieer wat voor deze controller is geleerd naar een andere controller. Wat daar al geleerd was, wordt overschreven.";
    public const string ProfileCopyNone =
        "Kopiëren kan pas als er voor dit profiel iets is geleerd en er nog een ander profiel is.";
    public const string ProfileExport =
        "Sla dit profiel op als bestand, bijvoorbeeld om het aan een vriend te geven of als reservekopie.";
    public const string ProfileImport =
        "Laad een profielbestand, bijvoorbeeld dat van een vriend. Het wordt gekoppeld aan zijn controller (MAC-adres): sluit die aan en hij werkt meteen met zijn eigen kalibratie.";
    public const string ProfileDelete =
        "Verwijder dit profiel en zijn naam. Sluit je de controller later weer aan, dan begint hij opnieuw met leren.";
    public const string ProfileDeleteBlocked =
        "Een profiel dat een aangesloten controller gebruikt kan niet worden verwijderd. Koppel de controller eerst los of kies voor hem een ander profiel.";
    public const string ProfileChoose =
        "Kies in welk profiel deze controller leert. Standaard zijn eigen profiel; je kunt hem ook laten delen met het profiel van een andere controller van hetzelfde type.\n" +
        "Twee aangesloten controllers kunnen niet tegelijk hetzelfde profiel gebruiken.";
    public const string LinkOwn = "De controller leert in zijn eigen profiel (aanbevolen).";
    public const string LinkShared = "De controller gebruikt en verbetert de kalibratie van dit andere profiel.";
    public const string ProfileOnCard =
        "Het profiel waarin deze controller leert. Wijzigen kan bij het tabblad Profielen.";
    public const string ProfileBlockedOnCard =
        "Het profiel van deze controller wordt al gebruikt door een andere aangesloten controller. Om te voorkomen dat beide hetzelfde bestand overschrijven, draait deze controller tijdelijk met fabriekswaarden en wordt er niets opgeslagen. Kies bij Profielen een ander profiel of sluit hem later opnieuw aan.";
    public const string OpenProfileFolder = "Opent de map waarin de profielbestanden staan.";

    // ----- Afstellen -----
    public const string TuneToggle =
        "Stel zelf af hoe sticks en triggers reageren: extra deadzone, vorm, gevoeligheid en hoe snel de app meet en doorgeeft. Alles staat standaard op 'niets veranderen'.";
    public const string TuneSection =
        "Deze instellingen komen bovenop wat de app zelf heeft geleerd. Ze worden in het profiel van de controller bewaard.";
    public const string TuneResetAll = "Zet alle instellingen van Afstellen voor deze controller terug naar standaard. Wat de app heeft geleerd blijft staan.";
    public const string TuneResetSection = "Zet alleen dit onderdeel terug naar standaard.";
    public const string TuneShape =
        "De vorm van de extra deadzone rond het midden van de stick.";
    public const string TuneShapeRadial =
        "Rond: de stick moet eerst uit een cirkel om het midden voordat er iets gebeurt. Voelt in alle richtingen hetzelfde. Dit is de normale keuze.";
    public const string TuneShapeSquare =
        "Vierkant: de stick telt pas als je op minstens één as ver genoeg duwt; kleine schuine bewegingen rond het midden worden genegeerd. Veel Rocket League-spelers gebruiken dit voor rustig en recht sturen.";
    public const string TuneShapeAxial =
        "Kruis: elke richting (links-rechts en omhoog-omlaag) heeft zijn eigen deadzone, zodat een klein zijwaarts wobbeltje niet doorlekt in een rechte duw. Ook anti-deadzone en gevoeligheid werken dan per as. Diagonalen voelen wel wat hoekiger.";
    public const string TuneInner =
        "Extra deadzone: de stick moet eerst zo ver bewegen voordat de game iets merkt. Komt bovenop de deadzone die de app zelf heeft geleerd. Handig als je personage of camera nog licht beweegt zonder dat je iets doet. Bij Vierkant en Kruis kan hij minder groot, en hoe hoger het eindpunt, hoe lager het maximum, zodat je altijd een werkend bereik overhoudt.";
    public const string TuneOuter =
        "Eindpunt: de stick geeft al volle kracht voordat hij tegen de rand zit. Handig als de rand van de stick moeilijk te halen is, bijvoorbeeld door een dikke stickdop of een versleten stick.";
    public const string TuneAnti =
        "Anti-deadzone: zodra de stick de deadzone verlaat springt de uitvoer meteen naar minstens dit percentage. Gebruik dit als de game zelf een deadzone heeft: dan reageert je stick meteen, in plaats van dat je eerst een stuk moet duwen. Begin klein, ongeveer 10%. Bij Kruis geldt dit per as.";
    public const string TuneTriggerAnti =
        "Anti-deadzone: zodra je de trigger voorbij zijn deadzone indrukt, springt de uitvoer meteen naar minstens dit percentage. Handig als de game zelf pas reageert na een stukje indrukken. Begin klein, ongeveer 10%.";
    public const string TuneCurve =
        "Curve: hoe de stick reageert tussen midden en rand. Lineair is gewoon 1 op 1. Hoger dan 1 is rustiger rond het midden (precies richten) en heftiger aan de rand. Lager dan 1 is juist snel bij kleine bewegingen.";
    public const string TuneSensitivity =
        "Gevoeligheid: vermenigvuldigt de uitslag van de stick. Boven 1× haal je volle uitslag al voordat de stick de rand raakt; onder 1× haal je nooit helemaal vol. De uitkomst gaat nooit boven 100%. Bij Kruis werkt dit per as.";
    public const string TuneSquareOutput =
        "Vierkante uitvoer: schuin duwen geeft op beide assen 100%, in plaats van maximaal ongeveer 70% per as. Handig voor games waar je diagonaal volle snelheid wilt. Zet dit niet aan als een game zelf al een vierkant bereik gebruikt.";
    public const string TuneInvertX = "Draait links en rechts om voor deze stick.";
    public const string TuneInvertY = "Draait omhoog en omlaag om voor deze stick. Handig voor vliegen of de camera 'omgekeerd'.";
    public const string TuneTriggerInner =
        "Extra deadzone: de trigger moet eerst zo ver worden ingedrukt voordat de game iets merkt. Handig als een trigger al licht reageert terwijl je hem loslaat.";
    public const string TuneTriggerOuter =
        "Eindpunt: de trigger geeft al volle kracht voordat hij helemaal is ingedrukt. Handig voor sneller gas geven of schieten.";
    public const string TunePoll =
        "Hoe vaak per seconde de controller zijn standen doorgeeft. Hoger geeft minder vertraging, maar kost meer batterij. 250 of meer is prima voor de meeste games.\n" +
        "Alleen te kiezen voor een DualShock 4 via Bluetooth. Via USB bepaalt de controller zelf de snelheid.";
    public const string TunePollUnavailable =
        "Dit kan alleen voor een DualShock 4 via Bluetooth. Via USB, en bij een DualSense, bepaalt de controller zelf hoe vaak hij meet.";
    public const string TuneOutputRate =
        "Hoe vaak per seconde de virtuele controller wordt bijgewerkt voor games.\n" +
        "'Elke meting' is het snelst en voor bijna iedereen goed. Beperken is alleen nodig bij een trage pc of een game die zich verslikt in heel veel updates.";
}
