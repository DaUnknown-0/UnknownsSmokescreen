# Unknown's Smokescreen

Lokaler Stream-Schutz für [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles). In einer geschützten Runde sieht die Kamera immer **Crewmate**. Das private Overlay zeigt dir die echte Rolle und den echten HUD an den üblichen Stellen.

Diese Mod steht in keiner Verbindung zu Innersloth LLC. Among Us und zugehörige Materialien gehören Innersloth LLC.

## Kamera und privates HUD

Die öffentliche Rollenanzeige bleibt fest auf Crewmate. Es gibt keine Rollenwürfelung, keinen Rollenpool, keinen Teamfilter und keinen zeitlichen Wechsel. Das gilt für den Text über dem Kopf, die Taskliste, Rolleninformationen als Geist, das gesamte Intro ab CoBegin. Rollenfarben und Rollenbeschreibungen anderer Teams werden dort nicht gezeigt. Alte Shuffle-Konfiguration wird beim Laden entfernt.

Die privaten Originale bleiben erhalten. Das Overlay enthält nur die Elemente, die die Maske tatsächlich ändert: Rollenknöpfe mit Texturen, Cooldowns und Tastenhinweisen, die echte Rollenzeile der Taskliste, farbige Namensschilder, Info-Zeilen, Umrisse, Pfeile und unterstützte Rollenobjekte. Schiff, Boden und alle übrigen Weltobjekte werden nie ins Overlay kopiert, weil sie dort bei Bewegung veralten würden.

Echt und maskiert werden diese Elemente je einmal über Schwarz und über Weiß gerendert, zusammen mit dem normalen HUD und der Abdunkelung des Spiels. Daraus ergibt sich pro Pixel genau die Schicht, die über dem live sichtbaren Spielbild das echte Bild ergibt. Nur wo das öffentliche Element nicht überdeckbar ist (zum Beispiel weiße Buchstaben unter einem roten Namen oder "Crewmate" unter einer längeren echten Rolle), liegt eine schmale schwarze Platte hinter dem echten Element; sie wirkt wie eine kräftigere Textkontur. Jede Insel gehört zu einem Anker: der HUD ist bildschirmfest, Namen, Info-Zeilen und Umrisse folgen ihrem Spieler, Weltobjekte ihrer Wurzel. Die Inhalte werden mit der eingestellten Rate aufgenommen, ihre Lage folgt jedem Spielframe. Intro und Meeting werden vollflächig gedeckt. Es werden keine Knöpfe nachgezeichnet und keine separaten Textkästen verwendet.

Klickflächen und Fähigkeitshotkeys bleiben unverändert. **F8** blendet das private HUD aus oder wieder ein. Unter **HUD** lässt sich auf **Halten** umstellen. Dann erscheint das private HUD bei gedrückter Taste und verschwindet beim Loslassen, Fokusverlust oder Öffnen des Chats. Nach Fokuswechsel oder Tastenbelegung zählt ein neuer Tastendruck. Die öffentliche Maske und der ausdrücklich freigegebene RecordingFeed laufen weiter. Ausgeschaltete oder durch Verkleidung geänderte Namensschilder werden respektiert. Öffentliche Namen bleiben unabhängig von der eigenen Rolle weiß und ohne Teamzeichen. Das private Bild übernimmt die native Sichtbarkeit, Teamfarben und bekannten Beziehungen aus TOR und geladenen Erweiterungen. Vorhandene UC-Namensadapter werden per Reflection eingebunden.

Im öffentlichen Intro erscheinen Crewmate, die Crew-Farbe und Crew-Beschreibung, der Crew-Sound und eine neutrale Crew-Aufstellung. Das private Bild behält die echte Aufstellung und die echte Rolle. Der Endscreen zeigt den echten Siegertext, die echten Farben und die unveränderte TOR-Rollenübersicht auch der Kamera. Der zusätzliche Smokescreen-Rückblick entfällt.

