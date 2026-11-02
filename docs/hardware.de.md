# Hardware-Referenz

[English](hardware.md)

Wie Borealis mit der Hardware spricht: mit dem USB-Beleuchtungscontroller, dem Nuvoton-Lüfter-Chip, den RGB-Controllern
auf den RAM-Modulen und dem CPU-Temperatursensor. Alles hier wurde am getesteten System ermittelt (siehe README) und ist
in `src/Borealis.Core` umgesetzt. Bytewerte sind hexadezimal.

Kennzeichen: **[beobachtet]** = am laufenden System oder im mitgeschnittenen Verkehr gesehen, **[abgeleitet]** = daraus
geschlossen, nicht direkt bestätigt, **[offen]** = noch unbekannt.

## 1. USB-Beleuchtungscontroller

USB-HID-Gerät `0B05:19AF`, Schnittstelle `MI_02` (Controller „6K75“, Protokollgeneration „Aura50“). Er steuert die festen
Mainboard-Zonen und die adressierbaren ARGB-Header. Code: `Lighting/UsbLedHid.cs`, `Lighting/UsbLedController.cs`.

### 1.1 Transport und Sperre

- Jeder Report hat **65 Byte**: Byte 0 = Report-ID `EC`, Byte 1 = Befehl, Byte 2–64 = Nutzdaten, mit `00` aufgefüllt.
- Schreiben mit `WriteFile`, Lesen mit `ReadFile`. Bei einem Schreibfehler: 1 ms warten, HID-Warteschlange leeren, einmal
  wiederholen.
- **Abfrage ohne Index:** `EC <cmd | 80>` senden, dann lesen, bis eine Antwort mit `[1] = cmd` kommt (Timeout 2 s).
- **Abfrage mit Index:** zwei Writes: `EC <cmd> <idx>` setzt den Index, `EC <cmd | 80>` fordert die Daten an. Dann lesen,
  bis `[1] = cmd` und `[2] = idx`. Immer das Echo prüfen, veraltete Antworten kommen vor. **[beobachtet]**
- Jeder Zugriff läuft unter dem systemweiten Mutex `Global\asusaurausb`, den auch die Herstellersoftware nimmt.

### 1.2 Befehle

| Befehl | Art | Bedeutung |
|---|---|---|
| `30` (gesendet als `EC B0`) | Abfrage | Konfigurationstabelle (§1.3) |
| `31 <unit>` | Abfrage | Unit-Info: Typ `[3]`, LED-Anzahl `[4..5]` (Big-Endian), Ort `[6]` |
| `32 <kanal>` | Abfrage | Zustand eines Steuerkanals (§1.4) |
| `35` | Schreiben | Effekt auf einem Steuerkanal setzen (§1.5) |
| `36` | Schreiben | Farben für einen Firmware-Effekt (§1.5) |
| `38` | Schreiben | Beleuchtung ein/aus: `EC 38 01 <an>`, dann `EC 38 00 <an>` |
| `3B <unit> <hi> <lo>` | Schreiben | LED-Anzahl eines ARGB-Headers setzen (§1.8) |
| `3F 55` | Schreiben | übernehmen und im Controller speichern **[abgeleitet]**; nicht in Schleifen senden |
| `3F AA` | Schreiben | Standard-Effekt der Firmware wiederherstellen **[abgeleitet]** |
| `40` | Schreiben | Direct-Modus: LED-Farben streamen (§1.6) |

### 1.3 Konfiguration und Units

Rohantwort auf `EC B0` beim getesteten Board:

```
EC 30 00 00 1E 9F 03 | 01 00 00 78 3C 00 | 01 00 00 78 3C 00 | 01 00 00 78 3C 00 | 00 … | 03 06 02 01 F4
                  [6] = Anzahl ARGB-Header, danach je 6 Byte: vorhanden, Typ, LED-Anzahl (BE, 0x78 = 120), 3C, 00
```

Das Ende `03 06 02 01 F4` enthält vermutlich die LED-Anzahl des Mainboards und das Maximum von 500 LEDs je Header
**[abgeleitet]**.

Units (`EC 31 <unit>`) beim getesteten Board **[beobachtet]**:

| Unit | Typ | LEDs | Bedeutung |
|---|---|---|---|
| 0, 1 | `10` | 1 | Mainboard, Logo an der Back-I/O |
| 2 | `10` | 1 | Mainboard, 12-V-RGB-Header |
| 3 | `20` | 120 | ARGB-Header 1 |
| 4 | `21` | 120 | ARGB-Header 2 |
| 5 | `22` | 120 | ARGB-Header 3 |

