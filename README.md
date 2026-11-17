# Unknown's Smokescreen

Lokaler Stream-Schutz für [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles). In einer geschützten Runde sieht die Kamera immer **Crewmate**. Das private Overlay zeigt dir die echte Rolle und den echten HUD an den üblichen Stellen.

Diese Mod steht in keiner Verbindung zu Innersloth LLC. Among Us und zugehörige Materialien gehören Innersloth LLC.

## Kamera und privates HUD

Die öffentliche Rollenanzeige bleibt fest auf Crewmate. Es gibt keine Rollenwürfelung, keinen Rollenpool, keinen Teamfilter und keinen zeitlichen Wechsel. Das gilt für den Text über dem Kopf, die Taskliste, Rolleninformationen als Geist, das gesamte Intro ab CoBegin. Rollenfarben und Rollenbeschreibungen anderer Teams werden dort nicht gezeigt. Alte Shuffle-Konfiguration wird beim Laden entfernt.

Die privaten Originale bleiben erhalten. Das Overlay enthält nur die Elemente, die die Maske tatsächlich ändert: Rollenknöpfe mit Texturen, Cooldowns und Tastenhinweisen, die echte Rollenzeile der Taskliste, farbige Namensschilder, Info-Zeilen, Umrisse, Pfeile und unterstützte Rollenobjekte. Schiff, Boden und alle übrigen Weltobjekte werden nie ins Overlay kopiert, weil sie dort bei Bewegung veralten würden.

Echt und maskiert werden diese Elemente je einmal über Schwarz und über Weiß gerendert, zusammen mit dem normalen HUD und der Abdunkelung des Spiels. Daraus ergibt sich pro Pixel genau die Schicht, die über dem live sichtbaren Spielbild das echte Bild ergibt. Nur wo das öffentliche Element nicht überdeckbar ist (zum Beispiel weiße Buchstaben unter einem roten Namen oder "Crewmate" unter einer längeren echten Rolle), liegt eine schmale schwarze Platte hinter dem echten Element; sie wirkt wie eine kräftigere Textkontur. Jede Insel gehört zu einem Anker: der HUD ist bildschirmfest, Namen, Info-Zeilen und Umrisse folgen ihrem Spieler, Weltobjekte ihrer Wurzel. Die Inhalte werden mit der eingestellten Rate aufgenommen, ihre Lage folgt jedem Spielframe. Intro und Meeting werden vollflächig gedeckt. Es werden keine Knöpfe nachgezeichnet und keine separaten Textkästen verwendet.

Klickflächen und Fähigkeitshotkeys bleiben unverändert. **F8** blendet das private HUD aus oder wieder ein. Die öffentliche Maske und der ausdrücklich aktivierte RecordingFeed laufen weiter. Ausgeschaltete oder durch Verkleidung geänderte Namensschilder werden respektiert. Öffentliche Namen bleiben unabhängig von der eigenen Rolle weiß und ohne Teamzeichen. Das private Bild übernimmt die native Sichtbarkeit, Teamfarben und bekannten Beziehungen aus TOR und geladenen Erweiterungen. Vorhandene UC-Namensadapter werden per Reflection eingebunden.

Im öffentlichen Intro erscheinen Crewmate, die Crew-Farbe und Crew-Beschreibung, der Crew-Sound und eine neutrale Crew-Aufstellung. Das private Bild behält die echte Aufstellung und die echte Rolle. Der Endscreen zeigt den echten Siegertext, die echten Farben und die unveränderte TOR-Rollenübersicht auch der Kamera. Der zusätzliche Smokescreen-Rückblick entfällt.

In geschützten Classic- und Guesser-Runden sehen du und die Zuschauer standardmäßig den Originalradius. Auf der Menüseite **Privatsphäre** kannst du **Sicht: Crew** wählen. Dann sehen beide den Crew-Radius einschließlich Lichtausfall und Trickster. Die Rollenlogik und der optionale RecordingFeed behalten den Originalradius.