In geschützten Classic- und Guesser-Runden sehen du und die Zuschauer standardmäßig den Originalradius. Auf der Menüseite **Privatsphäre** kannst du **Sicht: Crew** wählen. Dann sehen beide den Crew-Radius einschließlich Lichtausfall und Trickster. Die Rollenlogik und der optionale RecordingFeed behalten den Originalradius.

Der Chat mit Knopf, Benachrichtigung, Eingabe und Verlauf ist standardmäßig privat. So sind Rollenmeldungen auch dann geschützt, wenn sie im regulären Meeting-Chat landen. Zuschauer sehen in diesem Modus auch den normalen Meeting-Chat nicht. Mit **Chat: Öffentlich** sind auch Rollenlogs und Teamchats für Zuschauer sichtbar. Die private Guesser-Auswahl, die Swapper-Bedienung und der Mayor-Schalter haben eine eigene Einstellung. Die Abstimmungszeilen bleiben für Zuschauer sichtbar, während der Streamer die Guesser-Auswahl bedient.

## Fenster und Aufnahmeschutz

Das private Fenster ist klickdurchlässig und folgt dem Client-Bereich des eigenen Unity-Spielfensters in physischen Pixeln. Es erscheint im Vordergrund und bei sichtbarem, nicht minimiertem Spiel. Fenstermodus und randloses Vollbild lassen sich weiter verwenden. Exklusives Vollbild wechselt bei aktivem Schutz mit privatem HUD zu randlosem Vollbild. Beim Abschalten wird der gespeicherte exklusive Modus mit seiner Spielauflösung wiederhergestellt.