Typ `10` ist eine feste Mainboard-Zone, `2x` ist ARGB-Header *x* + 1.

### 1.4 Kanäle

Es gibt zwei Arten von Kanalnummern:

- **Steuerkanal** für `EC 35`, `EC 36` und `EC 3F`: Die Mainboard-Units liegen auf Kanal **0**, alle ARGB-Header teilen
  sich Kanal **1**. Wird ein Steuerkanal umgeschaltet (etwa in den Direct-Modus), löscht das die Direct-Puffer aller
  Units darauf; sie müssen alle neu gesendet werden. **[beobachtet]**
- **Direct-Kanal** für `EC 40`: ARGB-Header *n* = Kanal *n* − 1 (Typ − `20`), das Mainboard = Kanal **4**. Ältere
  „Aura36“-Controller nutzen Kanal 6 für das Mainboard; Kanal 6 zeigt bei diesem Controller zwar Farben, lässt beim
  Streamen aber alle Kanäle flackern. **[beobachtet]**

Antwort auf `EC 32 <kanal>`:

| Byte | Bedeutung |
|---|---|
| `[4..5]` | Unit-Maske (Bit *n* = Unit *n*) |
| `[6]`, `[7]`, `[8]` | aktueller Effekt, Richtung, Geschwindigkeit |
| `[9..10]` | Unit-Maske des Effekts nach dem Herunterfahren |
| `[11]`, `[12]`, `[13]` | Effekt nach dem Herunterfahren, Richtung, Geschwindigkeit |

### 1.5 Firmware-Effekte

Diese Effekte spielt der Controller selbst ab, auch ohne laufendes Windows. Code: `Lighting/FirmwareEffect.cs`.

```
EC 35 <kanal> 00 <flag> <effekt> <richtung> <tempo>          flag 00 = normal, 01 = nach dem Herunterfahren (§1.7)
EC 36 <maske hi> <maske lo> <flag> RGB × 16                  nur bei Effekten mit Farbe; Slot n = Unit n
EC 3F 55                                                     übernehmen/speichern
```

Ablauf: War der Kanal im Direct-Modus, zuerst `EC 35 <kanal> 00 00 00` (Effekt 0), danach die drei Reports oben.
Ein Firmware-Effekt färbt einen ARGB-Strip ganz in einer Farbe; Farben je LED gibt es nur im Direct-Modus.

| ID | Effekt | Farbe | Tempo | Richtung |
|---|---|---|---|---|
| `00` | aus | | | |
| `01` | statisch | ja | | |
| `02` | atmen | ja | ja | |
| `03` | blinken | ja | ja | |
| `04` | Farbzyklus | | ja | |
| `05` | Regenbogen | | ja | ja |
| `08` | Komet | | ja | ja |
| `0A` | Flash and Dash | | ja | ja |
| `0B` | Welle | | ja | ja |
| `0C` | Glowing Yoyo | | ja | ja |
| `0D` | Sternennacht | | ja | |
| `FF` | Direct-Modus (Steuerung durch Software, §1.6) | | | |

Die Spalte Farbe ist **[beobachtet]**, Tempo und Richtung sind **[abgeleitet]**. Tempowerte der Herstellersoftware:
langsam/normal/schnell = `02`/`01`/`00`, bei Bewegungseffekten `01`/`00`/`FF`.

### 1.6 Direct-Modus

1. Jeden Steuerkanal einmal umschalten: `EC 35 <kanal> 00 00 FF`. Die Firmware gibt den Kanal an die Software ab.
2. Farben je Direct-Kanal streamen:

```
[0] EC
[1] 40
[2] Direct-Kanal | ((start >> 4) & F0) | (letzter Report ? 80 : 00)
[3] erste LED (Low-Byte)
[4] Anzahl LEDs in diesem Report (höchstens 20)
[5..64] R, G, B je LED
```

- Ein Strip mit 120 LEDs braucht 6 Reports. Bit 7 in Byte 2 des letzten Reports übernimmt das Bild, und zwar **je Kanal**:
  Ein Bild ohne dieses Bit wird nicht angezeigt. **[beobachtet]**
- Es wird keine Antwort gelesen, es gibt keinen Handshake.
- Zurück zur Firmware geht es mit einem Firmware-Effekt (§1.5).

### 1.7 Effekt nach dem Herunterfahren