Der Chat mit Knopf, Benachrichtigung, Eingabe und Verlauf ist standardmäßig privat. So sind Rollenmeldungen auch dann geschützt, wenn sie im regulären Meeting-Chat landen. Zuschauer sehen in diesem Modus auch den normalen Meeting-Chat nicht. Mit **Chat: Öffentlich** sind auch Rollenlogs und Teamchats für Zuschauer sichtbar. Die private Guesser-Auswahl, die Swapper-Bedienung und der Mayor-Schalter haben eine eigene Einstellung. Die Abstimmungszeilen bleiben für Zuschauer sichtbar, während der Streamer die Guesser-Auswahl bedient.

## Fenster und Aufnahmeschutz

Das private Fenster ist klickdurchlässig und liegt exakt über dem Client-Bereich des eigenen Unity-Spielfensters. Es erscheint nur, wenn dieses im Vordergrund und nicht minimiert ist. Bei aktivem Schutz wechselt das Spiel in ein randloses Fenster in Desktopgröße. Beim Abschalten werden der vorherige Modus und die vorherige Spielauflösung wiederhergestellt, einschließlich eines zuvor exklusiven Vollbildmodus.

Das private Fenster wird erst angezeigt, wenn Windows `WDA_EXCLUDEFROMCAPTURE` gesetzt hat und `GetWindowDisplayAffinity` den Wert bestätigt. Erforderlich sind Windows 10 ab Version 2004 und aktive Desktop-Komposition. Bei Ablehnung oder späterem Verlust des Schutzes bleibt das Fenster aus. Im Spiel erscheint einmal kurz `Anzeige nicht verfügbar`. Es gibt keinen schwächeren Ersatzmodus.

**Vor dem ersten Stream die verwendeten OBS-Aufnahmearten lokal prüfen.** Spielaufnahme und Fensteraufnahme des Spiels sollen ausschließlich die Crewmate-Fassung sehen; bei Bildschirmaufnahme soll Windows das private Fenster auslassen. Die Windows-Funktion garantiert keinen Schutz gegen jede denkbare Aufnahmemethode. Siehe [Microsoft-Dokumentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).

## Getrennte lokale Aufnahme

`Overlay / RecordingFeed = true` öffnet zusätzlich ein ausdrücklich aufnehmbares Fenster mit dem echten Kamerabild. Es startet minimiert und passt das Bild mit erhaltenem Seitenverhältnis an seine Größe an. Es wird unabhängig vom privaten F8-Schalter aktualisiert, auch wenn das Spiel den Vordergrund verliert und Unity weiter Frames verarbeitet.

Diese Option ist standardmäßig aus. Nur für eine getrennte lokale Aufnahme verwenden. Das Fenster gehört niemals in die Live-Szene und muss vom gestreamten Bildschirm ferngehalten werden.

## Einstellungen

Alle Einstellungen lassen sich im Spiel ändern: Knopf **Smokescreen...** im Optionsmenü von Among Us (Hauptmenü, Lobby und Esc-Menü im Spiel) oder Taste **F3** (`General / MenuKey`). Das Menü übernimmt Optik und Schrift der Vanilla-Optionen, wird mit der Maus bedient und mit Esc oder dem Schließen-Knopf beendet. Änderungen wirken sofort und werden in die Config geschrieben; die Bildrate bleibt zwischen 5 und 30. Tasten werden belegt, indem man auf den Tastenknopf klickt und dann die gewünschte Taste drückt (Esc bricht ab). Beim Überfahren erklärt die Fußzeile jeden Eintrag; ist der RecordingFeed an, steht dort dauerhaft eine Warnung.

Das Menü ist normales Spiel-UI und damit auch im Stream sichtbar. Es zeigt nur Einstellungen und neutrale Zustände („Schutz aktiv“, „Privates HUD aufnahmegeschützt“), nie Rolle oder Team. Wer auch die Einstellungen nicht zeigen will, öffnet es außerhalb einer Runde. Ist der Schutz beim Start per Config aus, lädt die Mod nur das Menü; nach dem Wiedereinschalten ist ein Neustart nötig.

