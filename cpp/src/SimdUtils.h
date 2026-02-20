#pragma once

#include <cstddef>

#if (defined(__SSE__) || defined(_M_X64) || defined(_M_IX86)) && !defined(__arm64__) && !defined(_M_ARM64)
  #include <xmmintrin.h>
  #define SOUNDARRAY_USE_SIMD 1
#endif

namespace SimdUtils {

inline void AccumulateGains(float sample, const float* gains, float* output, int numChannels) {
#ifdef SOUNDARRAY_USE_SIMD
    const float* g = gains;
    float* o = output;
    int ch = 0;

    __m128 vsample = _mm_set1_ps(sample);
    for (; ch + 4 <= numChannels; ch += 4, g += 4, o += 4) {
        __m128 vg = _mm_loadu_ps(g);
        __m128 vo = _mm_loadu_ps(o);
        __m128 vmul = _mm_mul_ps(vsample, vg);
        __m128 vout = _mm_add_ps(vo, vmul);
        _mm_storeu_ps(o, vout);
    }

    for (; ch < numChannels; ++ch, ++g, ++o) {
        *o += sample * (*g);
    }
#else
    for (int ch = 0; ch < numChannels; ++ch) {
        output[ch] += sample * gains[ch];
    }
#endif
}
}