Der Controller speichert je Steuerkanal einen zweiten Effekt. Er läuft, solange der PC aus ist (S3/S5) und das Netzteil
noch Standby-Strom liefert, sofern im BIOS die Beleuchtung im Ruhezustand/Soft-Off eingeschaltet ist.

- Schreiben: wie §1.5 mit Flag `01` in `EC 35` und `EC 36`, dann `EC 3F 55`.
- Zurücklesen: `EC 32 <kanal>`, Byte `[9..13]` (§1.4).
- Nur Firmware-Effekte sind möglich. RAM-Module haben keinen solchen Effekt; sie gehen mit dem System aus.

### 1.8 LED-Anzahl eines ARGB-Headers

- Lesen: `EC 31 <unit>`, Antwort `[4..5]` (Big-Endian). Firmware-Versionen ≤ 200 können das nicht.
- Schreiben:
  1. Direct-Modus auf dem Kanal (§1.6).
  2. Ein schwarzes Bild mit der *alten* LED-Anzahl senden, damit LEDs hinter der neuen Anzahl ausgehen.
  3. `EC 3B <unit> <anzahl hi> <anzahl lo>`.

### 1.9 Timing und andere Software

- Ein HID-Report dauert **≈ 4 ms** (Intervall des Interrupt-Endpunkts), mit oder ohne überlappende Writes. Ein ganzes
  Bild mit 3 × 120 LEDs plus Mainboard sind 19 Reports, also höchstens ≈ 13 Bilder/s. Kürzere LED-Anzahlen erhöhen die
  Rate. **[beobachtet]**
- Borealis sendet alle Kanäle eines Bildes als einen Block unter der Sperre und pausiert danach mindestens 5 ms.
- Flackern beim Streamen hatte zwei Ursachen, beide behoben: das Mainboard auf Direct-Kanal 6 statt 4 und der
  Beleuchtungsdienst des Herstellers, der gleichzeitig seinen eigenen Software-Effekt in dieselben Kanäle streamte.
  Borealis hält diesen Dienst deshalb an, solange es die Beleuchtung steuert. **[beobachtet]**

## 2. Lüfter-Chip (Nuvoton NCT6799D)

Super-I/O-Chip mit der ID `D8xx` an den Konfigurationsports `2E`/`2F`. Er misst Drehzahlen und Temperaturen und regelt
die Lüfterausgänge selbst nach Kurven in seinen Registern. Code: `Hardware/Nct6799.cs`, `Hardware/SensorReader.cs`.

### 2.1 Zugriff und Sperren

- Port-I/O läuft über das PawnIO-Modul `LpcIO`.
- Der Hardware-Monitor liegt an der Basisadresse aus LDN `0B`, Register `60`/`61` (`290` beim getesteten Board).
  Indexport = Basis + 5, Datenport = Basis + 6, Bankauswahl = Register `4E`. Ein 16-Bit-Register `0xBRR` heißt Bank `B`,
  Register `RR`.
- Das BIOS sperrt den I/O-Bereich des Hardware-Monitors: Ist Bit 4 von `CR28` gesetzt, liefern die Ports nur `FF`.
  Jeder Zugriffsblock:
  1. `Global\Access_ISABUS.HTP.Method` und das Ereignis `Global\ASUS_SIO_MUTEX` nehmen (Auto-Reset, vom Lüfterdienst des
     Herstellers genutzt),
  2. Konfigurationsmodus betreten (`87`, `87` an `2E`), Bit 4 von `CR28` löschen, Konfigurationsmodus verlassen (`AA`),
  3. Register lesen/schreiben, Bank- und Indexregister wiederherstellen,
  4. Bit 4 von `CR28` wieder setzen und beide Sperren freigeben.

### 2.2 Register

Belegung wie im NCT6796D-Datenblatt.

| Was | Register | Umrechnung |
|---|---|---|
| Hersteller-ID | `804F` / `004F` | `5CA3` = Nuvoton |
| Drehzahlzähler SYSFAN, CPUFAN, AUXFAN0–4 | `4B0`, `4B2`, `4B4`, `4B6`, `4B8`, `4BA`, `4CC` | 13 Bit (`hi << 5 \| lo & 1F`); U/min = 1 350 000 / Zähler; `1FFF` = steht |
| PWM-Ausgang | `001`, `003`, `011`, `013`, `015`, `A09`, `B09` | 0–255 |
| Temperaturen | `073` … `67A`, je mit Quellregister | z. B. CPUTIN, SYSTIN, PECI |

### 2.3 Lüfterkurven (SmartFan IV)

