# Hardware reference

[Deutsch](hardware.de.md)

How Borealis talks to the hardware: the USB lighting controller, the Nuvoton fan chip, the RGB controllers on the memory
modules and the CPU temperature sensor. Everything here was worked out on the tested system (see the README) and is
implemented in `src/Borealis.Core`. Byte values are hexadecimal.

Labels: **[observed]** = seen on the live system or in captured traffic, **[derived]** = concluded from that, not
confirmed directly, **[open]** = not yet known.

## 1. USB lighting controller

USB HID device `0B05:19AF`, interface `MI_02` (the "6K75" controller, protocol generation "Aura50"). It drives the
fixed motherboard zones and the addressable ARGB headers. Code: `Lighting/UsbLedHid.cs`, `Lighting/UsbLedController.cs`.

### 1.1 Transport and locking

- Every report is **65 bytes**: byte 0 = report ID `EC`, byte 1 = command, bytes 2–64 = payload padded with `00`.
- Writes with `WriteFile`, reads with `ReadFile`. On a write error: wait 1 ms, flush the HID queue, retry once.
- **Query without index:** send `EC <cmd | 80>`, then read until a reply with `[1] = cmd` arrives (timeout 2 s).
- **Query with index:** two writes: `EC <cmd> <idx>` sets the index, `EC <cmd | 80>` requests the data. Then read until
  `[1] = cmd` and `[2] = idx`. Always check the echo, stale replies happen. **[observed]**
- All access happens under the system-wide mutex `Global\asusaurausb`, which the vendor software also uses.

### 1.2 Commands

| Command | Kind | Meaning |
|---|---|---|
| `30` (sent as `EC B0`) | query | configuration table (§1.3) |
| `31 <unit>` | query | unit info: type `[3]`, LED count `[4..5]` (big-endian), location `[6]` |
| `32 <channel>` | query | control channel state (§1.4) |
| `35` | write | set effect on a control channel (§1.5) |
| `36` | write | colors for a firmware effect (§1.5) |
| `38` | write | lighting on/off: `EC 38 01 <on>`, then `EC 38 00 <on>` |
| `3B <unit> <hi> <lo>` | write | set the LED count of an ARGB header (§1.8) |
| `3F 55` | write | apply and store in the controller **[derived]**; do not send in a loop |
| `3F AA` | write | restore the firmware default effect **[derived]** |
| `40` | write | direct mode: stream LED colors (§1.6) |

### 1.3 Configuration and units

Raw reply to `EC B0` on the tested board:

```
EC 30 00 00 1E 9F 03 | 01 00 00 78 3C 00 | 01 00 00 78 3C 00 | 01 00 00 78 3C 00 | 00 … | 03 06 02 01 F4
                  [6] = number of ARGB headers, then 6 bytes each: present, type, LED count (BE, 0x78 = 120), 3C, 00
```

The tail `03 06 02 01 F4` probably holds the motherboard LED count and the maximum of 500 LEDs per header **[derived]**.

Units (`EC 31 <unit>`) on the tested board **[observed]**:

| Unit | Type | LEDs | Meaning |
|---|---|---|---|
| 0, 1 | `10` | 1 | motherboard, back I/O logo |
| 2 | `10` | 1 | motherboard 12 V RGB header |
| 3 | `20` | 120 | ARGB header 1 |
| 4 | `21` | 120 | ARGB header 2 |
| 5 | `22` | 120 | ARGB header 3 |

Type `10` is a fixed motherboard zone, `2x` is ARGB header *x* + 1.

### 1.4 Channels

There are two kinds of channel numbers:

- **Control channel** for `EC 35`, `EC 36` and `EC 3F`: the motherboard units are on channel **0**, all ARGB headers
  share channel **1**. Switching a control channel (e.g. into direct mode) clears the direct buffers of every unit on it,
  so all of them have to be resent. **[observed]**
- **Direct channel** for `EC 40`: ARGB header *n* = channel *n* − 1 (type − `20`), the motherboard = channel **4**.
  Older "Aura36" controllers use channel 6 for the motherboard; using 6 on this controller shows colors but makes all
  channels flicker while streaming. **[observed]**

