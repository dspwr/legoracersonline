# Konstytucja Serwera LAN – LEGO Racers 1999 Multiplayer

## Streszczenie

Niniejszy dokument stanowi kompletną specyfikację techniczną dla asystenta kodowania, który będzie implementował serwer LAN do gry LEGO Racers (1999) w Pythonie. Serwer ma obsługiwać do 6 graczy w sieci lokalnej. Dokument opisuje architekturę, protokół komunikacji, formaty pakietów, modele synchronizacji (w tym podejście sandbox), struktury pamięci gry oraz plan implementacji krok po kroku. Bazą referencyjną jest projekt open-source legoracersonline oraz Lego-Racers-Mod.[1][2][3]

***

## Cel projektu

Stworzenie stabilnego, standalone serwera LAN w Pythonie, który umożliwi 6 graczom jednoczesną rozgrywkę w LEGO Racers (1999) na LAN party. Serwer jest jedynym źródłem prawdy (authoritative) o stanie wyścigu i sam przechowuje pozycje wszystkich graczy.[3][4]

***

## Architektura systemu

### Komponenty

System składa się z trzech głównych komponentów:

| Komponent | Język | Rola |
|-----------|-------|------|
| **Serwer LAN** | Python (asyncio) | Standalone aplikacja: lobby, zarządzanie wyścigiem, przechowywanie stanu, broadcast pozycji, walidacja |
| **Klient sieciowy** | C/C++ (DLL) | Wstrzykiwany do procesu gry – czyta/zapisuje pamięć, komunikuje się z serwerem |
| **LEGORacersAPI** | C/C++ (DLL) | Warstwa abstrakcji nad pamięcią gry – adresy, pointery, funkcje sterujące[3] |

Serwer **nie dotyka** procesu gry. Widzi wyłącznie logiczny stan świata jako stream danych od klientów. Cała „wiedza o pamięci LR" leży po stronie klienta/API.[5][3]

### Schemat komunikacji

```
[Gracz 1: LR.exe + Klient DLL] ←TCP/UDP→ [Serwer Python] ←TCP/UDP→ [Gracz 2..6: LR.exe + Klient DLL]
```

Serwer nasłuchuje na dwóch portach:
- **TCP port** (np. 27015) – połączenia, eventy, zarządzanie
- **UDP port** (np. 27016) – ciągły stream pozycji

***

## Protokół komunikacji

### Zasada podziału TCP/UDP

Podział jest zgodny z oryginalnym projektem legoracersonline i standardowymi praktykami gier sieciowych:[6][3]

| Kanał | Użycie | Uzasadnienie |
|-------|--------|-------------|
| **TCP** | Connect, disconnect, start race, finish race, użycie power-upa, chat, lobby operations | Gwarancja dostarczenia i kolejności – eventy krytyczne nie mogą zginąć |
| **UDP** | Pozycje, rotacje, prędkości, flagi stanu | Niskie opóźnienie, tolerancja na utratę pakietów – zaraz przyjdzie następny update[3][4] |

### Format pakietów – nagłówek wspólny

Każdy pakiet (TCP i UDP) zaczyna się od wspólnego nagłówka 8 bajtów:[7]

```python
import struct
from enum import IntEnum

class MsgType(IntEnum):
    # TCP messages
    CONNECT_REQUEST  = 0x01
    CONNECT_RESPONSE = 0x02
    DISCONNECT       = 0x03
    LOBBY_STATE      = 0x10
    RACE_START       = 0x20
    RACE_FINISH      = 0x21
    POWERUP_USE      = 0x30
    CHAT_MESSAGE     = 0x40
    # UDP messages
    POSITION_UPDATE  = 0x80
    STATE_SNAPSHOT   = 0x81

# Header: [1B type][1B version][2B flags][4B sequence]
HEADER_FORMAT = '>BBHI'  # big-endian
HEADER_SIZE = struct.calcsize(HEADER_FORMAT)  # = 8 bytes
```

### Definicje pakietów

#### CONNECT_REQUEST (TCP, klient → serwer)

```python
# Header + [32B nickname (UTF-8, null-padded)] + [16B game_version_hash]
CONNECT_REQ_FORMAT = '>32s16s'
```

Serwer sprawdza:
- Czy nickname jest unikalny (jak w oryginale)[3]
- Czy hash wersji gry się zgadza (3 różne wersje LR1 istnieją)[5]
- Czy jest wolny slot (max 6 graczy)

#### CONNECT_RESPONSE (TCP, serwer → klient)

```python
# Header + [1B status (0=ok, 1=nick_taken, 2=full, 3=version_mismatch)] + [1B assigned_player_id]
CONNECT_RESP_FORMAT = '>BB'
```

#### RACE_START (TCP, serwer → wszyscy klienci)