Jeder Lüfterkanal hat eine Regelbank: SYSFAN `1`, CPUFAN `2`, AUXFAN0 `3`, AUXFAN1–4 `8`–`B`. Eine Kurve hat vier Punkte.

| Register in der Bank | Wert |
|---|---|
| `00` | Temperaturquelle in Bit 0–4 (`1C` auf allen Kanälen: CPU-Temperatur, liest der Chip selbst; `01` = SYSTIN/Mainboard, `02` = CPUTIN/Sockel) |
| `02` | `40` = Modus SmartFan IV |
| `03`, `04` | Schrittzeit beim Hoch- bzw. Runterregeln (`00` auf allen Kanälen = Leistung folgt der Kurve sofort) **[beobachtet]**, Einheit **[offen]** |
| `21`–`24` | Temperaturen T1–T4 (°C, höchstens 100) |
| `27`–`2A` | Leistung P1–P4 (0–255) |

Der Lüfterdienst des Herstellers schreibt T1, T2, T3, T3 und P1, P2, P3, `FF`; über dem dritten Punkt läuft der Lüfter
also voll. Weil der Chip die CPU-Temperatur selbst liest, wirken die Kurven auch ohne laufende Software (fail-safe).
**[beobachtet]** Borealis schreibt die Kurven genauso und liest sie zurück. Der Lüfterdienst des Herstellers wird vorher
angehalten; er würde sonst beim Start seine eigenen Kurven schreiben. Bei gewählter Temperaturquelle schreibt Borealis
auch Bit 0–4 von Register `00` (die oberen Bits bleiben) und liest sie zurück; der CPU-Lüfter behält immer die CPU.

### 2.4 Lüfteranschlüsse beim getesteten Board

| Eingang am Chip | Anschluss | Bank |
|---|---|---|
| CPUFAN | CPU-Lüfter | 2 |
| SYSFAN | Gehäuselüfter 1 | 1 |
| AUXFAN0 | Gehäuselüfter 2 | 3 |
| AUXFAN1–3 | Gehäuselüfter 3–5 | 8–A |
| AUXFAN4 | AIO-Pumpe | B |

### 2.5 Auto-Tuning und Voreinstellungen

Code: `Borealis.Service/FanTuner.cs`, `Profiles/FanSettings.cs`.

- **Tuning:** Alle Lüfter gehen in 5-%-Stufen von 100 % auf 0 %; je Stufe wird die eingeschwungene, gemittelte Drehzahl
  gemessen. Danach auf 1 % genau die kleinste Leistung, bei der jeder Lüfter sicher weiterläuft (mit Gegenprobe), und die,
  ab der er aus dem Stand anläuft. Die Pumpe wird anschließend gemessen, während alle Lüfter voll laufen. Die Leistung wird
  über flache Kurven im Chip gesetzt. Der Lauf endet sofort über 85 °C CPU-Temperatur (70 °C in der Pumpenphase) oder bei
  Abbruch; danach werden die normalen Kurven zurückgeschrieben. Dauer etwa 5–7 Minuten.
- **Voreinstellungen** Leise/Standard/Turbo entstehen aus den Messwerten: Der unterste Punkt ist die sichere Lauf- bzw.
  Anlaufleistung + 5 %, die mittleren Punkte sind Anteile der gemessenen Höchstdrehzahl, in Leistung zurückgerechnet. Die
  Pumpe wird in Leise gedrosselt und in Standard leicht, aber nie unter 50 %.

## 3. RAM-Beleuchtung (ENE-Controller über SMBus)

DDR5-Module mit ENE-RGB-Controller (Chip-ID `68`, Name z. B. `AUDA0-E6K5-0101`, 8 LEDs). Code:
`Lighting/EneSmbusRam.cs`, `Hardware/SmbusPiix4.cs`.

### 3.1 SMBus-Zugriff

- Über das PawnIO-Modul `SmbusPIIX4`, Port 0 des AMD-FCH (PCI `1022:790B`), unter der Sperre
  `Global\Access_SMBUS.HTP.Method`.
- Module am getesteten System: 7-Bit-Adressen **`70`–`73`**. Das BIOS vergibt sie offenbar beim Start; sie waren auch
  nach einem Neustart ohne installierte Herstellersoftware da. **[beobachtet]**

