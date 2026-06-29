# Fan Controller – Protokół komunikacji i schemat podłączenia

## Sprzęt

| Komponent | Model |
|-----------|-------|
| Mikrokontroler | Seeed Studio XIAO ESP32C3 |
| Wentylator | Noctua NF-A20 5V PWM |
| Komunikacja | Bluetooth Low Energy (BLE) 4.2 |

---

## Schemat podłączenia pinów

### Wentylator NF-A20 PWM – złącze 4-pinowe

Noctua NF-A20 5V PWM ma standardowy 4-pinowy konektor PWM (kompatybilny z 4-pin PC PWM):

```
Wentylator       Kolor przewodu    XIAO ESP32C3
──────────────────────────────────────────────────────
Pin 1  GND       Czarny            GND
Pin 2  +5V       Żółty             5V (USB VBUS lub zewnętrzne 5V)
Pin 3  TACH      Zielony           D2 (GPIO2)   ← odczyt obrotów
Pin 4  PWM       Niebieski         D6 (GPIO6)   ← sterowanie prędkością
```

> **UWAGA:** XIAO ESP32C3 działa na 3.3V logiki. Pin TACH wentylatora Noctua ma wewnętrzny pull-up do 5V,
> dlatego **wymagany jest dzielnik napięcia lub konwerter poziomów** na linii TACH → GPIO2.
>
> Prosty dzielnik napięcia (5V→3.3V):
> ```
> TACH (5V) ──┬── R1 (10kΩ) ── GPIO2 (D2)
>             └── R2 (20kΩ) ── GND
> ```
> Alternatywnie: użyj modułu konwertera poziomów 5V↔3.3V (np. TXS0108E).

### Pinout XIAO ESP32C3

```
        ┌──────────┐
5V  ────┤ 5V    D0 ├──── (wolny)
GND ────┤GND    D1 ├──── (wolny)
 3V ────┤3V3    D2 ├──── TACH  (sygnał obrotów wentylatora)
        │       D3 ├──── (wolny)
        │       D4 ├──── (wolny)
        │       D5 ├──── (wolny)
        │       D6 ├──── PWM   (sterowanie prędkością)
        │       D7 ├──── (wolny)
        │       D8 ├──── (wolny)
        │       D9 ├──── (wolny)
        │      D10 ├──── (wolny)
        └──────────┘
```

---

## Parametry PWM

| Parametr | Wartość |
|----------|---------|
| Częstotliwość | 25 000 Hz (25 kHz) |
| Rozdzielczość | 8-bit (0–255) |
| Wartość przy 0% | PWM = 0 |
| Wartość przy 100% | PWM = 255 |
| Formuła | `PWM = fanSpeed_percent * 255 / 100` |

> Standard PC PWM dla wentylatorów wymaga 25 kHz. Wentylator startuje przy ~20% duty cycle.

---

## Protokół BLE

### Identyfikacja usługi

| Pole | Wartość |
|------|---------|
| Nazwa urządzenia BLE | `Fan Controller ESP32C3` |
| Service UUID | `4fafc201-1fb5-459e-8fcc-c5c9c331914b` |

---

### Charakterystyka 1 – Prędkość wentylatora (Speed)

| Pole | Wartość |
|------|---------|
| UUID | `beb5483e-36e1-4688-b7f5-ea07361b26a8` |
| Właściwości | **READ**, **WRITE**, **NOTIFY** |
| Format danych | 1 bajt (`uint8`) |
| Zakres wartości | `0` – `100` |

#### Zapis (Write) – sterowanie prędkością

Aplikacja wysyła **1 bajt** z żądaną prędkością:

| Wartość bajtu | Akcja |
|--------------|-------|
| `0` | Wyłącz wentylator (PWM = 0) |
| `1`–`100` | Ustaw prędkość w %; `0` = stop, `100` = pełna moc |

**Przykład – ustaw 75%:**
```
Write: [0x4B]   (75 dziesiętnie)
```

**Przykład – wyłącz wentylator:**
```
Write: [0x00]
```

#### Odczyt (Read) i powiadomienia (Notify)

ESP32C3 odsyła aktualną wartość prędkości po każdej zmianie oraz przy nawiązaniu połączenia:

```
Notify: [0x32]  → 50% prędkości
Notify: [0x00]  → wentylator wyłączony
```

---

