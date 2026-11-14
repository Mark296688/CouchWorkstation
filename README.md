# Couch Workstation

Ein großer Fernseher als Arbeitsmonitor, bedient vom Sofa aus mit einer **Nintendo Wii Remote**:
zeigen = Mauszeiger, **A** = Linksklick, **B halten** = diktieren (Push-to-Talk). Der erkannte Text
landet im gerade fokussierten Textfeld – ohne automatisches Enter.

Die App läuft nur, wenn sie gestartet wird (kein Dienst, kein Autostart).

Entstanden für das Mathe-Lernspiel [MathQuest](https://github.com/Mark296688/MathQuest), funktioniert aber mit
jedem Programm, das sich mit Maus und Texteingabe bedienen lässt. Hobbyprojekt: kostenlos, ohne Gewähr und ohne
Zusage für Support oder Weiterentwicklung (MIT-Lizenz).

## Herunterladen

Unter **Releases** liegt `CouchWorkstation-win-x64.zip`: entpacken, `CouchWorkstation.exe` starten. Die .exe
bringt .NET mit; es muss nichts installiert werden. Für MathQuest den Ordner `CouchWorkstation` neben den
MathQuest-Ordner legen, dann startet MathQuest sie mit.

Windows warnt beim ersten Start eventuell („Der Computer wurde durch Windows geschützt“), weil die .exe nicht
signiert ist: „Weitere Informationen“ → „Trotzdem ausführen“.

## Architektur

```
Wii Remote ──Bluetooth──► DolphinBar (Modus 4, USB-HID) ──► WiiRemote.cs (Report 0x33, ~95 Hz)
                                                                 │
                     ┌───────────────────────────────────────────┴──────────────┐
                     ▼                                                          ▼
   IrTracker (2 IR-Punkte → Zielpunkt,                              ButtonRouter (config: Taste → Aktion)
   Neigung herausgerechnet)                                           │A → LeftClickAction (SendInput)
          ▼                                                           │B → PushToTalkAction
   PointerMapping (Kalibrierung, Kamera-Sichtfeld)                    │1 → RecordPointerTrace (Diagnose)
          │                                                           │+/− → KeyAction (Numpad +/−)
          ▼                                                           ▼
   One-Euro-Filter + Ruhezone → SendInput (absolut)          HandySpeechBackend
                                                                 hält Handys PTT-Hotkey (Strg+Leertaste)
                                                                 solange B gedrückt ist
                                                                      ▼
                                                     Handy: Mikrofon → lokale Spracherkennung → Strg+V
```

| Baustein | Datei |
|---|---|
| HID-Zugriff (SetupAPI, CreateFile, Overlapped I/O) | `src/CouchWorkstation/Wii/HidDevice.cs` |
| Wii-Protokoll, IR-Init, Reconnect | `src/CouchWorkstation/Wii/WiiRemote.cs` |
| IR → Zielpunkt, Neigungskorrektur | `src/CouchWorkstation/Pointer/IrTracker.cs` |
| Abbildung auf den Bildschirm, Kalibrierung | `src/CouchWorkstation/Pointer/PointerMapping.cs` |
| Glättung | `src/CouchWorkstation/Pointer/OneEuroFilter.cs` |
| Cursor, Statistik, Ruhezone | `src/CouchWorkstation/Pointer/PointerController.cs` |
| Tastenbelegung / Aktionen | `src/CouchWorkstation/Pointer/ButtonRouter.cs` |
| Handy-Anbindung + Latenzmessung | `src/CouchWorkstation/Speech/HandySpeechBackend.cs`, `HandyLogWatcher.cs` |
| Displays (Namen, Auflösung, Hz, DPI) | `src/CouchWorkstation/Platform/Displays.cs` |
| Maus/Tastatur (SendInput) | `src/CouchWorkstation/Platform/Input.cs` |
| Statusfenster, Kalibrierung | `src/CouchWorkstation/UI/` |
| Diagnose-Tool (Rohdaten der Wii Remote) | `tools/WiiProbe/` |

Pointer, Tasten, Spracherkennung und Mikrofon sind getrennt: Ein anderes Mikrofon oder eine andere
Speech-Engine erfordert keine Änderung am Wii-Code.

## Voraussetzungen

- Windows 11 (x64), .NET 10 SDK (zum Bauen) bzw. .NET 10 Desktop Runtime (zum Ausführen)
- Mayflash DolphinBar per USB, Wii Remote (ohne MotionPlus-Pflicht)
- [Handy](https://github.com/cjpais/Handy) installiert (`%LOCALAPPDATA%\Handy\handy.exe`)
- Keine zusätzlichen Treiber, keine Admin-Rechte

## Bauen und Starten

```powershell
cd src\CouchWorkstation
dotnet build -c Release
```

Start: **`CouchWorkstation.cmd`** im Projektordner (Doppelklick). Beenden über „Beenden“ im
Statusfenster – dabei werden IR-Kamera abgeschaltet, gedrückte Tasten losgelassen und alle Handles
geschlossen. Handy läuft weiter (es wird beim Start gestartet, falls es nicht läuft).

## DolphinBar und Wii Remote

1. DolphinBar per USB anschließen und mit der **Mode-Taste auf Modus 4** stellen (LED 4).
   In Modus 4 reicht die DolphinBar die Wii Remote unverändert als HID-Gerät durch
   (`VID_057E PID_0306`, je Slot ein Interface). Nur so kommen IR-Rohdaten und alle Tasten an.
   Modus 1 (eingebaute Maus-Emulation) wird nicht verwendet.
2. Wii Remote koppeln: **1 + 2 gleichzeitig** drücken (oder roter Sync-Knopf im Batteriefach).
   Die Kopplung bleibt in der DolphinBar gespeichert; später reicht ein beliebiger Tastendruck zum Aufwecken.
3. Die App findet den belegten Slot selbst und verbindet sich nach Ausschalten/Akkuwechsel automatisch neu.
4. Wii Remote ausschalten: Power-Taste gedrückt halten.

Ob die DolphinBar oben, unten oder kopfüber montiert ist, spielt keine Rolle – die Kalibrierung ermittelt
Richtung und Lage automatisch.

## Kalibrierung

Beim ersten Verbinden (oder über „Kalibrieren“) erscheint auf dem Fernseher:

1. Auf das Kreuz in der **Mitte** zeigen → **A**
2. Auf das Kreuz **unten links** zeigen → **A**

Abbrechen mit **Home** oder Esc.

**Warum nur zwei Punkte und warum unten?** Die IR-Kamera der Wii Remote sieht nur ca. ±16° × ±12°.
Bei einer Bar unter einem großen Fernseher (gemessen: 65", 3 m Abstand) verschwindet die Bar aus dem
Kamerabild, sobald man physisch auf die obere Bildhälfte zielt. Die App misst deshalb die natürliche
Zielrichtung (Mitte) und die Empfindlichkeit (unten links, dort bleibt die Bar sicher sichtbar), und
verschiebt die Abbildung dann so, dass **jeder Bildschirmrand erreichbar bleibt, ohne dass die Bar das
Kamerabild verlässt**. Horizontal entspricht das weitgehend 1:1, vertikal zielt man für den oberen
Rand etwas tiefer, als der Cursor erscheint – wie bei der Wii.

Die Neigung (Roll) der Wii Remote wird aus der Lage der beiden IR-Punkte herausgerechnet.

## Handy-Integration (Spracherkennung)

Es wird **keine eigene Spracherkennung** installiert. Handy erledigt Mikrofon, lokale Transkription
(Modell: Nemotron 3.5 ASR Streaming 0.6B, GPU via Vulkan) und das Einfügen in das fokussierte Feld.

- Die App liest Handys Push-to-Talk-Hotkey aus `%APPDATA%\com.pais.handy\settings_store.json`
  (aktuell `ctrl+space`) und hält ihn gedrückt, solange B gedrückt ist.
- Alternative (Config `Speech.HandyTrigger = "Cli"`): `handy.exe --toggle-transcription`.
- Relevante Handy-Einstellungen (angepasst):
  - `push_to_talk: true`, `auto_submit: false` (kein Enter)
  - `selected_language: "de"`
  - `model_unload_timeout: "hour1"` – Modell bleibt 1 h geladen
  - `lazy_stream_close: true` – Mikrofon bleibt nach einem Diktat 30 s offen, Folgediktate starten sofort
  - `clipboard_handling: "dont_modify"` – der vorherige Inhalt der Zwischenablage bleibt erhalten
- Die App verfolgt Handys Log (`%LOCALAPPDATA%\com.pais.handy\logs\handy.log`) und misst damit
  „Mikrofon bereit“ und „B losgelassen → Text eingefügt“.

Gemessen (Iris Xe, 16 GB RAM): B losgelassen → Text eingefügt **470–640 ms**, Mikrofon bereit nach
B gedrückt **30–190 ms** (erstes Diktat nach Start: ~640 ms).

## Mikrofon

Handy verwendet das **Windows-Standard-Aufnahmegerät** (`selected_microphone: null`). Ist ein
Funkmikrofon eingesteckt und Windows-Standard, nimmt Handy dieses; ohne es nimmt Windows/Handy automatisch das
interne Mikrofon (MathQuest kann das Funkmikrofon beim Start zum Standard machen). Ein festes Mikrofon lässt sich in Handys Einstellungen wählen. Die App selbst greift
nie auf ein Mikrofon zu.

## Konfiguration

`%APPDATA%\CouchWorkstation\config.json` (Button „Konfiguration“ im Statusfenster). Änderungen
bei geschlossener App vornehmen.

| Schlüssel | Bedeutung |
|---|---|
| `TargetDisplay`, `TargetDisplayName` | Ziel-Display (Auswahl im Statusfenster). Fehlt es, wird das größte Nicht-Primär-Display genommen. |
| `Pointer.Calibration` | Ergebnis der Kalibrierung (`null` = unkalibriert) |
| `Pointer.SmoothingMinCutoff` | Glättung in Ruhe (kleiner = ruhiger, aber träger) |
| `Pointer.SmoothingBeta` | Wie schnell die Glättung bei Bewegung nachlässt (größer = weniger Nachziehen) |
| `Pointer.SmoothingDCutoff` | Glättung der Geschwindigkeitsschätzung |
| `Pointer.StillRadiusPercent` | Ruhezone in % der Bildbreite: Cursor steht still, bis die Hand sich weiter bewegt |
| `Pointer.ClickFreezeMs` | Cursor hält nach A-Druck kurz still (der Tastendruck verwackelt sonst den Klick) |
| `Pointer.CameraMargin` | Mindestabstand der Bar zum Kamerabildrand (Kamerapixel) |
| `Pointer.DefaultHalfRangeX`, `InvertX/Y` | Nur ohne Kalibrierung |
| `Buttons` | Taste → Aktion. Tasten: `A, B, One, Two, Minus, Plus, Home, Up, Down, Left, Right`. Aktionen: `LeftClick`, `PushToTalk`, `RecordPointerTrace`, `Key:<Tasten>` (gedrückt halten wie die Taste, Schreibweise wie Handys Kürzel, z. B. `Key:f13`; dazu `plus`/`minus` für Numpad + und −, im Standard auf Plus und Minus, MathQuest: Ja/Nein), `None` |
| `Speech.HandyTrigger` | `Hotkey` (Standard) oder `Cli` |
| `Speech.HandyHotkeyOverride` | Hotkey statt des aus Handy gelesenen |
| `Speech.StartHandyIfNotRunning` | Handy beim Start mitstarten |
| `DebugLogging` | Ausführliches Log (rohe IR-Punkte und Bildschirmkoordinaten, 5×/s) |

## Logging und Diagnose

Logs: `%LOCALAPPDATA%\CouchWorkstation\logs\couch-JJJJMMTT.log` (Button „Log-Ordner“), 7 Tage
aufbewahrt, max. 50 MB/Tag. Enthalten u. a.:

- Displays (Name, Auflösung, Hz, Skalierung, Position), Ziel-Display, Kalibrierung/Abbildung
- Wii: gefundener DolphinBar-Slot, Akku, Verbindungsverlust/Reconnect
- alle 5 s: Reports/s, IR-Sichtbarkeit, Cursor-Updates/s, Verarbeitungszeit Report → SendInput, Neigung
- jeder Tastendruck (A/B DOWN/UP), Speech-Zeitpunkte und Latenzen, Fehler

**Taste 1** zeichnet 30 s Pointer-Rohdaten als CSV (`pointer-trace-*.csv`) auf – Grundlage für das
Feintuning der Glättung.

`tools/simfilter.py <trace.csv>` spielt eine Aufnahme durch verschiedene Glättungs-Einstellungen und vergleicht Zittern im Stillstand gegen Rückstand bei Bewegung.

`tools/WiiProbe` gibt die Rohdaten der Wii Remote auf der Konsole aus
(`dotnet run -- 60 wait`: startet nach dem ersten Tastendruck, 60 s).

## Troubleshooting

| Problem | Lösung |
|---|---|
| „Wii Remote: Nicht verbunden“ | Taste an der Wii Remote drücken; DolphinBar auf Modus 4? Sonst neu koppeln (1+2). |
| „IR: Kein Signal“ | Wii Remote Richtung DolphinBar halten; nichts verdeckt die Bar; starke IR-Quellen (Sonne, Halogen) vermeiden. |
| Cursor zittert / zieht nach | `SmoothingBeta`, `StillRadiusPercent` anpassen oder mit Taste 1 eine Messung aufzeichnen. Fernseher auf 60 Hz und Bildmodus „Spiel“. |
| Oberer/unterer Rand schwer erreichbar | Neu kalibrieren, dabei ganz natürlich auf die Mitte zielen. |
| B tut nichts | Läuft Handy (Statusfenster)? Hotkey in Handy geändert → App neu starten. |
| Text kommt nicht an | Ziel-Fenster mit Admin-Rechten? Windows blockiert dann simulierte Eingaben (UIPI). |
| Strg „hängt“ nach Absturz | Einmal Strg drücken; die App löst hängende Hotkey-Tasten beim nächsten Start automatisch. |
| Fernseher nur 30 Hz | Einstellungen → System → Bildschirm → Fernseher → Erweiterte Anzeige → 60 Hz. |

## Bewusst nicht in Version 1

Rechtsklick, Scrollen, weitere Tastenbelegungen, mehrere Fernseher/Wii Remotes, Sprachkommandos.
Die Architektur ist dafür offen: neue Aktionen sind eine Klasse in `ButtonRouter.cs` plus ein Eintrag
in `Buttons`; das Ziel-Display ist austauschbar; `WiiRemote` kapselt genau einen DolphinBar-Slot.

## Lizenz

[MIT](LICENSE). „Wii“ und „Wii Remote“ sind Marken von Nintendo; dieses Projekt hat mit Nintendo nichts zu tun.
[Handy](https://github.com/cjpais/Handy) ist ein eigenes Projekt mit eigener Lizenz.