Reply to `EC 32 <channel>`:

| Byte | Meaning |
|---|---|
| `[4..5]` | unit mask (bit *n* = unit *n*) |
| `[6]`, `[7]`, `[8]` | current effect, direction, speed |
| `[9..10]` | unit mask of the shutdown effect |
| `[11]`, `[12]`, `[13]` | shutdown effect, direction, speed |

### 1.5 Firmware effects

The controller plays these effects on its own, also without Windows running. Code: `Lighting/FirmwareEffect.cs`.

```
EC 35 <channel> 00 <flag> <effect> <direction> <speed>       flag 00 = normal, 01 = shutdown effect (§1.7)
EC 36 <mask hi> <mask lo> <flag> RGB × 16                    only for effects with a color; slot n = unit n
EC 3F 55                                                     apply/store
```

Sequence: if the channel was in direct mode, first `EC 35 <channel> 00 00 00` (effect 0), then the three reports above.
A firmware effect colors a whole ARGB strip in one color; per-LED colors need direct mode.

| ID | Effect | Color | Speed | Direction |
|---|---|---|---|---|
| `00` | off | | | |
| `01` | static | yes | | |
| `02` | breathing | yes | yes | |
| `03` | strobing | yes | yes | |
| `04` | color cycle | | yes | |
| `05` | rainbow | | yes | yes |
| `08` | comet | | yes | yes |
| `0A` | flash and dash | | yes | yes |
| `0B` | wave | | yes | yes |
| `0C` | glowing yoyo | | yes | yes |
| `0D` | starry night | | yes | |
| `FF` | direct mode (software control, §1.6) | | | |

The color column is **[observed]**, speed and direction are **[derived]**. Speed values used by the vendor software:
slow/normal/fast = `02`/`01`/`00`, or `01`/`00`/`FF` for moving effects.

### 1.6 Direct mode

1. Switch each control channel once: `EC 35 <channel> 00 00 FF`. The firmware hands the channel over to software.
2. Stream colors per direct channel:

```
[0] EC
[1] 40
[2] direct channel | ((start >> 4) & F0) | (last report ? 80 : 00)
[3] first LED (low byte)
[4] number of LEDs in this report (at most 20)
[5..64] R, G, B per LED
```

- A strip with 120 LEDs takes 6 reports. Bit 7 in byte 2 of the last report applies the frame, **per channel**: a frame
  without it is not shown. **[observed]**
- No reply is read; there is no handshake.
- To go back to the firmware, set a firmware effect (§1.5).

### 1.7 Shutdown effect

The controller stores a second effect per control channel. It runs while the PC is off (S3/S5) and the power supply still
delivers standby power, provided the BIOS option for lighting in sleep/soft-off is enabled.

- Write: like §1.5 with flag `01` in `EC 35` and `EC 36`, then `EC 3F 55`.
- Read back: `EC 32 <channel>`, bytes `[9..13]` (§1.4).
- Only firmware effects are possible. Memory modules have no shutdown effect; they turn off with the system.

### 1.8 LED count of an ARGB header

- Read: `EC 31 <unit>`, reply `[4..5]` (big-endian). Firmware versions ≤ 200 do not support this.
- Write:
  1. Direct mode on the channel (§1.6).
  2. Send a black frame with the *old* LED count, so LEDs beyond the new count turn off.
  3. `EC 3B <unit> <count hi> <count lo>`.

### 1.9 Timing and coexistence

- One HID report takes **≈ 4 ms** (interrupt endpoint interval), with or without overlapping writes. A full frame of
  3 × 120 LEDs plus the motherboard is 19 reports, so ≈ 13 frames/s at most. Shorter LED counts raise the rate.
  **[observed]**
- Borealis sends all channels of a frame as one block under the lock and then pauses at least 5 ms.
- Flicker during streaming had two causes, both fixed: the motherboard on direct channel 6 instead of 4, and the vendor
  lighting service streaming its own software effect into the same channels at the same time. Borealis therefore stops
  that service while it controls the lighting. **[observed]**

## 2. Fan controller (Nuvoton NCT6799D)

