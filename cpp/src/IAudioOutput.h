#pragma once

#include "Speaker.h"
#include <vector>
#include <functional>
#include <cstddef>

/**
 * Abstract audio output backend.
 * Process callback receives planar buffers: outChannels[ch][frame] for ch in [0, numChannels-1].
 * Callback must be realtime-safe.
 */
class IAudioOutput {
public:
    using ProcessCallback = std::function<void(float** outChannels, unsigned long nframes)>;

    virtual ~IAudioOutput() = default;

    /**
     * Initialize with speaker layout. numChannels = speakers.size().
     * Port names will use speaker.id (e.g. spk_1, spk_2).
     */
    virtual bool Initialize(const std::vector<Speaker>& speakers, int sampleRate) = 0;

    virtual bool Start(ProcessCallback callback) = 0;
    virtual void Stop() = 0;

    virtual int GetNumChannels() const = 0;
    virtual int GetSampleRate() const = 0;
};
