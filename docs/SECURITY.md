# Security policy

[Deutsch](#sicherheitsrichtlinie)

## Supported versions

Only the latest release gets fixes.

## Reporting a vulnerability

Please do **not** open a public issue. Report it privately instead:
[Security → Report a vulnerability](https://github.com/Th3Tyr0x/borealis/security/advisories/new).

Please include the Borealis version, Windows version, motherboard and what an attacker or a faulty input could do.
You will get an answer as soon as possible; there is no fixed time frame, as this is a spare-time project.

Relevant are in particular:

- the service (runs as LocalSystem) and its named pipe `\\.\pipe\Borealis`,
- hardware access through PawnIO: fan chip, SMBus (memory lighting), USB lighting controller,
- downloads by setup and app (.NET runtime, PawnIO installer with pinned SHA-256).

Hardware damage risks caused by normal use (e.g. a fan curve set too low by the user) are not security issues; please
report them as a normal issue.

---

# Sicherheitsrichtlinie

## Unterstützte Versionen

Nur das neueste Release bekommt Korrekturen.

## Schwachstelle melden

Bitte **kein** öffentliches Issue anlegen, sondern vertraulich melden:
[Security → Report a vulnerability](https://github.com/Th3Tyr0x/borealis/security/advisories/new).

Bitte Borealis-Version, Windows-Version, Mainboard angeben und was ein Angreifer oder eine fehlerhafte Eingabe damit
anrichten könnte. Eine Antwort kommt so bald wie möglich; eine feste Frist gibt es nicht, Borealis ist ein
Freizeitprojekt.

Besonders relevant sind:

- der Dienst (läuft als LocalSystem) und seine Named Pipe `\\.\pipe\Borealis`,
- der Hardwarezugriff über PawnIO: Lüfter-Chip, SMBus (RAM-Beleuchtung), USB-Beleuchtungscontroller,
- Downloads durch Setup und App (.NET-Laufzeit, PawnIO-Installer mit fester SHA-256).

Risiken für die Hardware durch normale Bedienung (z. B. eine vom Benutzer zu niedrig eingestellte Lüfterkurve) sind
keine Sicherheitslücken; bitte als normales Issue melden.
