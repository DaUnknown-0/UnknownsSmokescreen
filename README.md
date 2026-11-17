# Unknown's Smokescreen

Lokaler Stream-Schutz für [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles). In einer geschützten Runde sieht die Kamera immer **Crewmate**. Das private Overlay zeigt dir die echte Rolle und den echten HUD an den üblichen Stellen.

Diese Mod steht in keiner Verbindung zu Innersloth LLC. Among Us und zugehörige Materialien gehören Innersloth LLC.

## Kamera und privates HUD

Die öffentliche Rollenanzeige bleibt fest auf Crewmate. Es gibt keine Rollenwürfelung, keinen Rollenpool, keinen Teamfilter und keinen zeitlichen Wechsel. Das gilt für den Text über dem Kopf, die Taskliste, Rolleninformationen als Geist, das Crew-Intro und den Rückblick. Rollenfarben und Rollenbeschreibungen anderer Teams werden dort nicht gezeigt. Alte Shuffle-Konfiguration wird beim Laden entfernt.

Die privaten Originale bleiben erhalten. Eine zweite Unity-Kamera rendert die vorhandenen Spielkameras mit echten Texturen, Texten, Cooldowns, Tastenhinweisen, Namensschildern, Pfeilen und unterstützten Rollenobjekten. Kameras mit RenderTexture-Ziel liefern dabei zuerst ihren jeweiligen echten oder maskierten Inhalt, bevor das Bildschirm-HUD gerendert wird. Neue aktive Kameras werden vor dem Render mit einem wiederverwendeten Kamerapuffer erfasst.

Aus echtem und maskiertem Bild entsteht ein Differenzbild: Nur abweichende Pixel werden durch echte Bildpixel ersetzt, alles andere bleibt transparent. Dadurch verschwinden auch die öffentlichen Crewmate-Buchstaben unter der echten privaten Rolle vollständig. Es werden keine Knöpfe nachgezeichnet und keine separaten Textkästen verwendet.

Klickflächen und Fähigkeitshotkeys bleiben unverändert. **F8** blendet das private HUD aus oder wieder ein. Die öffentliche Maske und der ausdrücklich aktivierte RecordingFeed laufen weiter. Ausgeschaltete oder durch Verkleidung geänderte Namensschilder werden respektiert. Öffentliche Namen bleiben unabhängig von der eigenen Rolle weiß und ohne Teamzeichen. Das private Bild übernimmt die native Sichtbarkeit, Teamfarben und bekannten Beziehungen aus TOR und geladenen Erweiterungen. Vorhandene UC-Namensadapter werden per Reflection eingebunden.

Im öffentlichen Intro erscheinen Crewmate, die Crew-Farbe und Crew-Beschreibung, der Crew-Sound und eine neutrale Crew-Aufstellung. Das private Bild behält die echte Aufstellung und die echte Rolle. Im öffentlichen Endbild werden Rollenübersicht und Rückblick als Crewmate dargestellt; die tatsächlichen Rollen, Teams und Ereignisse bleiben privat.

## Fenster und Aufnahmeschutz

Das private Fenster ist klickdurchlässig und liegt exakt über dem Client-Bereich des eigenen Unity-Spielfensters. Es erscheint nur, wenn dieses im Vordergrund und nicht minimiert ist. Bei aktivem Schutz wechselt das Spiel in ein randloses Fenster in Desktopgröße. Beim Abschalten werden der vorherige Modus und die vorherige Spielauflösung wiederhergestellt, einschließlich eines zuvor exklusiven Vollbildmodus.

Das private Fenster wird erst angezeigt, wenn Windows `WDA_EXCLUDEFROMCAPTURE` gesetzt hat und `GetWindowDisplayAffinity` den Wert bestätigt. Erforderlich sind Windows 10 ab Version 2004 und aktive Desktop-Komposition. Bei Ablehnung oder späterem Verlust des Schutzes bleibt das Fenster aus. Im Spiel erscheint einmal kurz `Anzeige nicht verfügbar`. Es gibt keinen schwächeren Ersatzmodus.

**Vor dem ersten Stream die verwendeten OBS-Aufnahmearten lokal prüfen.** Spielaufnahme und Fensteraufnahme des Spiels sollen ausschließlich die Crewmate-Fassung sehen; bei Bildschirmaufnahme soll Windows das private Fenster auslassen. Die Windows-Funktion garantiert keinen Schutz gegen jede denkbare Aufnahmemethode. Siehe [Microsoft-Dokumentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).

## Getrennte lokale Aufnahme

