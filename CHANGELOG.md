# Wat is er nieuw

Bij elke nieuwe versie komt bovenaan een kopje `## <versie>` met wat er nieuw is, in gewone woorden.
De bovenste sectie komt automatisch in de release op GitHub en de app laat hem na het bijwerken één keer zien.

## 1.2.1

- Verwijderen maakt je controllers nu ook weer zichtbaar als de app voor alle gebruikers is geïnstalleerd: de lijst met verborgen controllers van elke gebruiker wordt daarvoor gelezen.
- Bij het verwijderen haalt de app zichzelf ook weer van de toegestane programma's van HidHide af, en zet HidHide weer uit als de app het zelf had aangezet en er niets anders meer verborgen is.
- Het verwijderprogramma vraagt een nog draaiende app eerst om af te sluiten en schrijft eerlijk in het logboek of het weer zichtbaar maken is gelukt.
- Een instellingenbestand dat niet te lezen is, kost je lijst met verborgen controllers niet meer: die wordt hersteld, er komt alleen een reservekopie als die echt gelukt is, en het bestand wordt niet overschreven als het alleen even vergrendeld is.
- De app start niet meer met een foutmelding als er al een kopie met beheerdersrechten draait.

## 1.2.0

- Nieuw ontwerp: rustiger en strakker, met dezelfde duidelijke kleuren. Je controllers staan links in een lijst; klik er een aan om hem te bekijken.
- Per controller zie je sticks, triggers en kalibratie overzichtelijk naast en onder elkaar. Getallen springen niet meer heen en weer.
- Meldingen (zoals een nieuwe versie) staan als smalle balk bovenin in plaats van als grote kaart.
- Profielen en Instellingen zijn opgeruimd tot overzichtelijke lijsten. De uitleg als je met de muis ergens op staat is overal gebleven.
- Nieuw tabblad "Afstellen" bij elke controller. Alles is per controller in te stellen en wordt in zijn profiel bewaard.
- Sticks: extra deadzone in de vorm van een cirkel, vierkant of kruis (bij het vierkant telt de stick pas als je op minstens één as ver genoeg duwt; veel Rocket League-spelers gebruiken dit, en bij het kruis heeft elke as zijn eigen deadzone), een eindpunt, een anti-deadzone tegen de eigen deadzone van een game, een curve, gevoeligheid, vierkante uitvoer en links/rechts of omhoog/omlaag omdraaien.
- Triggers: extra deadzone, eindpunt, anti-deadzone en curve voor L2 en R2.
- Snelheid: hoe vaak een DualShock 4 via Bluetooth zijn standen doorgeeft (van 62 tot 1000 per seconde) en hoe vaak de virtuele controller wordt bijgewerkt voor games.
- Elk onderdeel heeft een knop "Standaard", en er is een knop om alles terug te zetten. De kalibratie blijft daarbij staan.
- Je afstelling gaat mee met het profiel: bij een gekoppeld of geëxporteerd profiel, en blijft staan bij "Kalibratie resetten".
- Verwijder je de app, dan worden je controllers weer zichtbaar voor games. Voorheen bleven ze door HidHide verborgen.
- Kan het instellingenbestand niet worden gelezen, dan bewaart de app het als settings.json.bad in plaats van het te overschrijven.

## 1.1.0

- Nieuw tabblad Profielen: elke controller die je pc kent, met een eigen naam (bijvoorbeeld "Controller van Sem"), wanneer hij laatst verbonden was en wat er is geleerd.
- Profielen hernoemen, kopiëren naar een andere controller, exporteren en importeren (zo kan een vriend zijn profiel meenemen) en verwijderen.
- Per controller kies je welk profiel hij gebruikt.
- Trillingsfilter: een stick die trilt wordt rustig als je hem stilhoudt of langzaam richt, terwijl snelle bewegingen gewoon direct doorkomen. Een goede stick merkt er niets van. Per controller aan of uit te zetten.
- De app laat zelf weten als er een nieuwe versie is, met een lijstje van wat er nieuw is.
- Bijwerken gaat met één klik: de nieuwe versie wordt over de oude heen geïnstalleerd en start vanzelf. Je profielen, kalibratie en instellingen blijven bewaard.
- Bij Instellingen kun je automatisch zoeken naar updates aan- of uitzetten.

## 1.0.2

- Eerste versie met installer: PlayStation-controller als Xbox 360- of DualShock 4-controller, zelflerende kalibratie voor stickdrift en versleten triggers, en HidHide.
