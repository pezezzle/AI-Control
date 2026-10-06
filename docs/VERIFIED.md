# Geprüfter Stand – 6. Oktober 2026

Version 0.2 wurde auf einem Windows-PC mit RTX 4090 geprüft.

## Automatische Prüfungen

`Build.ps1 -Verify` läuft erfolgreich: Build ohne Warnungen oder Fehler, 41 C#-Prüfungen, sechs Prüfungen zur Prozessidentität, eine Prüfung mit einem echten kurzlebigen Testprozess und sechs Browser-Tests. Insgesamt 54 Prüfungen.

Die Prüfungen decken aktive und wartende Aufträge, unbekannte oder fehlerhafte Statusantworten, fremde Prozesse, erneute Auftragsprüfung vor dem Stoppen, gespeicherte Einstellungen, Modellnamen, die Nachrichten-Schnittstelle und die Oberfläche ab. Browser-Tests verwenden eine simulierte Host-Schnittstelle; sie ersetzen nicht die folgenden Integrationstests.

## Echte Dienste und Windows-Oberfläche

- ComfyUI während eines laufenden Nutzerauftrags erkannt. Beenden-Dialog angezeigt und abgebrochen; der Render wurde nicht unterbrochen.
- Nach Abschluss ComfyUI über die neue App beendet und wieder gestartet.
- ComfyUI und die llama.cpp-Chatoberfläche direkt im App-Fenster geöffnet.
- Qwen 2.5 Coder 14B mit 32.768 Tokens Kontext über die App auf der RTX 4090 gestartet. llama.cpp meldete 49 von 49 Modellschichten auf der GPU. Eine kurze synthetische Python-Anfrage wurde korrekt beantwortet.
- Coding-Server anschließend im Leerlauf über die App beendet.
- Zusätzlich Start, Anfrage und Stop des 7B-Modells im CPU-Modus auf einem separaten Testport über den C#-Launcher und Windows PowerShell 5.1 geprüft. Der Testserver wurde beendet.
- Projektordner über den Windows-Dialog gewählt, gespeichert und in VS Code geöffnet.
- Erneuter App-Start aktiviert die vorhandene Instanz.
- Einstellungen, Desktop-Verknüpfungen und Windows-Symbol visuell geprüft.

Das 32B-Modell wird zur Auswahl angeboten, wurde für diese App-Prüfung aber nicht geladen. Eine Editor-Erweiterung wie Cline wurde nicht installiert oder konfiguriert. Die bisherigen Serverinstallationen und Modellgewichte wurden nicht geändert.

Die Belege und Testantworten werden ausschließlich lokal außerhalb des Repositorys aufbewahrt.