Der Overlay-Thread verwendet Per-Monitor DPI Awareness V2. Ein Verschieben auf demselben Monitor versetzt die Anzeige. Beim Wechsel von Monitor, Fenster-DPI oder Client-Größe wartet sie auf eine zur neuen Geometrie passende Aufnahme. Liegt die Unity-Auflösung unter der physischen Client-Größe, wird das zusammengesetzte HUD passend skaliert. Die öffentliche Maske bleibt währenddessen aktiv. [Microsofts Dokumentation zur DPI-Skalierung](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

Das private Fenster wird erst angezeigt, wenn Windows `WDA_EXCLUDEFROMCAPTURE` gesetzt hat und `GetWindowDisplayAffinity` den Wert bestätigt. Erforderlich sind natives Windows 10 ab Version 2004 und aktive Desktop-Komposition. Bei Ablehnung oder späterem Verlust des Schutzes bleibt das Fenster aus. Der Menüstatus nennt die Ursache und einen passenden nächsten Schritt. **Aufnahme → Erneut versuchen** legt das private Fenster ohne Spielneustart neu an. Das BepInEx-Log enthält API-Fehlercodes, Windows-Build, DWM-Zustand und Hinweise auf Wine/Proton, Remote Desktop oder eine virtuelle Maschine. Beim Verlust im laufenden Spiel kommen Zeitpunkt und der zuletzt gelesene Affinity-Wert hinzu.

**Vor dem ersten Stream die verwendeten OBS-Aufnahmearten lokal prüfen.** Unter **Aufnahme** zeigt der Selbsttest kurz ein harmloses Farbmuster in einem Fenster mit derselben Win32-Fenstersperre wie das private HUD. GDI-Bildschirmabzug, DXGI Desktop Duplication und Windows Graphics Capture prüfen ein kleines Rechteck auf dem Monitor des Spiels. Grün bedeutet, dass der Weg das öffentliche Kontrollmuster sieht und die private Probe auslässt. Rot bedeutet, dass die private Probe sichtbar war. Grau bedeutet, dass der Weg kein eindeutiges Ergebnis lieferte. Windows oder Treiber können einzelne Wege sperren. Der Test speichert keine Bilder.

Für **Bildschirmaufnahme** in OBS den auf demselben Monitor getesteten Weg auswählen. Die Methoden **Automatisch**, **Spielaufnahme** und **Fensteraufnahme** sowie OBS-Plugins in der tatsächlichen Szene mit einer lokalen Aufnahme prüfen. Ein Wechsel von Monitor, Quelle oder Treiber kann das Ergebnis ändern. [Windows-Fenstersperre](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [OBS-Aufnahmemethoden](https://github.com/obsproject/obs-studio/blob/master/plugins/win-capture/data/locale/en-US.ini).

Der **OBS-Wächter** sucht alle drei Sekunden lokal nach bekannten Prozessnamen, darunter OBS, Streamlabs und XSplit. Bei erwartetem Schutzproblem oder freigegebenem Feed warnt ein separates, aufnahmegeschütztes Fenster über dem eigenen Spiel. Falls Windows auch dieses Fenster nicht schützen kann, steht die Meldung im lokalen BepInEx-Log. Ein Prozessfund sagt nichts über den Streamingstatus oder die gewählte Szene aus.

## Getrennte lokale Aufnahme

`Overlay / RecordingFeed = true` erlaubt die manuelle Freigabe über **Aufnahme → Feed freigeben**. Jede neue Spielinstanz startet gesperrt. Erst die Freigabe öffnet ein aufnehmbares Fenster mit dem echten Kamerabild. Es startet minimiert und erhält das Seitenverhältnis. Es ist unabhängig von F8 und wird aktualisiert, solange Unity Frames verarbeitet. **Feed sperren** oder **Feed erlauben: Aus** schließt das Fenster. Auch eine aus einer früheren Version übernommene Config braucht die Sitzungsfreigabe.

Diese Option ist standardmäßig aus. Nur für eine getrennte lokale Aufnahme verwenden. Das Fenster gehört niemals in die Live-Szene und muss vom gestreamten Bildschirm ferngehalten werden.

**Rückblick puffern** hält in geschützten Runden bis zu 30 Sekunden Originalsicht ohne Ton im Arbeitsspeicher. Die Kamera liefert höchstens drei Bilder pro Sekunde mit maximal 480 × 270 Pixeln. Der Ring fasst maximal 90 Bilder und etwa 45 MiB zuzüglich kleiner Renderpuffer. Ein Abbruch oder der Beginn der nächsten Runde verwirft den bisher verfügbaren Rückblick. Nach dem Ergebnis kann **Rückblick als Bildfolge speichern** die Bilder und `Wiederholung.html` unter `BepInEx/SmokescreenReplays` ablegen. Ein Export belegt höchstens rund 34 MiB und kann neben der nächsten Runde auslaufen. Die Dateien enthalten echte Sicht, einschließlich sichtbarer Namen und privater HUD-Elemente.

## Zuschauer

Unter **Zuschauer** lässt sich eine lokale **OBS-Browserseite** einschalten. Die im Menü gezeigte Adresse, standardmäßig `http://127.0.0.1:18765/`, kommt als Browserquelle in OBS. Während der Runde bleibt die Quelle leer. Nach dem Endbildschirm zeigt sie Spielernamen und die tatsächlichen TOR-Rollen der beendeten Runde, bis die nächste Runde startet. Der Server lauscht nur auf IPv4-Loopback, Port 8765 bleibt reserviert. Bei einem belegten Port lässt sich im Menü ein anderer wählen. Lokale Programme können die veröffentlichte Übersicht lesen. Die Seite verwendet keine externen Ressourcen.

Der **Zuschauerhinweis** erklärt die feste Crewmate-Anzeige und die gegebenenfalls fehlenden Fähigkeitsschaltflächen mit demselben Text für alle Rollen. Er kann am oberen oder unteren Spielfensterrand erscheinen und verschwindet in Minispielen und am Rundenende.

## Einstellungen

Alle Einstellungen lassen sich im Spiel ändern: Knopf **Smokescreen...** im Optionsmenü von Among Us (Hauptmenü, Lobby und Esc-Menü im Spiel) oder Taste **F3** (`General / MenuKey`). Das Menü übernimmt Optik und Schrift der Vanilla-Optionen, wird mit der Maus bedient und mit Esc oder dem Schließen-Knopf beendet. Änderungen wirken sofort und werden in die Config geschrieben; die Bildrate bleibt zwischen 5 und 30. Tasten werden belegt, indem man auf den Tastenknopf klickt und dann die gewünschte Taste drückt (Esc bricht ab). Beim Überfahren erklärt die Fußzeile jeden Eintrag; ist der RecordingFeed an, steht dort dauerhaft eine Warnung.

Die Seite **HUD** enthält das Ausgabeprofil und die Profiltaste, den Haltemodus und die adaptive HUD-Rate. Der Profilknopf und standardmäßig **F5** wechseln in derselben Reihenfolge zwischen den Profilen. Rolle, öffentliche Maske, Streamer-Modus-Bindung und manuelle Maskenschalter gelten in jedem Profil. Jeder Profilwechsel sperrt einen zuvor freigegebenen Feed. Für eine neue Freigabe braucht es weiterhin **Feed erlauben** und den Klick auf **Feed freigeben**.

| Profil | Erlaubte optionale Ausgaben |
|---|---|
| Eigene Einstellungen (`Custom`) | Feed, Rückblick und Zuschauerausgaben nach den bestehenden Einstellungen. Standard für bestehende Configs. |
| Live (`Live`) | Rückblick und konfigurierte Zuschauerausgaben. |
| Nur Aufnahme (`Recording`) | Manuell freigegebener Feed und konfigurierter Rückblick. |
| Privat spielen (`Private`) | Privates, aufnahmegeschütztes HUD nach der HUD-Einstellung. Beim Wechsel wird der aktuelle Rückblickpuffer verworfen. |

Die adaptive HUD-Rate ist standardmäßig aus. Sie misst die Dauer der privaten Aufnahme einschließlich Rendern und Readback und schätzt diese Last pro Spielframe. Der Zielwert von 0,5 bis 5,0 ms steuert die Rate zwischen 5 fps und der eingestellten Obergrenze. Bei hoher Last kann schon die Mindestrate das Budget überschreiten. Das Menü zeigt das aktuelle Aufnahmeziel. Anker folgen weiterhin jedem Spielframe. Feed und Rückblick haben ihre eigene Taktung.

Das Menü ist normales Spiel-UI und damit auch im Stream sichtbar. Es zeigt nur Einstellungen und neutrale Zustände („Schutz aktiv“, „Privates HUD aufnahmegeschützt“), nie Rolle oder Team. Wer auch die Einstellungen nicht zeigen will, öffnet es außerhalb einer Runde. Ist der Schutz beim Start per Config aus, lädt die Mod nur das Menü; nach dem Wiedereinschalten ist ein Neustart nötig.

Datei: `BepInEx/config/com.daunknown.smokescreen.cfg`.

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `General / Enabled` | `true` | Schutz aktivieren. |
| `General / OnlyInStreamerMode` | `true` | Nur bei aktiviertem Streamer-Modus schützen. |
| `General / MenuKey` | `F3` | Einstellungsmenü öffnen und schließen. |
| `Profiles / Active` | `Custom` | Aktives Ausgabeprofil. |
| `Profiles / SwitchKey` | `F5` | Zum nächsten Ausgabeprofil wechseln. |
| `Mask / HideRoleButtons` | `true` | Rollenknöpfe öffentlich ausblenden. |
| `Mask / HideRoleFlashes` | `true` | Rollenhinweise nur privat darstellen. |
| `Overlay / PrivateOverlay` | `true` | Privates echtes HUD anzeigen. |
| `Overlay / ToggleKey` | `F8` | Privates Overlay einblenden und ausblenden. |
| `Overlay / HoldKey` | `false` | HUD nur beim Halten der HUD-Taste anzeigen. |
| `Overlay / FramesPerSecond` | `15` | Feste Zielrate oder Obergrenze für das adaptive HUD. Zielrate des RecordingFeeds bei Freigabe. |
| `Overlay / AdaptiveRate` | `false` | Private HUD-Rate an die gemessene Last anpassen. |
| `Overlay / BudgetTenthsMs` | `15` | Adaptives Zeitbudget pro Spielframe in Zehntelmillisekunden. |
| `Overlay / SyncFrames` | `1` | Verankerte private Namen und Beschriftungen nutzen die Spielerlage von so vielen Frames zuvor (0 bis 4), weil das Spielbild später auf dem Monitor erscheint als das Overlay. Laufen private Namen beim Gehen voraus, erhöhen; hängen sie hinterher, verringern. Im Menü als "HUD sync". |
| `Overlay / RecordingFeed` | `false` | Erlaubnis zur manuellen Sitzungsfreigabe des aufnehmbaren Originalfensters. |
| `Capture / ObsWatch` | `true` | Privater Hinweis bei erkannten Aufnahmeprogrammen und Schutzproblemen. |
| `Capture / DelayedReplay` | `false` | Originalsicht für einen späteren Bildexport im Speicher puffern. |
| `Viewers / BrowserSource` | `false` | Lokale OBS-Browserseite mit Rollenübersicht nach Rundenende. |
| `Viewers / Port` | `18765` | Port auf 127.0.0.1. Port 8765 ist reserviert. |
| `Viewers / Explanation` | `false` | Rollenfreie Zuschauer-Erklärung im Spiel anzeigen. |
| `Viewers / ExplanationPosition` | `Top` | Erklärung oben oder unten (`Bottom`) platzieren. |
| `Diagnostics / Enabled` | `false` | Diagnose für maximal 180 Sekunden, ohne Lockerung von Fokus oder Aufnahmeschutz. |

Die früheren Optionen `ShuffleMode`, `ShuffleGhostInfo` und `ShowRoundRecap` entfallen und werden aus der Config entfernt. Die Datei `smokescreen_overlay_test.txt` wird vollständig ignoriert. Explizite Diagnoseargumente sind `--smokescreen-diagnostics` und `"--smokescreen-dump=C:\absoluter\Diagnoseordner"`. Bildexporte enthalten private Informationen und erfolgen nur mit explizitem Dump-Argument. Höchstens 48 Proben (echtes Bild, maskiertes Bild und das für genau diesen Frame komponierte Overlay) werden innerhalb von 180 Sekunden gespeichert. Im Diagnosemodus wird auch ohne Vordergrund aufgenommen und gemessen; das private Fenster bleibt dann verborgen.

Die Menüseite **Privatsphäre** startet bei neuer Konfiguration mit **Sicht: Original**, **Chat: Privat** und **Rollentöne: Stumm**. Alle drei Einstellungen lassen sich im Spiel ändern. **Meeting privat** und **Leckwächter** sind standardmäßig eingeschaltet. Bekannte TOR-Fähigkeitstöne werden für die lokale Audioausgabe stummgeschaltet. Du hörst sie bei dieser Einstellung ebenfalls nicht. Öffentliche Bombentöne und Alarme bleiben erhalten. Der Leckwächter meldet verdächtige sichtbare Textstichproben im privaten HUD, beispielsweise Rollennamen oder Rollenfarben. Bestätigte Ausnahmen speichern eine Prüfsumme des exakten Texts. Der Wächter kann Treffer bei öffentlich erlaubten Texten melden und Lecks außerhalb der Stichprobe übersehen.

Der Schutzstatus im Menü und privaten HUD berücksichtigt Maskenrender, Sichtschutz und Windows-Fenstersperre. Die Anzeige unterscheidet eine veraltete oder fehlende Ausgabe und ein absichtlich verborgenes HUD. Die tatsächlich verwendete OBS-Quelle bleibt separat zu prüfen.

## Technik und Grenzen

Die Maskierung gilt einmal pro Frame vom ersten Kamerarender bis zum Frameende nach allen Bildschirmkameras. Ein Restore in Update und FixedUpdate sowie vor der TOR-Logik sichert ausgelassene Frameabschlüsse ab. Nur geänderte Texte erhalten erzwungene Meshupdates. Update, Physik und Rollenlogik behalten ihren echten Zustand. Der vorliegende Il2Cpp-Build stellt `AsyncGPUReadback` nicht bereit. Der Fallback nutzt `ReadPixels` und nichtgenerischen direkten Zugriff auf wiederverwendete Texturpuffer. Es gibt keine pixelweisen Il2Cpp-Zugriffe, generischen AOT-Aufrufe für den Readback oder neuen Pixelarrays pro Bild.

Renderziele und CPU-Texturen tragen `HideFlags.DontUnloadUnusedAsset` und werden ausdrücklich freigegeben. Die Pixelzahl ist auf das Äquivalent von 3840×2160 begrenzt. Pro Aufnahme rendern Haupt- und UI-Kamera vier leichte Durchgänge in ein wiederverwendetes Renderziel mit zwei Bildschirm-Slots; die Hauptkamera sieht dabei nur einen sonst ungenutzten Layer, auf den die privaten Weltrenderer kurz gelegt werden. Gelesen werden nur die Inselrechtecke. Zerlegung, Platten und Komposition laufen auf dem Overlay-Thread ohne Unity-Zugriffe; pro Spielframe wird nur das geänderte Rechteck per `UpdateLayeredWindowIndirect` übertragen. Das HUD rendert Vollbilder für Intro und Meetings sowie für einen freigegebenen Aufnahme-Feed. Der optionale Rückblick verwendet ein eigenes niedrig aufgelöstes Renderziel. Standard sind weiterhin 15 fps; explizite Werte bis 30 werden respektiert.

Beim Ende der Maskierung bleibt das private Bild bis nach dem ersten unmaskierten Bildschirmrender erhalten. Bei einer Renderpause bleibt das letzte geschützte Bild stehen; Fokusverlust und fehlender Aufnahmeschutz blenden es weiterhin aus. Inhalte wie Cooldown-Zahlen aktualisieren sich mit der eingestellten Rate, ihre Lage mit jedem Spielframe. Namen und Beschriftungen, die an Spielern hängen, liegen immer auf einem dunklen Namensschild, dessen Rand beim Laufen um bis zu einen Frame Bewegung wächst; ein kleiner Restversatz zum Spielbild legt so keine öffentliche Schrift frei. Ein neu auftauchendes privates Element erscheint erst mit der nächsten Aufnahme; bis dahin sieht man kurz die öffentliche Fassung. Private Weltobjekte am Boden werden über darüberlaufende Spieler gezeichnet. Inhalte in Texturkameras (z. B. Überwachungsbilder) zeigen privat die öffentliche Fassung.

Eigene Kamera-Nachbearbeitung, komplexe abhängige Texturkameras und UI mit `ScreenSpaceOverlay` können zusätzliche Integration benötigen. Neue Rollenobjekte anderer Modversionen sind nicht automatisch vollständig abgedeckt. Sichtbare Handlungen wie Töten, Verwandeln, Bewegung und öffentlich sichtbare Effekte bleiben mögliche Informationsquellen. Die öffentlich feste Rolle ändert die tatsächliche Spielrolle nicht.

Solange ein Vanilla-Minigame offen ist, blendet das private HUD weltverankerte Inseln und die private Statuszeile aus. Unterstützte zusätzliche UI-Kameras mit getrennten Layern verdecken private Inseln entsprechend der Renderreihenfolge. Inhalte von abhängigen Texturkameras bleiben im privaten HUD in ihrer öffentlichen Fassung.

## Schnittstelle für andere Mods

Der öffentliche Typ `UnknownsSmokescreen.PrivateVisuals` im Assembly `UnknownsSmokescreen` bietet die lokale API-Version 1 (`public const int ApiVersion`). Unity-Objekte auf dem Hauptthread registrieren, bevor sie erstmals sichtbar werden. Ein optionaler BepInEx-Soft-Dependency-Eintrag mit der GUID `com.daunknown.smokescreen` kann die Ladereihenfolge festlegen. Für das Beispiel braucht die integrierende Mod nur Unity und Harmony.

| Statische Methode | Öffentliche Darstellung in geschützten Classic- und Guesser-Runden |
|---|---|
| `long RegisterRenderer(UnityEngine.Renderer renderer)` | Blendet einen Renderer aus, zum Beispiel einen Fallenmarker. |
| `long RegisterTree(UnityEngine.GameObject root)` | Blendet Renderer im Baum aus, etwa ein Panel mit SpriteRenderern und Mesh-Texten. Das schließt später hinzugefügte Kinder ein. |
| `long RegisterText(TMPro.TextMeshPro text, string publicText)` | Ersetzt Welt- oder HUD-Meshtext durch einen fest vorgegebenen öffentlichen Text in Weiß. Bei `null` verschwindet der Text samt Unterrenderern, bei `""` wird er durch einen leeren Text ersetzt. |
| `bool Unregister(long token)` | Entfernt die entsprechende Registrierung. Bei weiterhin sichtbaren Objekten gilt die Änderung ab dem nächsten Maskenrender. |

```csharp
// Auf dem Unity-Hauptthread, sobald der eigene Renderer angelegt ist.
var api = HarmonyLib.AccessTools.TypeByName("UnknownsSmokescreen.PrivateVisuals");
var register = api != null && api.GetField("ApiVersion")?.GetRawConstantValue() is int version && version == 1
    ? api.GetMethod("RegisterRenderer", new[] { typeof(UnityEngine.Renderer) })
    : null;
long token = register?.Invoke(null, new object[] { ownTrapRenderer }) is long id ? id : 0;

// Beim Entfernen des eigenen Renderers.
if (token != 0)
    api.GetMethod("Unregister", new[] { typeof(long) })?.Invoke(null, new object[] { token });
```

Eine gültige Registrierung liefert ein Token ungleich 0. Registrierungen bleiben bis zur Abmeldung oder Zerstörung des Objekts erhalten und gelten über Rundenwechsel hinweg. Pro Ziel eine Registrierung verwenden. Objekte bleiben in Spiel- und Rollenlogik unverändert. Im geschützten privaten HUD werden die Originale über die üblichen Kamera- und Inselpfade gezeigt. Der explizit freigegebene Originalfeed und der optionale Rückblick können die Originale enthalten. `IsMaskActive` meldet, ob der aktuelle Spielzustand in den Schutzbereich fällt und die Kernpatches geladen sind. Für Informationen, die auch ohne Smokescreen, in der Lobby oder außerhalb geschützter Runden privat bleiben müssen, muss der Integrator ihre Sichtbarkeit selbst steuern.

Die API unterstützt Unity-Renderer auf den vorhandenen Haupt- und UI-Kamerapfaden. Camera-Canvas- und Screen-Space-Overlay-Elemente mit `CanvasRenderer`, `TextMeshProUGUI`, IMGUI, Audio und Ausgaben außerhalb des Spielfensters liegen außerhalb dieses Vertrags.

## Installation und Build

TOR und BepInEx/Il2Cpp müssen bereits installiert sein. `UnknownsSmokescreen.dll` kommt nach `BepInEx/plugins`. Nur der lokale Spieler braucht die Mod. Sie sendet keine eigenen RPCs.

`dotnet build -c Release` erzeugt `bin/Release/net6.0/UnknownsSmokescreen.dll`. Das Projekt besitzt keine Deploy- oder Kopierziele in eine Spielinstallation.

GPL-3.0-or-later, siehe [LICENSE](LICENSE).