```python
# Header + [1B track_id] + [1B num_players] + [1B countdown_seconds]
RACE_START_FORMAT = '>BBB'
```

#### POWERUP_USE (TCP, klient → serwer → broadcast)

```python
# Header + [1B player_id] + [1B powerup_type] + [4B target_player_id_or_0]
POWERUP_FORMAT = '>BBI'
```

#### POSITION_UPDATE (UDP, klient → serwer, 30-60x/sek)

```python
# Header + [1B player_id] + [4B tick] + [4B timestamp_ms]
# + [12B position: float x,y,z] + [24B rotation: 6 floats forward+up vectors]
# + [12B velocity: float vx,vy,vz] + [1B flags]
POSITION_FORMAT = '>BII3f6f3fB'
# flags: bit0=boosting, bit1=airborne, bit2=colliding, bit3=finished
```

Rotacja jest przechowywana jako 6 floatów (forward vector XYZ + up vector XYZ), co odpowiada temu, jak gra wewnętrznie trzyma orientację pojazdu.[5]

#### STATE_SNAPSHOT (UDP, serwer → wszyscy klienci, 30x/sek)

```python
# Header + [4B server_tick] + [4B timestamp_ms] + [1B num_players]
# + N * PlayerState
SNAPSHOT_HEADER_FORMAT = '>IIB'

# PlayerState (per player):
# [1B player_id] + [12B pos] + [24B rot] + [12B vel] + [1B flags] + [4B last_input_tick]
PLAYER_STATE_FORMAT = '>B3f6f3fBI'
```

***

## Modele synchronizacji

### Rekomendowany model: Snapshot Interpolation + Dead Reckoning

Dla gry wyścigowej na LAN z 6 graczami **snapshot interpolation** jest optymalnym wyborem. Lockstep nie nadaje się, bo LEGO Racers nie ma deterministycznej fizyki, a ponadto lockstep źle skaluje się powyżej 2-4 graczy (wszyscy czekają na najwolniejszego).[8][9][10][11]

#### Jak to działa

1. **Klient wysyła** swoje POSITION_UPDATE po UDP z częstotliwością 30-60 Hz.[9][3]
2. **Serwer zbiera** aktualizacje od wszystkich klientów i co ~33ms (30 Hz) tworzy STATE_SNAPSHOT zawierający pozycje/rotacje/velocities wszystkich graczy.
3. **Serwer wysyła** snapshot do każdego klienta (broadcast UDP).[9]
4. **Klient buforuje** 2-3 ostatnie snapshoty i **interpoluje** liniowo między nimi, renderując inne pojazdy z małym opóźnieniem (~100ms na LAN).[12][9]
5. **Między snapshotami** klient stosuje **dead reckoning** (ekstrapolację) na podstawie ostatniej znanej prędkości i kierunku, co maskuje drobne opóźnienia.[13][14]

#### Parametry interpolacji

Na LAN (ping <5ms) bufor interpolacyjny może być minimalny:[15]

```python
# Konfiguracja serwera
TICK_RATE = 30          # snapshotów/sek
TICK_INTERVAL_MS = 33   # ~33ms między tickami
CLIENT_SEND_RATE = 30   # pozycji/sek od klienta

# Konfiguracja klienta (do przekazania klientowi C++)
INTERPOLATION_BUFFER_MS = 100   # bufor na 3 pakiety (3 * 33ms)
DEAD_RECKONING_THRESHOLD = 2.0  # max odległość ekstrapolacji zanim snap
```

#### Dead Reckoning – szczegóły

Dead reckoning ekstrapoluje pozycję pojazdu między update'ami serwera:[13]

```python
def dead_reckon(last_pos, last_vel, last_time, current_time):
    dt = (current_time - last_time) / 1000.0  # sekundy
    predicted_x = last_pos[0] + last_vel[0] * dt
    predicted_y = last_pos[1] + last_vel[1] * dt
    predicted_z = last_pos[2] + last_vel[2] * dt
    return (predicted_x, predicted_y, predicted_z)
```

Gdy przychodzi nowy snapshot i pozycja gracza różni się od predykcji o więcej niż `DEAD_RECKONING_THRESHOLD`, klient powinien płynnie skorigować (lerp do prawidłowej pozycji w ciągu 100-200ms), a nie teleportować.[4][12]

### Server Reconciliation

Serwer jest „lekko authoritative":[16][4]
- **Pozycje**: klient jest źródłem prawdy o swoim ruchu (client-authoritative movement), serwer tylko przechowuje i rozsyła.[17]
- **Eventy**: serwer jest jedynym źródłem prawdy o power-upach, starcie/końcu wyścigu, pozycji w rankingu.
- **Walidacja**: serwer odrzuca pakiety z absurdalną prędkością (>2× max), nielegalnymi power-upami, lub od gracza który nie jest w wyścigu.