### Charakterystyka 2 – Obroty (RPM)

| Pole | Wartość |
|------|---------|
| UUID | `beb5483e-36e1-4688-b7f5-ea07361b26a9` |
| Właściwości | **READ**, **NOTIFY** |
| Format danych | 2 bajty (`uint16`, little-endian) |
| Zakres wartości | `0` – `65535` RPM |
| Interwał powiadomień | ~1 sekunda |

#### Format danych RPM

```
Bajt 0 (LSB) | Bajt 1 (MSB)
─────────────────────────────
RPM = Bajt0 | (Bajt1 << 8)
```

**Przykład – 800 RPM:**
```
Notify: [0x20, 0x03]
→ RPM = 0x20 | (0x03 << 8) = 32 + 768 = 800
```

**Przykład – wentylator zatrzymany:**
```
Notify: [0x00, 0x00]
→ RPM = 0
```

#### Obliczenie RPM przez firmware

```
RPM = (impulsy_w_1s × 60) / PULSES_PER_ROTATION
    = (impulsy_w_1s × 60) / 2
```

Wentylator NF-A20 generuje **2 impulsy na obrót** (standard PC).

---

## Sekwencja połączenia (aplikacja mobilna)

```
Telefon                              ESP32C3
  │                                     │
  │── BLE Scan ──────────────────────►  │ (nasłuchuje jako "Fan Controller ESP32C3")
  │◄─ Advertisement ───────────────────  │
  │── Connect ───────────────────────►  │
  │◄─ Connection Established ──────────  │
  │── Discover Services ─────────────►  │
  │◄─ Service UUID + Characteristics ──  │
  │── Subscribe NOTIFY (Speed Char) ──►  │
  │── Subscribe NOTIFY (RPM Char) ───►  │
  │◄─ Notify: Speed=[current] ─────────  │  (ESP32C3 wysyła aktualny stan)
  │                                     │
  │── Write: Speed=[0x32] ───────────►  │  (ustaw 50%)
  │◄─ Notify: Speed=[0x32] ─────────── │  (potwierdzenie)
  │◄─ Notify: RPM=[lo, hi] ─────────── │  (co ~1s)
  │                                     │
  │── Disconnect ────────────────────►  │
  │◄─ Disconnected ─────────────────── │  (ESP32C3 restartuje rozgłaszanie)
```

---

## Kompilacja firmware (Arduino IDE)

### Wymagane ustawienia Arduino IDE

| Ustawienie | Wartość |
|------------|---------|
| Board | XIAO ESP32C3 (Seeed Studio) |
| Flash Mode | QIO 80MHz |
| Partition Scheme | Default 4MB |
| Upload Speed | 921600 |
| Port | COMx (po podłączeniu USB-C) |

### Wymagane biblioteki

| Biblioteka | Wersja | Uwagi |
|------------|--------|-------|
| ESP32 Arduino Core | 2.x lub 3.x | Obsługiwane automatycznie |
| BLEDevice (wbudowana) | – | Część ESP32 core |

> Plik `.ino` automatycznie wykrywa wersję ESP32 Arduino Core i używa odpowiedniego API LEDC
> (`ledcSetup`/`ledcAttachPin` dla v2.x lub `ledcAttach` dla v3.x).

---

## Debugowanie (Serial Monitor)

Podłącz XIAO przez USB-C i otwórz Serial Monitor z prędkością **115 200 baud**.

| Komenda Serial | Akcja |
|---------------|-------|
| `t` | 5-sekundowy test pinu TACH |
| `s` | Wymuś wysłanie aktualnej prędkości przez BLE |

Przykładowe logi przy poprawnym działaniu:
```
DEBUG: Inicjowanie sterowania wentylatorem przez BLE...
DEBUG: Początkowa prędkość: 50% (PWM: 127)
DEBUG: Testowanie pinu TACH przez 5 sekund...
.......................................
DEBUG: Wykryto 1340 impulsów TACH w ciągu 5 sekund
DEBUG: Wykrywanie impulsów TACH działa prawidłowo
DEBUG: Oczekiwanie na połączenie BLE...
DEBUG: Urządzenie podłączone
DEBUG: Wysłano aktualizację prędkości: 50
DEBUG: Zliczono impulsów w ostatniej sekundzie: 26
DEBUG: Fan speed: 780 RPM
```
