# SonicARray

<!-- ===== LOGO PLACEMENT =====
     Put your logo right here, below the title, above the description.
     Recommended: a centered image, 200–400 px wide.

       <p align="center">
         <img src="docs/logo.png" alt="SonicARray logo" width="300">
       </p>

     For a wide/banner-style logo, use width="100%" or width="600".
     ============================= -->

**AR-assisted spatial audio visualization for multi-speaker VBAP rendering.**

SonicARray lets you place and drag virtual sound sources inside a real speaker array using a Meta Quest 3 headset. Coloured lines connect each source to its active speakers; line width and brightness reflect the live VBAP gain computed by a C++ backend running on a connected PC. Built as a research tool for studying how AR visualization helps people understand spatial audio routing.

---

## System Overview

```
┌─────────────────────────────┐   OSC / UDP :7000 (src :7002)   ┌──────────────────────────────┐
│   Meta Quest 3  (Unity AR)  │ ──────────────────────────────► │   C++ Backend  (PC / Linux)  │
│                             │                                  │                              │
│  • AR passthrough           │ ◄────────────────────────────── │  • VBAP renderer             │
│  • Drag source balls        │  OSC / UDP :7002 (src :7000)    │  • PortAudio / JACK output   │
│  • Visualise speaker lines  │                                  │  • Terminal dashboard        │
└─────────────────────────────┘                                  └──────────────────────────────┘
                                                                              │
                                                                       speakers.yaml
                                                                      (speaker layout)
```

**Data flow:**
1. Unity streams each source's 3-D position (relative to the listener) to the backend over OSC/UDP.
2. The C++ backend runs VBAP, computes per-speaker gains, and plays audio through the physical speaker array.
3. Gains are sent back to Unity over TCP; Unity draws gain-weighted lines from each source to its active speakers.

---

## Features

- **Up to 8 simultaneous spatial sources** draggable in AR
- **Real-time VBAP** with gain smoothing (lock-free, audio-thread safe)
- **Live terminal dashboard** — uptime, OSC packet rate, per-speaker gain bars
- **Flexible speaker layout** — edit `speakers.yaml`; no recompile needed
- **Cross-platform backend** — PortAudio on Windows/macOS, JACK on Linux
- **Hand-tracking & controller** support via Meta XR SDK

---

## Repository Structure

```
SonicARray/
├── cpp/                       # C++ backend
│   ├── src/                   # AudioEngine, VBAPRenderer, OSCReceiver, …
│   ├── third_party/           # oscpack, portaudio (vendored)
│   └── CMakeLists.txt
├── unity/                     # Unity AR frontend (Meta Quest 3)
│   └── Assets/Scripts/        # SpatialSource.cs, SpeakerManager.cs, OSCReceiver.cs, …
├── speakers.yaml              # Speaker layout (edit for your room)
└── user_study_draft.md        # Research protocol
```

---

## Requirements

### Backend (C++)

| Requirement | Notes |
|-------------|-------|
| CMake ≥ 3.10 | |
| C++17 compiler | MinGW-w64 on Windows; GCC/Clang on Linux/macOS |
| PortAudio | Windows/macOS — built automatically from `third_party/portaudio` |
| JACK | Linux — detected via pkg-config; enable with `-DUSE_JACK=ON` |
| oscpack | Vendored in `third_party/oscpack` |

### Frontend (Unity)

| Requirement | Notes |
|-------------|-------|
| Unity 2022 LTS or later | |
| Meta XR SDK (Core + Interaction) | Via Unity Package Manager or Meta developer portal |
| Android Build Support module | Target device: Meta Quest 3 |

---

## Building the C++ Backend

### Windows (MinGW-w64)

```bash
cd cpp
cmake -B build_mingw -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build build_mingw --config Release
```

The executable and `speakers.yaml` are copied automatically to `cpp/build_mingw/`.

### Linux (JACK)

```bash
cd cpp
cmake -B build -DUSE_JACK=ON -DCMAKE_BUILD_TYPE=Release
cmake --build build
```

### macOS (PortAudio)