`Overlay / RecordingFeed = true` öffnet zusätzlich ein ausdrücklich aufnehmbares Fenster mit dem echten Kamerabild. Es startet minimiert und passt das Bild mit erhaltenem Seitenverhältnis an seine Größe an. Es wird unabhängig vom privaten F8-Schalter aktualisiert, auch wenn das Spiel den Vordergrund verliert und Unity weiter Frames verarbeitet.

Diese Option ist standardmäßig aus. Nur für eine getrennte lokale Aufnahme verwenden. Das Fenster gehört niemals in die Live-Szene und muss vom gestreamten Bildschirm ferngehalten werden.

## Einstellungen

Datei: `BepInEx/config/com.daunknown.smokescreen.cfg`.

| Einstellung | Standard | Bedeutung |
|---|---|---|
| `General / Enabled` | `true` | Schutz aktivieren. |
| `General / OnlyInStreamerMode` | `true` | Nur bei aktiviertem Streamer-Modus schützen. |
| `Mask / HideRoleButtons` | `true` | Rollenknöpfe öffentlich ausblenden. |
| `Mask / HideRoleFlashes` | `true` | Rollenhinweise nur privat darstellen. |
| `Recap / ShowRoundRecap` | `true` | Rückblick mit öffentlichem Crewmate und privaten echten Details. |
| `Overlay / PrivateOverlay` | `true` | Privates echtes HUD anzeigen. |
| `Overlay / ToggleKey` | `F8` | Privates Overlay einblenden und ausblenden. |
| `Overlay / FramesPerSecond` | `30` | Zielbildrate von 5 bis 30. Die tatsächliche Rate hängt von GPU, Auflösung und Szene ab. |
| `Overlay / RecordingFeed` | `false` | Getrenntes aufnehmbares Wahrheitsfenster. |
| `Diagnostics / Enabled` | `false` | Diagnose für maximal 180 Sekunden, ohne Lockerung von Fokus oder Aufnahmeschutz. |

Die früheren Optionen `ShuffleMode` und `ShuffleGhostInfo` entfallen und werden aus der Config entfernt. Die Datei `smokescreen_overlay_test.txt` wird vollständig ignoriert. Explizite Diagnoseargumente sind `--smokescreen-diagnostics` und `"--smokescreen-dump=C:\absoluter\Diagnoseordner"`. Bildexporte enthalten private Informationen und erfolgen nur mit explizitem Dump-Argument. Höchstens zwölf Bildsätze werden innerhalb von 180 Sekunden gespeichert.

## Technik und Grenzen

Die Maskierung gilt nur innerhalb reversibler Kamerarender-Transaktionen. Update, Physik und Rollenlogik behalten ihren echten Zustand. Der vorliegende Il2Cpp-Build stellt `AsyncGPUReadback` nicht bereit. Der Fallback nutzt `ReadPixels` und nichtgenerischen direkten Zugriff auf wiederverwendete Texturpuffer. Es gibt keine pixelweisen Il2Cpp-Zugriffe, generischen AOT-Aufrufe für den Readback oder neuen Pixelarrays pro Bild.

Renderziele und CPU-Texturen tragen `HideFlags.DontUnloadUnusedAsset` und werden ausdrücklich freigegeben. Die Pixelzahl ist auf das Äquivalent von 3840×2160 begrenzt. Für die beiden zusätzlichen Renderdurchläufe und den synchronen Readback werden dauerhaft 30 fps nicht auf jedem System erreicht. Ein Overlay mit begrenzter Bildrate kann bei Bewegung zeitlich hinter einem schnelleren Spielbild liegen.

Eigene Kamera-Nachbearbeitung, komplexe abhängige Texturkameras und UI mit `ScreenSpaceOverlay` können zusätzliche Integration benötigen. Neue Rollenobjekte anderer Modversionen sind nicht automatisch vollständig abgedeckt. Sichtbare Handlungen wie Töten, Verwandeln, Bewegung und öffentlich sichtbare Effekte bleiben mögliche Informationsquellen. Die öffentlich feste Rolle ändert die tatsächliche Spielrolle nicht.

## Installation und Build

TOR und BepInEx/Il2Cpp müssen bereits installiert sein. `UnknownsSmokescreen.dll` kommt nach `BepInEx/plugins`. Nur der lokale Spieler braucht die Mod. Sie sendet keine eigenen RPCs.

`dotnet build -c Release` erzeugt `bin/Release/net6.0/UnknownsSmokescreen.dll`. Das Projekt besitzt keine Deploy- oder Kopierziele in eine Spielinstallation.

GPL-3.0-or-later, siehe [LICENSE](LICENSE).
