## What and why / Was und warum

<!-- Short description; link the issue, e.g. "Fixes #12". / Kurze Beschreibung; Issue verlinken, z. B. "Fixes #12". -->

## Tested on / Getestet auf

<!-- Motherboard, CPU, memory, Windows version. / Mainboard, CPU, RAM, Windows-Version. -->

## Checklist / Checkliste

- [ ] Builds with `setup\build.ps1` / Baut mit `setup\build.ps1`
- [ ] Tested on real hardware (needed for changes to hardware access) / An echter Hardware getestet (nötig bei Änderungen am Hardwarezugriff)
- [ ] SMBus access stays limited to the ENE addresses 0x70–0x77 / SMBus-Zugriff bleibt auf die ENE-Adressen 0x70–0x77 beschränkt
- [ ] User-facing texts in English and German (`Loc.T`) / Texte in der Oberfläche auf Englisch und Deutsch (`Loc.T`)
- [ ] `docs/hardware.md` and `docs/hardware.de.md` updated if the hardware protocol changed / bei geändertem Hardwareprotokoll aktualisiert
