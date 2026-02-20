# SoundARray

Spatial audio for a 28–29 speaker array. C++ backend, OSC for position input.

VBAP does the panning. send a direction (x,y,z), pick 3 speakers that form a triangle containing it, solve for gains. the core: L^T g = p where L rows are the 3 normalized speaker directions, p is the source direction. so g = inv(L) p. clamp negatives to 0, L2-normalize.

we used to assume a grid layout for triangles. brittle. now geometry-based kNN — for each speaker grab K nearest neighbors, form triangles from all pairs, drop degenerate (tiny area), dedupe. way more triangles, better coverage.

triangle selection used to pick by max dot with triangle normal. often all-zero gains. now feasibility-driven: solve g = inv(L)*p for each candidate, keep only if all g ≥ 0, then pick largest min(g1,g2,g3). avoids edge cases. fallback to nearest single speaker when nothing fits.

backend double-buffered: OSC thread writes gains, audio callback reads. atomic flip. no locks in hot path.

multi-source, up to 8. SIMD mixing — SSE 4-wide on x86, scalar elsewhere. output += sample * gains per channel per source.

gain smoothing: 50 ms default, exponential.