```bash
cd cpp
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
```

---

## Running the Backend

```bash
# Default: load speakers.yaml from the same directory, play a sine wave
./SoundARray

# Custom speaker layout
./SoundARray /path/to/my_room.yaml

# WAV file as audio source
./SoundARray speakers.yaml recording.wav
```

The terminal dashboard refreshes every 500 ms. Press **Enter** to quit.

---

## Speaker Layout (`speakers.yaml`)

Coordinates follow the same convention as the Unity scene: **x = right, y = forward (front), z = up**. Edit this file to match your physical array; no recompile is needed.

```yaml
speakers:
  - id: 1
    x: -0.414
    y:  1.000
    z:  0.000
  - id: 2
    x:  0.414
    y:  1.000
    z:  0.000
  # add more speakers …
```

The project ships with a 13-speaker layout derived from the Couch 204 IEM AllRADecoder configuration.

---

## Unity Frontend Setup

1. Open the `unity/` folder as a Unity project.
2. Go to **Edit → Project Settings → XR Plug-in Management** and enable **Meta OpenXR**.
3. Open `Assets/Scenes/MainScene.unity`.
4. Select the **SpeakerManager** GameObject and set **Backend IP** to your PC's local IP address.
5. Copy `speakers.yaml` into `Assets/StreamingAssets/` so the app can load the layout at runtime.
6. Connect your Quest 3 and click **File → Build Settings → Android → Build and Run**.

> **Network note:** Both devices must be on the same Wi-Fi network. All communication is UDP — the backend uses a single socket on port 7000 for both receiving positions and replying with gains. On Windows, run `open_firewall.bat` as administrator to open port 7000.

---

## Network Ports

| Port | Protocol | Direction | Purpose |
|------|----------|-----------|---------|
| 7000 | UDP | Quest → PC | Source position OSC messages (`/spatial/source_pos`) — Quest sends from port 7002 |
| 7002 | UDP | PC → Quest | Per-speaker VBAP gains (`/spatial/speaker_gains`) — PC replies from port 7000 to Quest's port 7002 |

Both directions share the **same UDP socket** on the PC (bound to port 7000). Replies are sent from port 7000 so Android's stateful firewall recognises them as return traffic and allows them through.

On Windows, run `open_firewall.bat` (included) as administrator to open port 7000.

---

## Screenshots

<!-- ===== SCREENSHOT GUIDANCE =====

SHOULD YOU ADD SCREENSHOTS? Yes — strongly recommended for both sides.

BACKEND DASHBOARD (easy to capture right now):
  Run ./SoundARray, let it receive a few OSC packets, then screenshot the terminal.
  The dashboard shows uptime, packet rate, source XYZ positions, and gain bars.

    ![Backend dashboard](docs/screenshots/backend_dashboard.png)

UNITY / AR FRONTEND (capture from inside the headset):
  Use Meta Quest Developer Hub (MQDH) → Casting / Screenshot,
  or:  adb exec-out screencap -p > ar_view.png
  Best moments to capture:
    - A source ball with coloured lines reaching 2–3 active speakers
    - Two or more sources active at once (different line colours)
    - The in-headset status panel

    ![AR view — source lines to speakers](docs/screenshots/ar_source_lines.png)

One screenshot from each side is enough to make the README immediately clear
to someone who has never seen the system.
================================= -->

---

## Research Context

SonicARray was developed to support a user study comparing 2D video explanation against live AR interaction for teaching VBAP concepts. See [`user_study_draft.md`](user_study_draft.md) for the full protocol.

---

## Third-Party Libraries

| Library | License | Purpose |
|---------|---------|---------|
| [oscpack](https://github.com/RossBencina/oscpack) | BSD/MIT-like | OSC encoding & decoding |
| [PortAudio](http://www.portaudio.com) | MIT | Cross-platform audio I/O |
| [JACK Audio](https://jackaudio.org) | LGPL | Low-latency audio on Linux |
| Meta XR SDK | Meta Platform SDK License | AR passthrough & hand tracking |

---

## License

This project is released under the [MIT License](LICENSE).