Datei: `BepInEx/config/com.daunknown.smokescreen.cfg`.

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `General / Enabled` | `true` | Schutz aktivieren. |
| `General / OnlyInStreamerMode` | `true` | Nur bei aktiviertem Streamer-Modus schützen. |
| `General / MenuKey` | `F3` | Einstellungsmenü öffnen und schließen. |
| `Mask / HideRoleButtons` | `true` | Rollenknöpfe öffentlich ausblenden. |
| `Mask / HideRoleFlashes` | `true` | Rollenhinweise nur privat darstellen. |
| `Overlay / PrivateOverlay` | `true` | Privates echtes HUD anzeigen. |
| `Overlay / ToggleKey` | `F8` | Privates Overlay einblenden und ausblenden. |
| `Overlay / FramesPerSecond` | `15` | Zielbildrate von 5 bis 30. Die tatsächliche Rate hängt von GPU, Auflösung und Szene ab. |
| `Overlay / SyncFrames` | `1` | Verankerte private Namen und Beschriftungen nutzen die Spielerlage von so vielen Frames zuvor (0 bis 4), weil das Spielbild später auf dem Monitor erscheint als das Overlay. Laufen private Namen beim Gehen voraus, erhöhen; hängen sie hinterher, verringern. Im Menü als "HUD sync". |
| `Overlay / RecordingFeed` | `false` | Getrenntes aufnehmbares Wahrheitsfenster. |
| `Diagnostics / Enabled` | `false` | Diagnose für maximal 180 Sekunden, ohne Lockerung von Fokus oder Aufnahmeschutz. |

Die früheren Optionen `ShuffleMode`, `ShuffleGhostInfo` und `ShowRoundRecap` entfallen und werden aus der Config entfernt. Die Datei `smokescreen_overlay_test.txt` wird vollständig ignoriert. Explizite Diagnoseargumente sind `--smokescreen-diagnostics` und `"--smokescreen-dump=C:\absoluter\Diagnoseordner"`. Bildexporte enthalten private Informationen und erfolgen nur mit explizitem Dump-Argument. Höchstens 48 Proben (echtes Bild, maskiertes Bild und das für genau diesen Frame komponierte Overlay) werden innerhalb von 180 Sekunden gespeichert. Im Diagnosemodus wird auch ohne Vordergrund aufgenommen und gemessen; das private Fenster bleibt dann verborgen.

Die Menüseite **Privatsphäre** startet bei neuer Konfiguration mit **Sicht: Original**, **Chat: Privat** und **Rollentöne: Stumm**. Alle drei Einstellungen lassen sich im Spiel ändern. **Meeting privat** und **Leckwächter** sind standardmäßig eingeschaltet. Bekannte TOR-Fähigkeitstöne werden für die lokale Audioausgabe stummgeschaltet. Du hörst sie bei dieser Einstellung ebenfalls nicht. Öffentliche Bombentöne und Alarme bleiben erhalten. Der Leckwächter meldet verdächtige sichtbare Textstichproben im privaten HUD, beispielsweise Rollennamen oder Rollenfarben. Bestätigte Ausnahmen speichern eine Prüfsumme des exakten Texts. Der Wächter kann Treffer bei öffentlich erlaubten Texten melden und Lecks außerhalb der Stichprobe übersehen.

Der Schutzstatus im Menü und privaten HUD berücksichtigt Maskenrender, Sichtschutz und Windows-Fenstersperre. Die Anzeige unterscheidet eine veraltete oder fehlende Ausgabe und ein absichtlich verborgenes HUD. Die tatsächlich verwendete OBS-Quelle bleibt separat zu prüfen.

## Technik und Grenzen

