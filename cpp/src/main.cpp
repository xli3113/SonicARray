#include <iostream>
#include <iomanip>
#include <sstream>
#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <chrono>
#include <thread>
#include "ConfigLoader.h"
#include "AudioEngine.h"
#include "SpatialRenderer.h"
#include "OSCReceiver.h"

#ifdef _WIN32
#include <windows.h>
#include <conio.h>
#endif

// Cross-platform tick counter (milliseconds since an arbitrary epoch)
static uint32_t GetTickMs() {
    using namespace std::chrono;
    return (uint32_t)duration_cast<milliseconds>(
        steady_clock::now().time_since_epoch()).count();
}

static void SleepMs(int ms) {
    std::this_thread::sleep_for(std::chrono::milliseconds(ms));
}

// ── ANSI helpers ──────────────────────────────────────────────────────────────
#define A_RESET  "\033[0m"
#define A_BOLD   "\033[1m"
#define A_DIM    "\033[2m"
#define A_GREEN  "\033[32m"
#define A_YELLOW "\033[33m"
#define A_CYAN   "\033[36m"
#define A_RED    "\033[31m"
#define A_WHITE  "\033[97m"
#define A_CLREOL "\033[K"   // clear to end of line

static void EnableAnsiConsole()
{
#ifdef _WIN32
    // ENABLE_VIRTUAL_TERMINAL_PROCESSING may be missing in older MinGW SDK headers
#   ifndef ENABLE_VIRTUAL_TERMINAL_PROCESSING
#   define ENABLE_VIRTUAL_TERMINAL_PROCESSING 0x0004
#   endif
    HANDLE h = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD  mode = 0;
    if (GetConsoleMode(h, &mode))
        SetConsoleMode(h, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    SetConsoleOutputCP(CP_UTF8);
#endif
}

static void PauseBeforeExit()
{
#ifdef _WIN32
    std::cout << "\nPress Enter to exit.\n";
    std::cin.clear();
    std::cin.get();
#endif
}

// ── ASCII bargraph (width chars) ─────────────────────────────────────────────
static std::string GainBar(float gain, int width = 16)
{
    int filled = static_cast<int>(gain * width + 0.5f);
    filled = std::max(0, std::min(filled, width));
    return std::string(filled, '#') + std::string(width - filled, '-');
}

// ── Fixed-width padded field ──────────────────────────────────────────────────
static std::string Pad(const std::string& s, int w, char fill = ' ')
{
    if ((int)s.size() >= w) return s.substr(0, w);
    return s + std::string(w - (int)s.size(), fill);
}

// ── Format elapsed seconds as HH:MM:SS ───────────────────────────────────────
static std::string FormatUptime(uint32_t sec)
{
    char buf[16];
    snprintf(buf, sizeof(buf), "%02u:%02u:%02u",
             sec / 3600, (sec / 60) % 60, sec % 60);
    return buf;
}

// ── Dashboard state (persisted across calls) ─────────────────────────────────
static int      s_dashLines    = 0;   // lines last printed (for cursor-up)
static uint64_t s_lastPktCount = 0;
static uint32_t s_lastPktTick  = 0;
static double   s_oscRate      = 0.0; // packets / second

// Previous speaker-gain snapshot — redraw only when this changes
static std::vector<float> s_prevSpkGain;

// Print the full multi-line dashboard, replacing the previous one in-place.
static void PrintDashboard(
    SpatialRenderer*           renderer,
    OSCReceiver*               oscReceiver,
    AudioEngine*               engine,
    const std::vector<Speaker>& speakers,
    uint32_t                    startTick)
{
    // ── update pkt/s ──
    uint64_t curPkt  = oscReceiver ? oscReceiver->GetPacketCount() : 0;
    uint32_t nowTick = GetTickMs();
    if (s_lastPktTick > 0) {
        double dt = (nowTick - s_lastPktTick) / 1000.0;
        if (dt > 0.0) s_oscRate = (curPkt - s_lastPktCount) / dt;
    }
    s_lastPktCount = curPkt;
    s_lastPktTick  = nowTick;

    // ── build current speaker gains; skip redraw if unchanged ──
    int numSpkEarly = static_cast<int>(speakers.size());
    int maxSrcEarly = renderer ? renderer->GetMaxSources() : 0;
    std::vector<float> curSpkGain(numSpkEarly, 0.0f);
    for (int s = 0; s < maxSrcEarly && renderer; ++s) {
        std::vector<float> g = renderer->CopyGainsForSource(s);
        for (int i = 0; i < numSpkEarly && i < (int)g.size(); ++i)
            if (g[i] > curSpkGain[i]) curSpkGain[i] = g[i];
    }
    // Round to 2 decimal places so tiny float drift doesn't trigger redraws
    for (float& v : curSpkGain)
        v = std::round(v * 100.0f) / 100.0f;

    if (curSpkGain == s_prevSpkGain) return;  // nothing changed — skip
    s_prevSpkGain = curSpkGain;

    uint32_t uptimeSec = (nowTick - startTick) / 1000;
    bool live = (s_oscRate > 0.5 || (curPkt > 0 && uptimeSec < 5));

    std::string questIP = oscReceiver ? oscReceiver->GetLastSenderIP() : "";
    if (questIP.empty()) questIP = "---.---.---.---";

    // ── collect source positions ──
    std::array<std::array<float, 3>, OSCReceiver::kMaxSources> positions{};
    int numPos = oscReceiver ? oscReceiver->GetSourcePositions(positions) : 0;

    // Use the already-computed snapshot
    int numSpk = numSpkEarly;
    const std::vector<float>& spkGain = s_prevSpkGain;

    // ── build lines ──────────────────────────────────────────────────
    std::vector<std::string> lines;
    const int W = 52; // total display width

    auto ln = [&](std::string s) {
        // Pad or truncate to W then append ANSI clear-to-eol
        if ((int)s.size() < W) s += std::string(W - (int)s.size(), ' ');
        else                   s  = s.substr(0, W);
        lines.push_back(s + A_CLREOL);
    };

    auto sep = [&]() {
        ln(A_DIM + std::string(W, '=') + A_RESET);
    };

    // Title
    sep();
    {
        std::string title = "  " A_BOLD A_WHITE "SonicARray  Backend Dashboard" A_RESET;
        ln(title);
    }
    sep();

    // Status row
    {
        std::string liveStr = live
            ? (A_GREEN "[LIVE]" A_RESET)
            : (A_RED   "[NO SIGNAL]" A_RESET);
        std::ostringstream ss;
        ss << "  Uptime  " A_CYAN << FormatUptime(uptimeSec) << A_RESET
           << "   OSC  " << A_YELLOW << std::fixed << std::setprecision(1)
           << s_oscRate << " pkt/s" A_RESET "  " << liveStr;
        ln(ss.str());
    }
    {
        uint64_t gainsSent = engine ? engine->GetGainsSentCount() : 0;
        int replyPort = oscReceiver ? oscReceiver->GetLastSenderPort() : 0;
        std::string replyTarget = questIP + ":" + (replyPort > 0 ? std::to_string(replyPort) : "?");
        std::ostringstream ss;
        ss << "  Quest   " A_CYAN << questIP << A_RESET
           << "   Rx " << curPkt << " pkts";
        ln(ss.str());
        std::ostringstream ss2;
        ss2 << "  Gains-> " A_YELLOW << replyTarget << A_RESET
            << "   Tx " << gainsSent << " pkts";
        ln(ss2.str());
    }

    // Sources
    sep();
    {
        // Count active sources (non-zero position)
        int activeSrc = 0;
        for (int s = 0; s < numPos; ++s) {
            auto& p = positions[s];
            if (p[0] != 0.0f || p[1] != 0.0f || p[2] != 0.0f) activeSrc++;
        }
        std::ostringstream ss;
        ss << "  Sources  (" << activeSrc << " / " << OSCReceiver::kMaxSources << " active)";
        ln(ss.str());
    }
    {
        bool anySrc = false;
        for (int s = 0; s < numPos; ++s) {
            auto& p = positions[s];
            if (p[0] == 0.0f && p[1] == 0.0f && p[2] == 0.0f) continue;
            anySrc = true;
            std::ostringstream ss;
            ss << "   [" << s << "]"
               << "  x=" << std::setw(6) << std::fixed << std::setprecision(2) << p[0]
               << "  y=" << std::setw(6) << p[1]
               << "  z=" << std::setw(6) << p[2];
            ln(ss.str());
        }
        if (!anySrc) ln("   -- no position data received yet --");
    }

    // Speakers
    sep();
    ln("  Speakers");
    {
        // Sort indices by gain descending for display
        std::vector<int> order(numSpk);
        for (int i = 0; i < numSpk; ++i) order[i] = i;
        std::sort(order.begin(), order.end(),
                  [&](int a, int b){ return spkGain[a] > spkGain[b]; });

        bool anyActive = false;
        for (int rank = 0; rank < numSpk; ++rank) {
            int    i    = order[rank];
            float  gain = spkGain[i];
            if (gain < 0.01f) break;  // remaining are all zero
            anyActive = true;

            std::string bar = GainBar(gain);
            std::ostringstream ss;
            ss << "   spk" << std::setw(2) << speakers[i].id
               << "  [" << A_CYAN << bar << A_RESET << "]"
               << "  " << A_YELLOW << std::fixed << std::setprecision(2) << gain << A_RESET;
            ln(ss.str());
        }
        if (!anyActive) ln("   -- all silent --");
    }

    sep();
    ln("  " A_DIM "[Enter] quit" A_RESET);

    // ── move cursor up to overwrite previous dashboard ────────────────
    if (s_dashLines > 0)
        std::cout << "\033[" << s_dashLines << "A";

    for (auto& l : lines)
        std::cout << l << "\n";

    std::cout << std::flush;
    s_dashLines = static_cast<int>(lines.size());
}

// ─────────────────────────────────────────────────────────────────────────────
int main(int argc, char* argv[])
{
    EnableAnsiConsole();
    std::cout << A_BOLD A_WHITE "=== SonicARray ===" A_RESET "\n";

    std::string yamlPath = (argc > 1) ? argv[1] : "speakers.yaml";
    std::vector<Speaker> speakers;

    if (!ConfigLoader::LoadSpeakers(yamlPath, speakers) || speakers.empty()) {
        std::cerr << "Failed to load speakers from " << yamlPath << "\n";
        PauseBeforeExit();
        return 1;
    }

    AudioEngine engine;
    if (!engine.Initialize(speakers)) {
        std::cerr << "Engine init failed.\n";
        PauseBeforeExit();
        return 1;
    }

    // Audio setup:
    //   No extra args      → 8 sine waves at C major scale (C4–C5)
    //   1 .wav arg          → that file on src0, remaining srcs use sine waves
    //   Up to 8 .wav args   → each file assigned to src0..srcN
    int numWavArgs = argc - 2;
    if (numWavArgs <= 0) {
        engine.EnableSineWave(true);
        std::cout << "Audio: sine waves — C4 D4 E4 F4 G4 A4 B4 C5\n";
    } else {
        for (int i = 0; i < numWavArgs && i < 8; ++i) {
            if (!engine.LoadAudioFile(i, argv[2 + i]))
                std::cerr << "Warning: failed to load " << argv[2 + i]
                          << " for src" << i << ", using sine wave\n";
        }
        std::cout << "Audio: loaded " << numWavArgs << " wav file(s)\n";
    }

    if (!engine.Start()) {
        std::cerr << "Engine start failed.\n";
        PauseBeforeExit();
        return 1;
    }

    std::cout << "Listening for OSC on /spatial/source_pos  (UDP :7000)\n";
    std::cout << "Dashboard refreshes every 0.5 s — press Enter to quit.\n\n";

    SpatialRenderer* renderer    = engine.GetRenderer();
    OSCReceiver*     oscReceiver = engine.GetOSCReceiver();

    uint32_t startTick   = GetTickMs();
    uint32_t lastDashTick = startTick;
    bool     shouldExit  = false;

    while (!shouldExit && renderer != nullptr) {
        SleepMs(50);

        uint32_t now = GetTickMs();
        if (now - lastDashTick >= 500) {
            engine.SendSpeakerGainsToUnity();
            PrintDashboard(renderer, oscReceiver, &engine, speakers, startTick);
            lastDashTick = now;
        }

#ifdef _WIN32
        if (_kbhit()) {
            int c = _getch();
            if (c == '\r' || c == '\n' || c == 13)
                shouldExit = true;
        }
#endif
    }

    engine.Stop();
    engine.Shutdown();
    PauseBeforeExit();
    return 0;
}
