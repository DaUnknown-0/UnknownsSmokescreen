# Unknown's Smokescreen

Lokaler Stream-Schutz für [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles). In einer geschützten Runde sieht die Kamera immer **Crewmate**. Das private Overlay zeigt dir die echte Rolle und den echten HUD an den üblichen Stellen.

Diese Mod steht in keiner Verbindung zu Innersloth LLC. Among Us und zugehörige Materialien gehören Innersloth LLC.

## Kamera und privates HUD

Die öffentliche Rollenanzeige bleibt fest auf Crewmate. Es gibt keine Rollenwürfelung, keinen Rollenpool, keinen Teamfilter und keinen zeitlichen Wechsel. Das gilt für den Text über dem Kopf, die Taskliste, Rolleninformationen als Geist, das gesamte Intro ab CoBegin. Rollenfarben und Rollenbeschreibungen anderer Teams werden dort nicht gezeigt. Alte Shuffle-Konfiguration wird beim Laden entfernt.

Das geschützte Fenster zeigt pro sichtbarem Spielframe die vollständige Welt mit der echten Rolle und dem privaten HUD. Der reguläre Kamerastapel zeichnet die Crew-Fassung ins Spielfenster. Beide Durchläufe verwenden dieselbe Spielauflösung und erfolgen zwischen denselben Spielupdates.

Klickflächen und Fähigkeitshotkeys bleiben unverändert. **F8** blendet das private HUD aus oder wieder ein. Unter **HUD** lässt sich auf **Halten** umstellen. Dann erscheint das private HUD bei gedrückter Taste und verschwindet beim Loslassen, Fokusverlust oder Öffnen des Chats. Nach Fokuswechsel oder Tastenbelegung zählt ein neuer Tastendruck. Die öffentliche Maske und der ausdrücklich freigegebene RecordingFeed laufen weiter. Ausgeschaltete oder durch Verkleidung geänderte Namensschilder werden respektiert. Öffentliche Namen bleiben unabhängig von der eigenen Rolle weiß und ohne Teamzeichen. Das private Bild übernimmt die native Sichtbarkeit, Teamfarben und bekannten Beziehungen aus TOR und geladenen Erweiterungen. Vorhandene UC-Namensadapter werden per Reflection eingebunden.

Im öffentlichen Intro erscheinen Crewmate, die Crew-Farbe und Crew-Beschreibung, der Crew-Sound und eine neutrale Crew-Aufstellung. Das private Bild behält die echte Aufstellung und die echte Rolle. Der Endscreen zeigt den echten Siegertext, die echten Farben und die unveränderte TOR-Rollenübersicht auch der Kamera. Der zusätzliche Smokescreen-Rückblick entfällt.

In geschützten Classic- und Guesser-Runden sehen du und die Zuschauer standardmäßig den Originalradius. Auf der Menüseite **Privatsphäre** kannst du **Sicht: Crew** wählen. Dann sehen beide den Crew-Radius einschließlich Lichtausfall und Trickster. Die Rollenlogik und der optionale RecordingFeed behalten den Originalradius.

Der Chat mit Knopf, Benachrichtigung, Eingabe und Verlauf ist standardmäßig privat. So sind Rollenmeldungen auch dann geschützt, wenn sie im regulären Meeting-Chat landen. Zuschauer sehen in diesem Modus auch den normalen Meeting-Chat nicht. Mit **Chat: Öffentlich** sind auch Rollenlogs und Teamchats für Zuschauer sichtbar. Die private Guesser-Auswahl, die Swapper-Bedienung und der Mayor-Schalter haben eine eigene Einstellung. Die Abstimmungszeilen bleiben für Zuschauer sichtbar, während der Streamer die Guesser-Auswahl bedient.

## Fenster und Aufnahmeschutz

Das private Fenster ist klickdurchlässig und folgt dem Client-Bereich des eigenen Unity-Spielfensters in physischen Pixeln. Es erscheint im Vordergrund und bei sichtbarem, nicht minimiertem Spiel. Fenstermodus und randloses Vollbild lassen sich weiter verwenden. Exklusives Vollbild wechselt bei aktivem Schutz mit privatem HUD zu randlosem Vollbild. Beim Abschalten wird der gespeicherte exklusive Modus mit seiner Spielauflösung wiederhergestellt.