Super I/O chip with ID `D8xx` at config ports `2E`/`2F`. It measures fan speeds and temperatures and controls the fan
outputs by itself from curves stored in its registers. Code: `Hardware/Nct6799.cs`, `Hardware/SensorReader.cs`.

### 2.1 Access and locking

- Port I/O goes through the PawnIO module `LpcIO`.
- The hardware monitor sits at the base address from LDN `0B`, registers `60`/`61` (`290` on the tested board). Index
  port = base + 5, data port = base + 6, bank select = register `4E`. A 16-bit register `0xBRR` means bank `B`,
  register `RR`.
- The BIOS locks the hardware monitor I/O space: with bit 4 of `CR28` set, the ports return only `FF`. Each access block:
  1. take `Global\Access_ISABUS.HTP.Method` and the event `Global\ASUS_SIO_MUTEX` (auto-reset, used by the vendor fan
     service),
  2. enter config mode (`87`, `87` to `2E`), clear bit 4 of `CR28`, leave config mode (`AA`),
  3. access the registers, restore the bank and index registers,
  4. set bit 4 of `CR28` again and release both locks.

### 2.2 Registers

Layout as in the NCT6796D datasheet.

| What | Register | Conversion |
|---|---|---|
| vendor ID | `804F` / `004F` | `5CA3` = Nuvoton |
| fan count SYSFAN, CPUFAN, AUXFAN0–4 | `4B0`, `4B2`, `4B4`, `4B6`, `4B8`, `4BA`, `4CC` | 13 bit (`hi << 5 \| lo & 1F`); rpm = 1 350 000 / count; `1FFF` = stopped |
| PWM output | `001`, `003`, `011`, `013`, `015`, `A09`, `B09` | 0–255 |
| temperatures | `073` … `67A`, each with a source register | e.g. CPUTIN, SYSTIN, PECI |

### 2.3 Fan curves (SmartFan IV)

Each fan channel has a control bank: SYSFAN `1`, CPUFAN `2`, AUXFAN0 `3`, AUXFAN1–4 `8`–`B`. A curve has four points.

| Register in the bank | Value |
|---|---|
| `00` | temperature source (`1C` on all channels: CPU temperature, read by the chip itself) |
| `02` | `40` = SmartFan IV mode |
| `21`–`24` | temperatures T1–T4 (°C, at most 100) |
| `27`–`2A` | duty P1–P4 (0–255) |

The vendor fan service writes T1, T2, T3, T3 and P1, P2, P3, `FF`, so the fan runs at full speed above the third point.
Because the chip reads the CPU temperature itself, the curves keep working when no software runs (fail-safe).
**[observed]** Borealis writes curves the same way and reads them back. The vendor fan service is stopped first; it would
otherwise write its own curves when it starts.

### 2.4 Fan headers on the tested board

| Chip input | Header | Bank |
|---|---|---|
| CPUFAN | CPU fan | 2 |
| SYSFAN | chassis fan 1 | 1 |
| AUXFAN0 | chassis fan 2 | 3 |
| AUXFAN1–3 | chassis fans 3–5 | 8–A |
| AUXFAN4 | AIO pump | B |

### 2.5 Auto tuning and presets

Code: `Borealis.Service/FanTuner.cs`, `Profiles/FanSettings.cs`.

- **Tuning:** all fans step from 100 % to 0 % in 5 % steps; at each step the settled, averaged speed is measured. Then,
  at 1 % resolution, the lowest duty that keeps each fan running (with a re-check) and the duty at which it starts from
  standstill. The pump is measured afterwards while all fans run at full speed. Duty is set through flat curves in the
  chip. The run stops at once above 85 °C CPU (70 °C during the pump phase) or on cancel; the normal curves are then
  written back. Takes about 5–7 minutes.
- **Presets** Silent/Standard/Turbo are built from the measured values: the lowest point is the safe running or start-up
  duty + 5 %, the middle points are fractions of the measured maximum speed converted back to duty, the pump is reduced
  in Silent and slightly in Standard, but never below 50 %.

## 3. Memory lighting (ENE controller over SMBus)

