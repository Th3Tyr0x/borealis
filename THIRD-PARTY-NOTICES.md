# Third-party components

[Deutsch](#fremde-bestandteile)

## PawnIO modules

`src/Borealis.Core/Hardware/PawnIO/LpcIO.bin`, `AMDFamily17.bin` and `SmbusPIIX4.bin` are unmodified, signed modules
from [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) release 0.2.11 by namazso (SHA-256 of the release
archive `43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4`). They are licensed under the GNU Lesser
General Public License 2.1 or later; the license text is included (`src/Borealis.Core/Hardware/PawnIO/COPYING`, shipped
as `PawnIO-Module-LICENSE.txt`). The module sources are available in that repository.

## PawnIO driver

The [PawnIO](https://pawnio.eu) driver (namazso) is not part of Borealis. Setup and the app optionally download the
official 2.2.0 installer from GitHub (pinned SHA-256) and run it.

## .NET

Borealis runs on Microsoft's .NET 10 Desktop Runtime (MIT license), which setup downloads from Microsoft if needed.

## Inno Setup

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) by Jordan Russell and Martijn Laan (own
license, free for any use).

---

# Fremde Bestandteile

## PawnIO-Module

`src/Borealis.Core/Hardware/PawnIO/LpcIO.bin`, `AMDFamily17.bin` und `SmbusPIIX4.bin` sind unveränderte, signierte
Module aus [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) Release 0.2.11 von namazso
(SHA-256 des Release-Archivs `43608cb89bc84247fef1368a139013f7d043e17db6d6c8dfc9b46bf0905a81f4`). Sie stehen unter der
GNU Lesser General Public License 2.1 oder später; der Lizenztext liegt bei
(`src/Borealis.Core/Hardware/PawnIO/COPYING`, im Programm als `PawnIO-Module-LICENSE.txt`). Die Quelltexte der Module
gibt es im genannten Repository.

## PawnIO-Treiber

Der Treiber [PawnIO](https://pawnio.eu) (namazso) ist nicht Teil von Borealis. Das Setup und die App laden auf Wunsch
den offiziellen Installer 2.2.0 von GitHub (feste SHA-256-Prüfsumme) und führen ihn aus.

## .NET

Borealis läuft auf der .NET-10-Desktop-Laufzeit von Microsoft (MIT-Lizenz), die das Setup bei Bedarf von Microsoft lädt.

## Inno Setup

Der Installer wird mit [Inno Setup](https://jrsoftware.org/isinfo.php) von Jordan Russell und Martijn Laan gebaut
(eigene Lizenz, für freie und kommerzielle Nutzung).