Der Overlay-Thread verwendet Per-Monitor DPI Awareness V2. Ein Verschieben auf demselben Monitor versetzt die Anzeige. Beim Wechsel von Monitor, Fenster-DPI oder Client-Größe wartet sie auf eine zur neuen Geometrie passende Aufnahme. Liegt die Unity-Auflösung unter der physischen Client-Größe, wird das zusammengesetzte HUD passend skaliert. Die öffentliche Maske bleibt währenddessen aktiv. [Microsofts Dokumentation zur DPI-Skalierung](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

Das private Fenster wird erst angezeigt, wenn Windows `WDA_EXCLUDEFROMCAPTURE` gesetzt hat und `GetWindowDisplayAffinity` den Wert bestätigt. Die Freigabe gilt für das geprüfte Layout und bleibt beim Neustart erhalten. Mindestens zwei der drei Aufnahmewege müssen bestehen. Jede sichtbare Privatprobe sperrt das HUD. Erforderlich sind natives Windows 10 ab Version 2004 und aktive Desktop-Komposition. Bei Ablehnung oder späterem Verlust des Schutzes bleibt das Fenster aus. Der Menüstatus nennt die Ursache und einen passenden nächsten Schritt. **Aufnahme → Erneut versuchen** legt das private Fenster ohne Spielneustart neu an. Das BepInEx-Log enthält API-Fehlercodes, Windows-Build, DWM-Zustand und Hinweise auf Wine/Proton, Remote Desktop oder eine virtuelle Maschine. Beim Verlust im laufenden Spiel kommen Zeitpunkt und der zuletzt gelesene Affinity-Wert hinzu.

**Vor dem ersten Stream die verwendeten OBS-Aufnahmearten lokal prüfen.** Im Hauptmenü unter **Aufnahme** den Test vor Beginn der Aufzeichnung manuell starten. Pro Tastendruck läuft ein Versuch. Bei ungeprüftem Layout lässt sich der Test auch in der Runde starten. Dann verwenden beide Proben ausschließlich synthetische Pixel. Im Menü hält die offene DirectComposition-Kontrolle einen 48 × 48 Pixel großen Hauptmenüausschnitt an seiner Position fest. 16 einzelne Pixel tragen im Grünkanal eine eigene Kennung je Messphase. Das geschützte HUD zeigt darüber einen anderen Menüausschnitt. Grün bedeutet, dass der Aufnahmeweg die offene Kontrolle vor und während der privaten Probe erfasst hat. Rot bedeutet, dass die private Probe sichtbar war. Grau bedeutet, dass der Weg kein eindeutiges Ergebnis lieferte. Windows oder Treiber können einzelne Wege sperren. Der Test speichert keine Bilder.

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

Alle Einstellungen lassen sich im Spiel ändern: Knopf **Smokescreen...** im Optionsmenü von Among Us (Hauptmenü, Lobby und Esc-Menü im Spiel) oder Taste **F3** (`General / MenuKey`). Das Menü übernimmt Optik und Schrift der Vanilla-Optionen, wird mit der Maus bedient und mit Esc oder dem Schließen-Knopf beendet. Änderungen wirken sofort und werden in die Config geschrieben; die Feed-Bildrate bleibt zwischen 5 und 30. Tasten werden belegt, indem man auf den Tastenknopf klickt und dann die gewünschte Taste drückt (Esc bricht ab). Beim Überfahren erklärt die Fußzeile jeden Eintrag; ist der RecordingFeed an, steht dort dauerhaft eine Warnung.

Die Seite **HUD** enthält das Ausgabeprofil mit Profiltaste und den Haltemodus. Der Profilknopf und standardmäßig **F5** wechseln in derselben Reihenfolge zwischen den Profilen. Rolle, öffentliche Maske, Streamer-Modus-Bindung und manuelle Maskenschalter gelten in jedem Profil. Jeder Profilwechsel sperrt einen zuvor freigegebenen Feed. Für eine neue Freigabe braucht es weiterhin **Feed erlauben** und den Klick auf **Feed freigeben**.

| Profil | Erlaubte optionale Ausgaben |
|---|---|
| Eigene Einstellungen (`Custom`) | Feed, Rückblick und Zuschauerausgaben nach den bestehenden Einstellungen. Standard für bestehende Configs. |
| Live (`Live`) | Rückblick und konfigurierte Zuschauerausgaben. |
| Nur Aufnahme (`Recording`) | Manuell freigegebener Feed und konfigurierter Rückblick. |
| Privat spielen (`Private`) | Privates, aufnahmegeschütztes HUD nach der HUD-Einstellung. Beim Wechsel wird der aktuelle Rückblickpuffer verworfen. |


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
| `Overlay / FramesPerSecond` | `15` | Zielrate des RecordingFeeds bei Freigabe. |
| `Overlay / RecordingFeed` | `false` | Erlaubnis zur manuellen Sitzungsfreigabe des aufnehmbaren Originalfensters. |
| `Capture / ObsWatch` | `true` | Privater Hinweis bei erkannten Aufnahmeprogrammen und Schutzproblemen. |
| `Capture / DelayedReplay` | `false` | Originalsicht für einen späteren Bildexport im Speicher puffern. |
| `Viewers / BrowserSource` | `false` | Lokale OBS-Browserseite mit Rollenübersicht nach Rundenende. |
| `Viewers / Port` | `18765` | Port auf 127.0.0.1. Port 8765 ist reserviert. |
| `Viewers / Explanation` | `false` | Rollenfreie Zuschauer-Erklärung im Spiel anzeigen. |
| `Viewers / ExplanationPosition` | `Top` | Erklärung oben oder unten (`Bottom`) platzieren. |
| `Diagnostics / Enabled` | `false` | Diagnose für maximal 180 Sekunden, ohne Lockerung von Fokus oder Aufnahmeschutz. |

Die früheren Optionen `ShuffleMode`, `ShuffleGhostInfo` und `ShowRoundRecap` entfallen und werden aus der Config entfernt. Die Datei `smokescreen_overlay_test.txt` wird vollständig ignoriert. Explizite Diagnoseargumente sind `--smokescreen-diagnostics` und `"--smokescreen-dump=C:\absoluter\Diagnoseordner"`. Bildexporte enthalten private Informationen und erfolgen nur mit explizitem Dump-Argument. Höchstens 48 Proben (privates Renderziel und öffentlicher Backbuffer desselben Frames) werden innerhalb von 180 Sekunden gespeichert. Im Diagnosemodus wird auch ohne Vordergrund aufgenommen und gemessen; das private Fenster bleibt dann verborgen.

Die Menüseite **Privatsphäre** startet bei neuer Konfiguration mit **Sicht: Original**, **Chat: Privat** und **Rollentöne: Stumm**. Alle drei Einstellungen lassen sich im Spiel ändern. **Meeting privat** und **Leckwächter** sind standardmäßig eingeschaltet. Bekannte TOR-Fähigkeitstöne werden für die lokale Audioausgabe stummgeschaltet. Du hörst sie bei dieser Einstellung ebenfalls nicht. Öffentliche Bombentöne und Alarme bleiben erhalten. Der Leckwächter meldet verdächtige sichtbare Textstichproben im privaten HUD, beispielsweise Rollennamen oder Rollenfarben. Bestätigte Ausnahmen speichern eine Prüfsumme des exakten Texts. Der Wächter kann Treffer bei öffentlich erlaubten Texten melden und Lecks außerhalb der Stichprobe übersehen.

Der Schutzstatus im Menü und privaten HUD berücksichtigt Maskenrender, Sichtschutz und Windows-Fenstersperre. Die Anzeige unterscheidet eine veraltete oder fehlende Ausgabe und ein absichtlich verborgenes HUD. Die tatsächlich verwendete OBS-Quelle bleibt separat zu prüfen.

## Technik und Grenzen

Die Maskierung gilt einmal pro Frame vom ersten Kamerarender bis zum Frameende nach allen Bildschirmkameras. Ein Restore in Update und FixedUpdate sowie vor der TOR-Logik sichert ausgelassene Frameabschlüsse ab. Nur geänderte Texte erhalten erzwungene Meshupdates. Update, Physik und Rollenlogik behalten ihren echten Zustand. Der private Kamerastapel schreibt in eine wiederverwendete RenderTexture. Ein Unity-Renderereignis kopiert diese auf dem D3D11-Gerät des Spiels in eine DirectComposition-Fläche des geschützten Fensters. BeginDraw und EndDraw aktualisieren die gesamte Fläche. Die private Ausgabe ruft weder Present noch Present1 auf. Für Diagnosebilder, freigegebene Feeds und den optionalen Rückblick kopiert D3D11 die Bilder in separate Lesetexturen. Der Renderthread prüft die Kopie später mit DO_NOT_WAIT und liest nur fertig übertragene Bilder. Fokusverlust und fehlender Aufnahmeschutz blenden die private Ausgabe aus.

Für Sichtschatten und Kameramonitore auf Skeld, Polus und Fungle verwendet der private Durchlauf eigene Renderziele. Beim Kamerawechsel ermittelt Smokescreen zusätzlich Renderer, deren erstes Material ein Kameraziel als `_MainTex` verwendet. Kamerakomponenten anderer Mods mit eigenen Render-Callbacks können im Privatbild fehlen. Die Kamerakopie übernimmt keine fremden CommandBuffer. Deren Effekte können im Privatbild fehlen. Aktive Screen-Space-Overlay-Canvas werden für den privaten Durchlauf an die letzte Bildschirmkamera gebunden. IMGUI wird von Unity außerhalb des Kamerastapels gezeichnet und fehlt im Privatbild. Für die Bedienung solcher Menüs lässt sich das Privatfenster mit F8 ausblenden. Neue Rollenobjekte anderer Modversionen sind nicht automatisch vollständig abgedeckt. Sichtbare Handlungen wie Töten, Verwandeln, Bewegung und öffentlich sichtbare Effekte bleiben mögliche Informationsquellen. Die öffentlich feste Rolle ändert die tatsächliche Spielrolle nicht.


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

Eine gültige Registrierung liefert ein Token ungleich 0. Registrierungen bleiben bis zur Abmeldung oder Zerstörung des Objekts erhalten und gelten über Rundenwechsel hinweg. Pro Ziel eine Registrierung verwenden. Objekte bleiben in Spiel- und Rollenlogik unverändert. Im geschützten privaten HUD werden die Originale durch den vollständigen Kamerastapel gezeigt. Der explizit freigegebene Originalfeed und der optionale Rückblick können die Originale enthalten. `IsMaskActive` meldet, ob der aktuelle Spielzustand in den Schutzbereich fällt und die Kernpatches geladen sind. Für Informationen, die auch ohne Smokescreen, in der Lobby oder außerhalb geschützter Runden privat bleiben müssen, muss der Integrator ihre Sichtbarkeit selbst steuern.

Die API unterstützt Unity-Renderer auf den vorhandenen Haupt- und UI-Kamerapfaden. Camera-Canvas- und Screen-Space-Overlay-Elemente mit `CanvasRenderer`, `TextMeshProUGUI`, IMGUI, Audio und Ausgaben außerhalb des Spielfensters liegen außerhalb dieses Vertrags.

## Installation und Build

TOR und BepInEx/Il2Cpp müssen bereits installiert sein. `UnknownsSmokescreen.dll` kommt nach `BepInEx/plugins`. Nur der lokale Spieler braucht die Mod. Sie sendet keine eigenen RPCs.

`dotnet build -c Release` erzeugt `bin/Release/net6.0/UnknownsSmokescreen.dll`. Das Projekt besitzt keine Deploy- oder Kopierziele in eine Spielinstallation.

GPL-3.0-or-later, siehe [LICENSE](LICENSE).