DDR5 modules with an ENE RGB controller (chip ID `68`, name e.g. `AUDA0-E6K5-0101`, 8 LEDs). Code:
`Lighting/EneSmbusRam.cs`, `Hardware/SmbusPiix4.cs`.

### 3.1 SMBus access

- Through the PawnIO module `SmbusPIIX4`, port 0 of the AMD FCH (PCI `1022:790B`), under the lock
  `Global\Access_SMBUS.HTP.Method`.
- Modules on the tested system: 7-bit addresses **`70`–`73`**. The BIOS seems to assign them at boot; they were present
  after a restart with the vendor software uninstalled. **[observed]**

| Step | SMBus transaction |
|---|---|
| select register | write word, command `00`, value `(reg << 8) \| (reg >> 8)` (high byte first on the bus) |
| read byte | read byte data, command `81`; the address auto-increments |
| write byte | write byte data, command `01`; the address auto-increments |
| write block | block write, command `03`, at most 24 bytes; checked at startup by reading back |

### 3.2 Registers

| Register | Content |
|---|---|
| `F01C` | chip ID (`68`) |
| `1000` | name, 16 bytes |
| `1C00` | configuration; byte 2 = LED count |
| `8020` | control block, 16 bytes: `[0]` direct mode on/off, `[1]` effect, `[2]` speed (−2…2), `[3]` direction, `[F]` = 1 applies |
| `8100` | direct colors, 3 bytes per LED in the order **R, B, G** |
| `8160` | effect colors |

The control block is written only when switching modes; while streaming, only the colors are written.

### 3.3 Safety

Borealis only scans `70`–`77`, only selects a register at an address that answers, and only treats it as a memory
lighting controller with chip ID `68` and a readable name. Writes are limited to such addresses, with an additional hard
limit of `70`–`77` in the code. The SPD hub and voltage regulator of the modules are never addressed.

### 3.4 Fallback: ENE HAL

If the ENE RGB HAL from the vendor software is still installed (COM class `{05B8E7A3-7D5D-41DE-B6CC-5E6205837FB7}`,
in-process, must be used from an MTA thread), Borealis can use it instead of its own SMBus access (`Lighting/RamController.cs`).
Enumerate devices with `Enumerate` (first call with `NULL` for the count); `Enumerate2` returns an invalid HRESULT.
Colors are `0x00BBGGRR` per LED; effect 0 = direct.

## 4. CPU temperature

AMD Zen 4 / Zen 5: Tctl and per-CCD temperatures from the CPU's SMN space, register `59800` and the per-CCD registers
(from `59B08` on Ryzen 7000/9000), read through the PawnIO module `AMDFamily17` (index `60` / data `64` of host bridge 0:0.0) under the lock
`Global\Access_PCI`. Formulas as in Linux `k10temp`. Code: `Hardware/AmdCpuTemperature.cs`.

## 5. Software effects

All effects except "static, one color" are computed by Borealis and streamed in direct mode (`Lighting/SoftwareEffects.cs`,
`Borealis.Service/EffectRunner.cs`). Effects are stateless and time-based, so all devices stay in sync. A similar
firmware effect is stored in the controller as the starting state after a restart.

- **Music:** WASAPI loopback on the default playback device, FFT over 1024 samples with a hop of 512 (≈ 94 analyses/s),
  16 bands with auto gain, beats from bass energy (40–160 Hz), BPM from the median beat interval
  (`Borealis.Service/AudioAnalyzer.cs`).
- **Smart:** CPU or memory load (`Borealis.Service/SignalSources.cs`).
- **Frame rate:** 8 frames/s by default, 12/20/30 in performance mode, limited by the USB report time (§1.9).

## 6. Locks shared with other tools

| Lock | Protects |
|---|---|
| `Global\asusaurausb` (mutex) | USB lighting controller |
| `Global\Access_ISABUS.HTP.Method` (mutex) | Super I/O port access |
| `Global\ASUS_SIO_MUTEX` (auto-reset event) | Super I/O hardware monitor (vendor fan service) |
| `Global\Access_SMBUS.HTP.Method` (mutex) | SMBus |
| `Global\Access_PCI` (mutex) | PCI configuration / SMN |

Code: `Hardware/BusLock.cs`.