| Schritt | SMBus-Transaktion |
|---|---|
| Register wählen | Wort schreiben, Befehl `00`, Wert `(reg << 8) \| (reg >> 8)` (hohes Byte zuerst auf dem Bus) |
| Byte lesen | Byte-Daten lesen, Befehl `81`; die Adresse zählt weiter |
| Byte schreiben | Byte-Daten schreiben, Befehl `01`; die Adresse zählt weiter |
| Block schreiben | Block schreiben, Befehl `03`, höchstens 24 Byte; wird beim Start durch Zurücklesen geprüft |

### 3.2 Register

| Register | Inhalt |
|---|---|
| `F01C` | Chip-ID (`68`) |
| `1000` | Name, 16 Byte |
| `1C00` | Konfiguration; Byte 2 = LED-Anzahl |
| `8020` | Steuerblock, 16 Byte: `[0]` Direct-Modus an/aus, `[1]` Effekt, `[2]` Tempo (−2…2), `[3]` Richtung, `[F]` = 1 übernimmt |
| `8100` | Direct-Farben, 3 Byte je LED in der Reihenfolge **R, B, G** |
| `8160` | Effektfarben |

Der Steuerblock wird nur beim Moduswechsel geschrieben; beim Streamen nur noch die Farben.

### 3.3 Sicherheit

Borealis sucht nur an `70`–`77`, wählt ein Register nur bei einer Adresse, die antwortet, und behandelt sie nur mit
Chip-ID `68` und lesbarem Namen als RAM-Beleuchtung. Geschrieben wird nur an solche Adressen, zusätzlich mit einer festen
Grenze `70`–`77` im Code. SPD-Hub und Spannungsregler der Module werden nie angesprochen.

### 3.4 Rückfall: ENE-HAL

Ist die ENE-RGB-HAL der Herstellersoftware noch installiert (COM-Klasse `{05B8E7A3-7D5D-41DE-B6CC-5E6205837FB7}`,
in-proc, nur aus einem MTA-Thread), kann Borealis sie statt des eigenen SMBus-Zugriffs nutzen
(`Lighting/RamController.cs`). Geräte mit `Enumerate` aufzählen (erst mit `NULL` für die Anzahl); `Enumerate2` liefert
ein ungültiges HRESULT. Farben sind `0x00BBGGRR` je LED; Effekt 0 = direkt.

## 4. CPU-Temperatur

AMD Zen 4 / Zen 5: Tctl und Temperaturen je CCD aus dem SMN-Raum der CPU, Register `59800` und die CCD-Register
(ab `59B08` bei Ryzen 7000/9000), gelesen über das PawnIO-Modul `AMDFamily17` (Index `60` / Daten `64` der Host-Bridge
0:0.0) unter der Sperre `Global\Access_PCI`. Formeln wie Linux `k10temp`. Code: `Hardware/AmdCpuTemperature.cs`.

## 5. Software-Effekte

Alle Effekte außer „statisch, eine Farbe“ berechnet Borealis selbst und streamt sie im Direct-Modus
(`Lighting/SoftwareEffects.cs`, `Borealis.Service/EffectRunner.cs`). Die Effekte sind zustandslos und zeitbasiert, dadurch
laufen alle Geräte synchron. Als Startzustand nach einem Neustart wird ein ähnlicher Firmware-Effekt im Controller
gespeichert.

- **Musik:** WASAPI-Loopback auf dem Standard-Wiedergabegerät, FFT über 1024 Samples mit Vorschub 512 (≈ 94 Analysen/s),
  16 Bänder mit Auto-Gain, Beats aus der Bassenergie (40–160 Hz), BPM aus dem Median der Beat-Abstände
  (`Borealis.Service/AudioAnalyzer.cs`).
- **Smart:** CPU- oder RAM-Last (`Borealis.Service/SignalSources.cs`).
- **Bildrate:** standardmäßig 8 Bilder/s, im Leistungsmodus 12/20/30, begrenzt durch die Dauer der USB-Reports (§1.9).

## 6. Sperren, die mit anderen Tools geteilt werden

| Sperre | Schützt |
|---|---|
| `Global\asusaurausb` (Mutex) | USB-Beleuchtungscontroller |
| `Global\Access_ISABUS.HTP.Method` (Mutex) | Super-I/O-Portzugriff |
| `Global\ASUS_SIO_MUTEX` (Auto-Reset-Ereignis) | Hardware-Monitor im Super-I/O (Lüfterdienst des Herstellers) |
| `Global\Access_SMBUS.HTP.Method` (Mutex) | SMBus |
| `Global\Access_PCI` (Mutex) | PCI-Konfiguration / SMN |

Code: `Hardware/BusLock.cs`.
