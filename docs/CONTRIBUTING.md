# Contributing

[Deutsch](#mitmachen)

## Hardware reports

Borealis is only tested on one system (see the README). Reports from other boards help most. Please open an
[issue](https://github.com/Th3Tyr0x/borealis/issues/new) with:

- motherboard model and BIOS version,
- CPU and memory modules (model, number of modules),
- what is connected to the ARGB headers and fan headers,
- what works and what does not (lighting per device, fan speeds, temperatures, fan curves, auto tuning),
- the service log from `C:\ProgramData\Borealis\logs` (remove anything personal first).

## Bugs and ideas

Open an issue and describe what you did, what you expected and what happened. For security problems see
[SECURITY.md](SECURITY.md).

## Code

- Build with the .NET 10 SDK: `setup\build.ps1` (details in the README).
- Keep pull requests small and focused on one change.
- Test changes to hardware access (`src/Borealis.Core/Hardware`, `Lighting`) on real hardware and say on which.
  Never extend SMBus access beyond the ENE addresses 0x70–0x77.
- How the hardware is addressed is described in [hardware.md](hardware.md).
- By contributing you agree that your contribution is licensed under GPL-3.0-or-later.

---

# Mitmachen

## Hardware-Rückmeldungen

Borealis ist nur an einem System getestet (siehe README). Rückmeldungen von anderen Boards helfen am meisten. Bitte ein
[Issue](https://github.com/Th3Tyr0x/borealis/issues/new) anlegen mit:

- Mainboard-Modell und BIOS-Version,
- CPU und RAM-Modulen (Modell, Anzahl),
- was an den ARGB- und Lüfteranschlüssen hängt,
- was geht und was nicht (Beleuchtung je Gerät, Drehzahlen, Temperaturen, Lüfterkurven, Auto-Tuning),
- dem Dienst-Protokoll aus `C:\ProgramData\Borealis\logs` (vorher Persönliches entfernen).

## Fehler und Ideen

Ein Issue anlegen und beschreiben, was du gemacht hast, was du erwartet hast und was passiert ist. Sicherheitsprobleme
siehe [SECURITY.md](SECURITY.md).

## Code

- Bauen mit dem .NET 10 SDK: `setup\build.ps1` (Details im README).
- Pull Requests klein halten, eine Änderung pro Request.
- Änderungen am Hardwarezugriff (`src/Borealis.Core/Hardware`, `Lighting`) an echter Hardware testen und angeben, an
  welcher. SMBus-Zugriffe nie über die ENE-Adressen 0x70–0x77 hinaus erweitern.
- Wie die Hardware angesprochen wird, steht in [hardware.de.md](hardware.de.md).
- Mit einem Beitrag stimmst du zu, dass er unter GPL-3.0-or-later steht.
