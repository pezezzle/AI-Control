# ComfyUI Control

Die Desktop-Verknüpfung **ComfyUI Control** öffnet die kleine Steuerung.

- **Starten** startet die bestehende ComfyUI-Installation im Hintergrund.
- **Im Browser öffnen** öffnet http://127.0.0.1:8188 im Standardbrowser.
- **Beenden** stoppt ComfyUI. Bei laufenden oder vorgemerkten Aufträgen kannst du auf alle Aufträge warten oder sofort abbrechen.
- **Aufheben** nimmt ein vorgemerktes Beenden zurück.

Das Schließen des Fensters lässt ComfyUI weiterlaufen. Für „Nach allen Aufträgen beenden“ muss das Kontrollfenster geöffnet bleiben; beim Schließen erscheint ein Hinweis.

Die Anzeige wird alle zwei Sekunden aktualisiert. Bei Problemen öffnet **Protokoll öffnen** das passende Protokoll. Die App beendet nur die vorhandene ComfyUI-Installation; die bestehenden Skripte prüfen dazu den Prozess auf Port 8188.

Programm: `publish\ComfyUIControl.exe`. Benötigt die bereits installierte .NET Desktop Runtime 10 (x64). Keine zusätzliche Python-Umgebung und keine Änderungen an Modellen oder Workflows.

Neu bauen:
```
dotnet publish ComfyUIControl.csproj -c Release -o publish
dotnet run --project Tests\ComfyUIControl.Tests.csproj -c Release
```