Die Maskierung gilt einmal pro Frame vom ersten Kamerarender bis zum Frameende nach allen Bildschirmkameras. Ein Restore in Update und FixedUpdate sowie vor der TOR-Logik sichert ausgelassene Frameabschlüsse ab. Nur geänderte Texte erhalten erzwungene Meshupdates. Update, Physik und Rollenlogik behalten ihren echten Zustand. Der vorliegende Il2Cpp-Build stellt `AsyncGPUReadback` nicht bereit. Der Fallback nutzt `ReadPixels` und nichtgenerischen direkten Zugriff auf wiederverwendete Texturpuffer. Es gibt keine pixelweisen Il2Cpp-Zugriffe, generischen AOT-Aufrufe für den Readback oder neuen Pixelarrays pro Bild.

Renderziele und CPU-Texturen tragen `HideFlags.DontUnloadUnusedAsset` und werden ausdrücklich freigegeben. Die Pixelzahl ist auf das Äquivalent von 3840×2160 begrenzt. Pro Aufnahme rendern Haupt- und UI-Kamera vier leichte Durchgänge in ein wiederverwendetes Renderziel mit zwei Bildschirm-Slots; die Hauptkamera sieht dabei nur einen sonst ungenutzten Layer, auf den die privaten Weltrenderer kurz gelegt werden. Gelesen werden nur die Inselrechtecke. Zerlegung, Platten und Komposition laufen auf dem Overlay-Thread ohne Unity-Zugriffe; pro Spielframe wird nur das geänderte Rechteck per `UpdateLayeredWindowIndirect` übertragen. Ein Vollbild wird nur für Intro, Meeting und einen aktiven RecordingFeed gerendert. Standard sind weiterhin 15 fps; explizite Werte bis 30 werden respektiert.

Beim Ende der Maskierung bleibt das private Bild bis nach dem ersten unmaskierten Bildschirmrender erhalten. Bei einer Renderpause bleibt das letzte geschützte Bild stehen; Fokusverlust und fehlender Aufnahmeschutz blenden es weiterhin aus. Inhalte wie Cooldown-Zahlen aktualisieren sich mit der eingestellten Rate, ihre Lage mit jedem Spielframe. Namen und Beschriftungen, die an Spielern hängen, liegen immer auf einem dunklen Namensschild, dessen Rand beim Laufen um bis zu einen Frame Bewegung wächst; ein kleiner Restversatz zum Spielbild legt so keine öffentliche Schrift frei. Ein neu auftauchendes privates Element erscheint erst mit der nächsten Aufnahme; bis dahin sieht man kurz die öffentliche Fassung. Private Weltobjekte am Boden werden über darüberlaufende Spieler gezeichnet. Inhalte in Texturkameras (z. B. Überwachungsbilder) zeigen privat die öffentliche Fassung.

Eigene Kamera-Nachbearbeitung, komplexe abhängige Texturkameras und UI mit `ScreenSpaceOverlay` können zusätzliche Integration benötigen. Neue Rollenobjekte anderer Modversionen sind nicht automatisch vollständig abgedeckt. Sichtbare Handlungen wie Töten, Verwandeln, Bewegung und öffentlich sichtbare Effekte bleiben mögliche Informationsquellen. Die öffentlich feste Rolle ändert die tatsächliche Spielrolle nicht.

Solange ein Vanilla-Minigame offen ist, blendet das private HUD weltverankerte Inseln und die private Statuszeile aus. Unterstützte zusätzliche UI-Kameras mit getrennten Layern verdecken private Inseln entsprechend der Renderreihenfolge. Inhalte von abhängigen Texturkameras bleiben im privaten HUD in ihrer öffentlichen Fassung.

## Installation und Build

TOR und BepInEx/Il2Cpp müssen bereits installiert sein. `UnknownsSmokescreen.dll` kommt nach `BepInEx/plugins`. Nur der lokale Spieler braucht die Mod. Sie sendet keine eigenen RPCs.

`dotnet build -c Release` erzeugt `bin/Release/net6.0/UnknownsSmokescreen.dll`. Das Projekt besitzt keine Deploy- oder Kopierziele in eine Spielinstallation.

GPL-3.0-or-later, siehe [LICENSE](LICENSE).