***

## Podejście Sandbox – powłoka lokalna na każdym laptopie

### Koncepcja

Każdy gracz uruchamia na swoim laptopie aplikację `app.py`, która tworzy lokalną **powłokę (shell/wrapper)** wokół plików gry LEGO Racers. Powłoka ta działa jako warstwa pośrednia między grą a systemem plików: przechwytuje, monitoruje i modyfikuje pliki, z których korzysta gra, oraz odpowiada za przesyłanie i synchronizację zdarzeń z centralnym serwerem LAN. Gra uruchamiana jest **lokalnie** na laptopie gracza (natywnie na Windows), a `app.py` kontroluje jej środowisko plikowe.[18][19]

To podejście łączy zalety niskiego lagu (lokalne renderowanie) z pełną kontrolą nad plikami gry (jak w sandbox), eliminując potrzebę skomplikowanego DLL injection.

### Architektura

```
┌─── Laptop gracza 1 ────────────────────────────────┐
│                                                     │
│  ┌─────────────┐     ┌─────────────────────────┐   │
│  │  app.py      │────▶│  Powłoka / Shell Layer   │   │
│  │  (Python)    │     │                           │   │
│  │              │     │  • Filesystem overlay      │   │
│  │  TCP/UDP  ◄──┼────▶│  • Monitorowanie plików   │   │
│  │  do serwera  │     │  • Modyfikacja LEGO.JAM    │   │
│  │              │     │  • Zapis pozycji do plików │   │
│  │              │     │  • Memory R/W (opcja)      │   │
│  └──────┬───────┘     └─────────┬─────────────────┘   │
│         │                       │                     │
│         │              ┌────────▼────────┐            │
│         │              │  LEGORacers.exe │            │
│         │              │  (gra oryg.)    │            │
│         │              │  ▲ czyta pliki  │            │
│         │              │  z katalogu     │            │
│         │              │  powłoki        │            │
│         │              └─────────────────┘            │
│         │                                             │
└─────────┼─────────────────────────────────────────────┘
          │
          ▼ TCP/UDP (LAN)
┌─────────────────────┐
│  Serwer LAN Python  │
│  (standalone)       │
│  • Stan wyścigu     │
│  • Pozycje graczy   │
│  • Power-upy       │
│  • Walidacja       │
└─────────────────────┘
          ▲ TCP/UDP (LAN)
          │
   ┌──────┴──────┐
   │ Laptop 2..6 │
   │ (app.py)    │
   └─────────────┘
```

### Mechanizmy powłoki – trzy warstwy

Powłoka `app.py` operuje na trzech warstwach, od najprostszej do najbardziej zaawansowanej. Implementacja może zacząć od Warstwy 1 i iteracyjnie dodawać kolejne.

#### Warstwa 1: Filesystem overlay (kluczowa)

Gra LEGO Racers czyta swoje zasoby z katalogu instalacyjnego (głównie `LEGO.JAM` i pliki konfiguracyjne). Powłoka tworzy **wirtualny katalog gry** z nakładką (overlay), w której może podmieniać, dodawać lub modyfikować pliki, zanim gra je zobaczy.[20][21]

**Na Windows** – dwa podejścia:

| Metoda | Opis | Złożoność |
|--------|------|-----------|
| **Symlinki + katalog roboczy** | `app.py` tworzy tymczasowy katalog, kopiuje/linkuje oryginalne pliki gry, podmienia wybrane (np. savefile'e, config, MOD.JAM). Gra uruchamiana z tego katalogu. | Niska – czysty Python (`os.symlink`, `shutil`)[21] |
| **WinFsp / Dokan (FUSE for Windows)** | `app.py` montuje wirtualny filesystem, który przechwytuje każde odczytanie/zapisanie pliku przez grę. Może w locie podmieniać zawartość plików. | Wysoka – wymaga WinFsp + Python binding[22][23][19] |

**Rekomendacja**: Zacznij od **symlinki + katalog roboczy** – jest to proste, nie wymaga sterowników, i daje wystarczającą kontrolę nad plikami gry.

```python
import os
import shutil
from pathlib import Path

class GameShell:
    def __init__(self, original_game_dir: str, workspace_dir: str):
        self.original = Path(original_game_dir)
        self.workspace = Path(workspace_dir)

    def create_overlay(self):
        """Tworzy katalog roboczy z linkami do oryginalnych plików."""
        self.workspace.mkdir(parents=True, exist_ok=True)
        for item in self.original.iterdir():
            target = self.workspace / item.name
            if not target.exists():
                if item.is_file():
                    os.symlink(item, target)  # symlink do oryginału
                elif item.is_dir():
                    os.symlink(item, target, target_is_directory=True)

    def override_file(self, filename: str, content: bytes):
        """Podmienia plik w workspace (zrywa symlink, tworzy prawdziwy plik)."""
        target = self.workspace / filename
        if target.is_symlink():
            target.unlink()
        target.write_bytes(content)

    def launch_game(self):
        """Uruchamia grę z katalogu workspace."""
        import subprocess
        exe = self.workspace / "LEGORacers.exe"
        subprocess.Popen([str(exe)], cwd=str(self.workspace))
```

#### Warstwa 2: File watcher (synchronizacja zdarzeń)

Po uruchomieniu gry, powłoka monitoruje zmiany w plikach katalogu roboczego za pomocą biblioteki `watchdog`. Gdy gra zapisuje coś (np. stan wyścigu, savefile, config), `app.py` natychmiast to wykrywa i może:[24][18]

- Wysłać zmianę do serwera (event: "plik X zmieniony, nowa zawartość")
- Odebrać od serwera zmodyfikowane pliki od innych graczy i zapisać je do workspace
- Reagować na zdarzenia gry odczytane z plików

```python
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler

class GameFileHandler(FileSystemEventHandler):
    def __init__(self, network_client):
        self.net = network_client

    def on_modified(self, event):
        if event.is_directory:
            return
        filename = os.path.basename(event.src_path)
        # Pliki, które nas interesują:
        if filename in ('LEGO.JAM', 'save.dat', 'race_state.bin'):
            with open(event.src_path, 'rb') as f:
                data = f.read()
            self.net.send_file_update(filename, data)

    def on_created(self, event):
        if not event.is_directory:
            self.on_modified(event)

def start_watcher(game_dir: str, network_client):
    handler = GameFileHandler(network_client)
    observer = Observer()
    observer.schedule(handler, game_dir, recursive=True)
    observer.start()
    return observer
```

#### Warstwa 3: Memory R/W (opcjonalna, zaawansowana)

Dla danych, które gra nie zapisuje do plików (pozycje, rotacje, prędkości – trzymane tylko w RAM), powłoka może dodatkowo czytać i zapisywać pamięć procesu gry za pomocą `ctypes` + Windows API.[25][26][27]

```python
import ctypes
import ctypes.wintypes

PROCESS_ALL_ACCESS = 0x1F0FFF
kernel32 = ctypes.windll.kernel32

class MemoryAccess:
    def __init__(self, process_name: str = "LEGORacers.exe"):
        self.pid = self._find_pid(process_name)
        self.handle = kernel32.OpenProcess(PROCESS_ALL_ACCESS, False, self.pid)

    def read_float(self, address: int) -> float:
        buf = ctypes.c_float()
        bytes_read = ctypes.c_size_t()
        kernel32.ReadProcessMemory(
            self.handle, ctypes.c_void_p(address),
            ctypes.byref(buf), ctypes.sizeof(buf),
            ctypes.byref(bytes_read)
        )
        return buf.value

    def write_float(self, address: int, value: float):
        buf = ctypes.c_float(value)
        bytes_written = ctypes.c_size_t()
        kernel32.WriteProcessMemory(
            self.handle, ctypes.c_void_p(address),
            ctypes.byref(buf), ctypes.sizeof(buf),
            ctypes.byref(bytes_written)
        )

    def read_position(self, base_addr: int) -> tuple:
        x = self.read_float(base_addr + 0x00)
        y = self.read_float(base_addr + 0x04)
        z = self.read_float(base_addr + 0x08)
        return (x, y, z)

    def _find_pid(self, name):
        import psutil
        for proc in psutil.process_iter(['pid', 'name']):
            if proc.info['name'] == name:
                return proc.info['pid']
        raise RuntimeError(f"Process {name} not found")
```

To pozwala na klasyczną synchronizację pozycji jak w legoracersonline, ale sterowaną z Pythona zamiast z C# DLL.[3][5]

### Synchronizacja przez powłokę – co idzie jakim kanałem

| Dane | Warstwa powłoki | Kanał sieciowy | Kierunek |
|------|----------------|----------------|----------|
| Pozycja, rotacja, prędkość gracza | Warstwa 3 (memory) LUB Warstwa 2 (jeśli gra zapisuje do pliku) | UDP, 30-60 Hz | Klient → Serwer → Broadcast |
| Pozycje innych graczy (zapis do enemy slotów) | Warstwa 3 (memory write) | UDP (snapshot z serwera) | Serwer → Klient |
| Użycie power-upa | Warstwa 3 (memory hook) lub Warstwa 2 (file event) | TCP | Klient → Serwer → Broadcast |
| Start/koniec wyścigu | Warstwa 1 (podmiana pliku konfiguracyjnego) + Warstwa 3 | TCP | Serwer → Klienci |
| Zmiana toru/levelu | Warstwa 1 (podmiana LEGO.JAM / MOD.JAM) | TCP | Serwer → Klienci |
| Dołączenie/odłączenie gracza | - | TCP | Klient ↔ Serwer |

### Struktura app.py – główna aplikacja gracza

```python
import asyncio
from game_shell import GameShell
from file_watcher import start_watcher
from memory_access import MemoryAccess  # opcja
from network_client import NetworkClient

class App:
    def __init__(self, config):
        self.shell = GameShell(
            original_game_dir=config['game_dir'],
            workspace_dir=config['workspace_dir']
        )
        self.net = NetworkClient(
            server_host=config['server_ip'],
            tcp_port=config['tcp_port'],
            udp_port=config['udp_port'],
            nickname=config['nickname']
        )
        self.memory = None  # inicjalizowane po uruchomieniu gry

    async def run(self):
        # 1. Tworzenie overlay katalogu
        self.shell.create_overlay()

        # 2. Połączenie z serwerem
        await self.net.connect()

        # 3. Pobranie ewentualnych plików od serwera (mody, config)
        server_files = await self.net.get_lobby_files()
        for fname, data in server_files.items():
            self.shell.override_file(fname, data)

        # 4. Uruchomienie gry z workspace
        self.shell.launch_game()

        # 5. Uruchomienie watchera plików
        self.watcher = start_watcher(
            str(self.shell.workspace), self.net
        )

        # 6. (opcja) Podpięcie memory access po starcie gry
        await asyncio.sleep(3)  # czekaj na załadowanie gry
        self.memory = MemoryAccess()

        # 7. Główna pętla: czytaj pozycję, wysyłaj, odbieraj, zapisuj
        await asyncio.gather(
            self.position_send_loop(),
            self.position_recv_loop(),
            self.net.event_listener()
        )

    async def position_send_loop(self):
        while True:
            if self.memory and self.net.race_active:
                pos = self.memory.read_position(PLAYER_BASE_ADDR)
                rot = self.memory.read_rotation(PLAYER_BASE_ADDR)
                vel = self.memory.read_velocity(PLAYER_BASE_ADDR)
                self.net.send_position(pos, rot, vel)
            await asyncio.sleep(1 / 30)  # 30 Hz

    async def position_recv_loop(self):
        while True:
            snapshot = await self.net.recv_snapshot()
            if snapshot and self.memory:
                for player in snapshot.players:
                    if player.id != self.net.my_id:
                        slot_addr = ENEMY_SLOT_ADDRS[player.id]
                        self.memory.write_position(slot_addr, player.position)
                        self.memory.write_rotation(slot_addr, player.rotation)

if __name__ == '__main__':
    import yaml
    with open('config.yaml') as f:
        config = yaml.safe_load(f)
    app = App(config)
    asyncio.run(app.run())
```

### Zalety podejścia powłoki lokalnej

- **Niski lag**: gra renderuje się lokalnie na laptopie gracza – zero opóźnienia video/input.
- **Pełna kontrola nad plikami**: powłoka może podmienić dowolny plik gry (tory, tekstury, konfigurację, save'y) przed i w trakcie rozgrywki.
- **Czysta dystrybucja**: gracz kopiuje folder gry + `app.py` + `config.yaml` i jest gotowy. Żadnych DLL do kompilowania (chyba że włączy Warstwę 3).
- **Modyfikowalność**: serwer może przesyłać zmodyfikowane pliki gry (np. nowe tory, zbalansowane power-upy) do wszystkich klientów przez TCP przed startem wyścigu.
- **Kompatybilność z Warstwą 3**: jeśli kontrola plików nie wystarczy (bo pozycje nie są w plikach), `app.py` może dodatkowo czytać/pisać pamięć procesu – to ten sam program, ta sama powłoka.

### Wady i ograniczenia

- **Warstwa 3 wymaga Windows**: `ReadProcessMemory` / `WriteProcessMemory` to Windows API. Na Linuxie alternatywą jest `/proc/<pid>/mem`, ale gra działa przez WINE, co komplikuje adresy.[25]
- **LEGO Racers trzyma większość stanu w RAM, nie w plikach**: sama Warstwa 1+2 (filesystem) nie wystarczy do synchronizacji pozycji w czasie rzeczywistym. Warstwa 3 (memory) jest praktycznie konieczna dla pozycji/rotacji/prędkości.
- **Symlinki na Windows**: wymagają uprawnień administratora lub włączonego Developer Mode w Windows 10/11.

### Rekomendacja

Powłoka lokalna jest **głównym** podejściem do synchronizacji. Warstwa 1 (overlay) + Warstwa 3 (memory R/W) to minimum dla działającego multiplayera. Warstwa 2 (file watcher) dodaje elastyczność przy synchronizacji plików konfiguracyjnych i modów. Wszystkie trzy warstwy działają w jednym procesie `app.py` na każdym laptopie gracza.

***

## Adresy pamięci LEGO Racers

Poniższe adresy pochodzą z badań społeczności i projektu legoracersonline:[5]

### Pozycja gracza lokalnego

```
Base: [LEGORacers.exe + 0x000C67B0] → pointer, offset +0x518
  X:          base + 0x00  (float)
  Y:          base + 0x04  (float)
  Z:          base + 0x08  (float)
  Rotation Y: base + 0x20  (float)
  Rotation X: base + 0x24  (float)
```

### Pozycja przeciwnika (Enemy 1 / Champion slot)

```
Base: [LEGORacers.exe + 0x000C5258] → offsets: +0x794, +0x7A4, +0x4, +0x14, +0x514
  Dest X:     base + 0x00  (float)
  Dest Y:     base + 0x04  (float)
  Dest Z:     base + 0x08  (float)
  Rotation X: base + 0x14 AND 0x18  (float pair)
  Rotation Y: base + 0x24 AND 0x28  (float pair)
```

Rotacja jest przechowywana jako **6 floatów** – para wektorów forward/up (po 3 floaty każdy). Wartości rotacji gracza i przeciwnika **nie są identyczne** nawet przy tym samym obrocie – wymagają konwersji między formatami.[5]

### Wersje pliku wykonywalnego

Istnieją co najmniej 3 wersje LegoRacers.exe:[5]
- Oryginalna 1999: `325cbbedc9d745107bca4a8654fce4db` (987222 bytes)
- Re-release 2001: inna suma kontrolna (wspierana przez Lego-Racers-Mod ProxyLibrary)[2]
- Wersje patchowane: zmienione hashe – **nie są kompatybilne**

**WAŻNE**: Serwer i klienci **muszą** używać tej samej wersji exe. Klient powinien przy CONNECT_REQUEST wysyłać hash MD5 swojego LegoRacers.exe, a serwer odrzucać niezgodne wersje.

***

## Struktura serwera Python

### Główna pętla serwera (asyncio)

```python
import asyncio
import struct
import time
from dataclasses import dataclass, field
from typing import Dict, Optional

@dataclass
class PlayerState:
    player_id: int
    nickname: str
    position: tuple = (0.0, 0.0, 0.0)
    rotation: tuple = (0.0,) * 6  # forward_xyz + up_xyz
    velocity: tuple = (0.0, 0.0, 0.0)
    flags: int = 0
    last_tick: int = 0
    last_update_time: float = 0.0
    tcp_writer: Optional[asyncio.StreamWriter] = None
    udp_addr: Optional[tuple] = None

@dataclass
class GameServer:
    players: Dict[int, PlayerState] = field(default_factory=dict)
    max_players: int = 6
    tick_rate: int = 30
    current_tick: int = 0
    race_active: bool = False
    required_game_hash: str = ""

    async def run(self, host='0.0.0.0', tcp_port=27015, udp_port=27016):
        # Uruchom TCP i UDP jednocześnie
        tcp_server = await asyncio.start_server(
            self.handle_tcp_client, host, tcp_port
        )
        loop = asyncio.get_running_loop()
        udp_transport, _ = await loop.create_datagram_endpoint(
            lambda: UDPProtocol(self),
            local_addr=(host, udp_port)
        )
        # Główna pętla tick
        asyncio.create_task(self.tick_loop())
        async with tcp_server:
            await tcp_server.serve_forever()
```

### Pętla tick (snapshot broadcast)

```python
    async def tick_loop(self):
        while True:
            start = time.monotonic()
            self.current_tick += 1
            if self.race_active:
                snapshot = self.build_snapshot()
                await self.broadcast_udp(snapshot)
            elapsed = time.monotonic() - start
            sleep_time = max(0, (1.0 / self.tick_rate) - elapsed)
            await asyncio.sleep(sleep_time)

    def build_snapshot(self) -> bytes:
        header = struct.pack(HEADER_FORMAT, MsgType.STATE_SNAPSHOT, 1, 0, self.current_tick)
        snap_header = struct.pack(SNAPSHOT_HEADER_FORMAT,
            self.current_tick,
            int(time.monotonic() * 1000),
            len(self.players)
        )
        player_data = b''
        for pid, ps in self.players.items():
            player_data += struct.pack(PLAYER_STATE_FORMAT,
                ps.player_id,
                *ps.position, *ps.rotation, *ps.velocity,
                ps.flags, ps.last_tick
            )
        return header + snap_header + player_data
```

### Obsługa UDP (pozycje)

```python
class UDPProtocol(asyncio.DatagramProtocol):
    def __init__(self, server: GameServer):
        self.server = server
        self.transport = None

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        if len(data) < HEADER_SIZE:
            return
        msg_type, ver, flags, seq = struct.unpack(HEADER_FORMAT, data[:HEADER_SIZE])
        if msg_type == MsgType.POSITION_UPDATE:
            self.handle_position(data[HEADER_SIZE:], addr)

    def handle_position(self, payload, addr):
        pid, tick, ts, x, y, z, *rot, vx, vy, vz, flags = struct.unpack(
            POSITION_FORMAT, payload
        )
        player = self.server.players.get(pid)
        if not player:
            return
        # Walidacja: odrzuć absurdalne prędkości
        speed = (vx**2 + vy**2 + vz**2) ** 0.5
        if speed > MAX_ALLOWED_SPEED:
            return
        player.position = (x, y, z)
        player.rotation = tuple(rot)
        player.velocity = (vx, vy, vz)
        player.flags = flags
        player.last_tick = tick
        player.last_update_time = time.monotonic()
        player.udp_addr = addr
```

### Obsługa TCP (eventy)

```python
    async def handle_tcp_client(self, reader, writer):
        addr = writer.get_extra_info('peername')
        try:
            while True:
                header_data = await reader.readexactly(HEADER_SIZE)
                msg_type, ver, flags, seq = struct.unpack(HEADER_FORMAT, header_data)

                if msg_type == MsgType.CONNECT_REQUEST:
                    await self.handle_connect(reader, writer)
                elif msg_type == MsgType.POWERUP_USE:
                    await self.handle_powerup(reader, writer)
                elif msg_type == MsgType.DISCONNECT:
                    await self.handle_disconnect(writer)
                    break
        except asyncio.IncompleteReadError:
            await self.handle_disconnect(writer)
```

***

## Obsługa do 6 graczy

### Sloty przeciwników w pamięci gry

Oryginalna gra ma sloty na „przeciwników" (AI drivers). Przy multiplayerze klient musi:[5]
- Wyłączyć ładowanie ścieżek AI (funkcja już odkryta przez Grappigegovert)[28]
- Zamapować 5 slotów przeciwników na 5 zdalnych graczy
- Zapisywać do pamięci przeciwników pozycje otrzymane z serwera

Każdy gracz ma przypisany `player_id` (0-5). Gracz o `player_id=0` pisze do slotu „Player", a słoty enemy 1-5 otrzymują dane od graczy 1-5.

### Skalowanie pakietów

Przy 6 graczach, 30 tickach/sek, jeden STATE_SNAPSHOT waży:

```
Header: 8B + Snapshot header: 9B + 6 × PlayerState: 6 × 54B = 324B
Total: ~341 bajtów per snapshot
Bandwidth: 341B × 30/s × 6 klientów ≈ 60 KB/s outbound
```

To jest minimalny ruch sieciowy – LAN bez problemu to obsłuży.[8][9]

***

## Sekwencja gry (state machine)

```
IDLE → LOBBY → COUNTDOWN → RACING → RESULTS → LOBBY
```

| Stan | Opis | Serwer robi |
|------|-------|------------|
| IDLE | Serwer uruchomiony, czeka na graczy | Nasłuchuje TCP |
| LOBBY | 1-6 graczy podłączonych | Broadcastuje LOBBY_STATE (lista graczy, gotowość) |
| COUNTDOWN | Host rozpoczął wyścig | Wysyła RACE_START z track_id, zaczyna odliczanie |
| RACING | Wyścig trwa | Tick loop: zbiera pozycje, buduje snapshoty, broadcastuje, waliduje power-upy |
| RESULTS | Wszyscy ukończyli lub timeout | Wysyła ranking, czeka na powrót do LOBBY |

***

## Klient C++ – co musi robić

Klient-side DLL (oparty na ProxyLibrary z Lego-Racers-Mod jako DINPUT.dll proxy) realizuje:[2]

1. **Przy starcie gry**: ładuje się jako proxy DLL, otwiera połączenie TCP do serwera, wysyła CONNECT_REQUEST.
2. **W lobby**: odbiera LOBBY_STATE, wyświetla info (opcjonalnie overlay).
3. **Przy starcie wyścigu**: odbiera RACE_START, wyłącza AI paths, ustawia track.
4. **W pętli wyścigu** (30-60 Hz):
   - Czyta z pamięci: pozycja gracza, rotacja (6 floatów), prędkość
   - Pakuje do POSITION_UPDATE i wysyła UDP do serwera
   - Odbiera STATE_SNAPSHOT z serwera
   - Dla każdego zdalnego gracza: interpoluje pozycję i zapisuje do odpowiedniego slotu enemy w pamięci gry
5. **Power-upy**: hookuje funkcję użycia power-upa, wysyła POWERUP_USE po TCP, a przy odbiorze cudzego power-upa wywołuje efekt w grze.

***

## Plan implementacji – kolejność kroków

### Faza 1: Minimalny serwer (MVP)

- [ ] Serwer Python z asyncio: TCP accept + CONNECT handshake
- [ ] Przechowywanie listy graczy w pamięci
- [ ] UDP echo: odbieraj POSITION_UPDATE, odsyłaj surowe pozycje wszystkich graczy
- [ ] Test z prostym klientem Python (symulujący grę) – weryfikacja protokołu

### Faza 2: Klient DLL (proof of concept)

- [ ] Fork Lego-Racers-Mod ProxyLibrary[2]
- [ ] Dodaj wątek sieciowy w DLL (TCP connect, UDP send/recv)
- [ ] Czytaj pozycję gracza z pamięci i wysyłaj do serwera
- [ ] Odbieraj pozycję 1 zdalnego gracza i zapisz do enemy slot 1
- [ ] Test: 2 instancje gry na jednym PC → widzą się nawzajem

### Faza 3: Pełny multiplayer

- [ ] Rozszerz na 6 graczy (5 enemy slotów)
- [ ] Implementuj interpolację po stronie klienta
- [ ] Dodaj dead reckoning między snapshotami
- [ ] Synchronizacja power-upów (TCP)
- [ ] Start wyścigu z serwera (TCP RACE_START)
- [ ] Wyłączanie AI paths dla slotów multiplayer

### Faza 4: Stabilność i polish

- [ ] Timeout/reconnect gracza
- [ ] Walidacja prędkości po stronie serwera
- [ ] Obsługa utraty pakietów i jitteru
- [ ] Lobby UI (może prosty web dashboard na serwerze)
- [ ] Testy z 6 graczami na prawdziwym LAN

### Faza 5 (opcjonalna): Sandbox

- [ ] Docker + WINE kontener z LEGO Racers
- [ ] Orchestrator Python do zarządzania 6 kontenerami
- [ ] Bezpośredni odczyt/zapis `/proc/<pid>/mem` z orchestratora
- [ ] Video streaming (noVNC) do graczy
- [ ] Input forwarding przez sieć

***

## Narzędzia i zależności

| Narzędzie | Zastosowanie |
|-----------|-------------|
| **Python 3.10+** | Serwer (asyncio, struct, dataclasses) |
| **Visual Studio 2022** | Kompilacja klienta DLL i ProxyLibrary[2] |
| **Cheat Engine** | Weryfikacja adresów pamięci, szukanie nowych[5] |
| **Wireshark** | Sniffing pakietów TCP/UDP podczas testów |
| **x64dbg / IDA / Ghidra** | Reverse engineering LegoRacers.exe (jeśli potrzeba nowych adresów) |
| **Docker + scottyhardy/docker-wine** | Podejście sandbox[29][30] |
| **Git** | Wersjonowanie kodu |

***

## Zasady dla asystenta kodowania

1. **Serwer jest standalone** – nie wymaga żadnych plików gry, nie dotyka procesów, działa na dowolnym OS z Pythonem.
2. **Binary protocol** – używaj `struct.pack`/`struct.unpack` z big-endian (`>`) do wszystkich pakietów. Żadnego JSON, XML, protobuf – surowe bajty dla minimalnego lagu.[31][7]
3. **asyncio** – jeden event loop obsługuje i TCP i UDP. Żadnych threadów do networking (poza opcjonalnym workerem do logowania).[32]
4. **Walidacja zawsze** – każdy pakiet jest sprawdzany: rozmiar, typ wiadomości, player_id, zakres wartości pozycji/prędkości.
5. **Nie zgaduj adresów pamięci** – klient używa wyłącznie zweryfikowanych adresów z dokumentacji. Nowe adresy muszą być potwierdzone przez Cheat Engine.[5]
6. **Interpolacja po stronie klienta** – serwer wysyła surowy stan, klient robi lerp/dead reckoning. Serwer nie interpoluje.
7. **Testuj inkrementalnie** – najpierw TCP handshake, potem UDP echo, potem 2 graczy, potem 6.
8. **Loguj wszystko** – serwer loguje do pliku każde connect/disconnect, błędy walidacji, tick timing. To kluczowe przy debugowaniu desync.
9. **Sandbox jest opcją** – implementuj jako osobny moduł/tryb, nie jako zależność rdzenia serwera.
10. **Kompatybilność z legoracersonline** – staraj się, żeby format pakietów był bliski oryginałowi, ale nie kosztem czytelności. Dokument ten jest nadrzędny wobec oryginalnego kodu.