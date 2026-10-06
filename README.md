# AI Control

Eine lokale Windows-App für ComfyUI und den llama.cpp-Coding-Server. Die Oberfläche ist in HTML, CSS und TypeScript gebaut und läuft in WebView2. C#/.NET 10 übernimmt die Serversteuerung, Einstellungen, Ordnerauswahl und den Einstieg in VS Code.

## Benutzen

Die Desktop-Verknüpfung **AI Control** öffnet die App. Die frühere Verknüpfung **ComfyUI Control** zeigt ebenfalls auf die neue App.

- **Übersicht:** Status beider Dienste, Starten, Beenden, Protokolle und Installationsordner.
- **Bild & Video:** ComfyUI direkt in der App oder im Standardbrowser öffnen. Vorhandene Workflows, Modelle und Ergebnisse werden weiterverwendet.
- **Coding:** Eines der vorhandenen GGUF-Modelle auswählen, den Server starten und den Chat direkt in der App öffnen. Ein Coding-Projekt auswählen und in VS Code öffnen.
- **Einstellungen:** Installationsordner, Ports, Kontextgröße und GPU-Zuteilung anpassen. Beide Dienste vorher stoppen.

Die App startet einen Dienst erst, wenn der andere gestoppt ist. Beide Dienste teilen sich die GPU. Ein Wechsel bricht keine Aufträge automatisch ab. **Beenden** wartet im Normalfall auf Leerlauf. Bei laufenden oder unbekannten Aufträgen kannst du ausdrücklich **Nach allen Aufträgen beenden** oder **Sofort beenden und abbrechen** auswählen. **Aufheben** nimmt ein vorgemerktes Beenden zurück.

Fenster schließen lässt die Server weiterlaufen. Für vorgemerktes Beenden muss das Kontrollfenster geöffnet bleiben; beim Schließen erscheint ein Hinweis.

Der alte AUTOMATIC1111-Server und seine Modelle bleiben erhalten. Sie werden von dieser Version nicht verwaltet.

## Coding-Assistent anbinden

Der Coding-Server stellt standardmäßig `http://127.0.0.1:8080/v1` bereit und verwendet die Modell-ID `local-coder`. Wähle in Cline oder einem kompatiblen Editor-Assistenten den Anbieter **OpenAI Compatible**, diese API-Adresse und diese Modell-ID. Der lokale Server verlangt keinen echten Cloud-API-Schlüssel; falls der Client einen nichtleeren Wert voraussetzt, genügt ein Platzhalter wie `local`.

Die App startet den lokalen Server und öffnet VS Code. Sie installiert oder konfiguriert keine Editor-Erweiterung automatisch. Der eingebaute Coding-Chat ist die vorhandene llama.cpp-Weboberfläche; er erhält keinen Zugriff auf Windows-Steuerungsfunktionen der App.

## Bauen

Voraussetzungen: Windows 10/11, .NET SDK 10, Node.js/npm sowie die Microsoft Edge WebView2 Runtime. Zum Ausführen der App reichen die .NET Desktop Runtime 10 (x64) und WebView2; Node.js wird nur beim Entwickeln benötigt.

```powershell
npm ci
.\Build.ps1 -Verify -Publish
.\Install-Shortcut.ps1
.\Start-App.cmd
```

Die App liegt anschließend in `artifacts\app\AIControl.exe`. Den vollständigen `artifacts\app`-Ordner zusammenhalten; er enthält auch die Weboberfläche und Startskripte. Abhängigkeiten sind über `package-lock.json` und die NuGet-Lockdatei festgehalten.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `src/AIControl.Core` | Konfiguration, HTTP-Statusabfragen, Auftragsprüfung und Start-/Stop-Schnittstelle |
| `src/AIControl.Host` | Windows-Fenster, WebView2, geprüfte Nachrichten-Schnittstelle und Dienstskripte |
| `web/app.ts` | Frontend-Logik; wird nach `src/AIControl.Host/Web/app.js` kompiliert |
| `src/AIControl.Host/Web` | HTML, CSS und Symbol der Oberfläche |
| `tests` | C#-Prüfungen, Prozessidentität und Browser-Tests mit einer simulierten Host-Schnittstelle |
| `legacy/ComfyUIControl` | Gesicherter Quellcode der ursprünglichen kleinen WPF-App |
| `docs` | Architektur und geprüfter Stand |

Modelle, Python-Umgebungen, llama.cpp-Binaries und Nutzerprojekte bleiben außerhalb dieses Repositorys. Git enthält keine Gewichte, Tokens oder persönlichen Laufzeitdaten. Die Einstellungen und Protokolle liegen unter `%LOCALAPPDATA%\AIControl`.

Quellcode: [pezezzle/AI-Control](https://github.com/pezezzle/AI-Control). Der lokale Installationsort kann beispielsweise `%USERPROFILE%\Coding\AI-Control` sein.
