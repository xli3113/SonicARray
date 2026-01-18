#include "VBAPRenderer.h"
#include <cmath>
#include <algorithm>
#include <limits>
#include <iostream>

VBAPRenderer::VBAPRenderer()
    : smoothingTime_(0.05f) {
}

bool VBAPRenderer::Initialize(const std::vector<Speaker>& speakers) {
    speakers_ = speakers;
    
    if (speakers_.size() < 3) {
        std::cerr << "VBAPRenderer: Need at least 3 speakers" << std::endl;
        return false;
    }
    
    rawGains_.resize(speakers_.size(), 0.0f);
    smoothedGains_.resize(speakers_.size(), 0.0f);
    
    BuildTriangles();
    
    std::cout << "VBAPRenderer initialized with " << speakers_.size() << " speakers" << std::endl;
    return true;
}

VBAPRenderer::~VBAPRenderer() {
}

void VBAPRenderer::BuildTriangles() {
    // For a 2D grid layout, build triangles from adjacent speakers
    // This is a simplified approach - you may want to use Delaunay triangulation
    // for more complex layouts
    
    triangles_.clear();
    
    if (speakers_.size() < 3) {
        std::cerr << "Warning: Not enough speakers for VBAP (need at least 3)" << std::endl;
        return;
    }
    
    // Try to detect grid layout from speaker positions
    // For now, use a simple approach: create triangles from grid neighbors
    // Assuming speakers are arranged in a grid (you may need to adjust this)
    // Based on the default speakers.yaml: 8 columns x 4 rows (28 speakers)
    int gridWidth = 8; // Adjust based on your layout
    int gridHeight = (speakers_.size() + gridWidth - 1) / gridWidth; // Round up
    
    // If speakers don't form a perfect grid, use distance-based triangulation
    bool useGridLayout = (speakers_.size() == gridWidth * gridHeight || 
                         speakers_.size() == gridWidth * gridHeight - (gridWidth * gridHeight - speakers_.size()));
    
    if (useGridLayout && gridHeight > 1) {
        // Grid-based triangulation
        for (int row = 0; row < gridHeight - 1; ++row) {
            for (int col = 0; col < gridWidth - 1; ++col) {
                int idx1 = row * gridWidth + col;
                int idx2 = row * gridWidth + col + 1;
                int idx3 = (row + 1) * gridWidth + col;
                int idx4 = (row + 1) * gridWidth + col + 1;
                
                if (idx1 < static_cast<int>(speakers_.size()) && 
                    idx2 < static_cast<int>(speakers_.size()) && 
                    idx3 < static_cast<int>(speakers_.size())) {
                    triangles_.push_back({idx1, idx2, idx3});
                }
                if (idx2 < static_cast<int>(speakers_.size()) && 
                    idx3 < static_cast<int>(speakers_.size()) && 
                    idx4 < static_cast<int>(speakers_.size())) {
                    triangles_.push_back({idx2, idx3, idx4});
                }
            }
        }
    } else {
        // Fallback: create triangles from nearest neighbors
        // This is a simplified approach - for production, use Delaunay triangulation
        for (size_t i = 0; i < speakers_.size(); ++i) {
            // Find two nearest neighbors
            float minDist1 = std::numeric_limits<float>::max();
            float minDist2 = std::numeric_limits<float>::max();
            int nearest1 = -1, nearest2 = -1;
            
            for (size_t j = 0; j < speakers_.size(); ++j) {
                if (i == j) continue;
                
                float dx = speakers_[j].x - speakers_[i].x;
                float dy = speakers_[j].y - speakers_[i].y;
                float dz = speakers_[j].z - speakers_[i].z;
                float dist = std::sqrt(dx * dx + dy * dy + dz * dz);
                
                if (dist < minDist1) {
                    minDist2 = minDist1;
                    nearest2 = nearest1;
                    minDist1 = dist;
                    nearest1 = j;
                } else if (dist < minDist2) {
                    minDist2 = dist;
                    nearest2 = j;
                }
            }
            
            if (nearest1 >= 0 && nearest2 >= 0) {
                // Avoid duplicate triangles
                bool exists = false;
                for (const auto& tri : triangles_) {
                    if ((tri[0] == static_cast<int>(i) && tri[1] == nearest1 && tri[2] == nearest2) ||
                        (tri[0] == static_cast<int>(i) && tri[1] == nearest2 && tri[2] == nearest1) ||
                        (tri[0] == nearest1 && tri[1] == static_cast<int>(i) && tri[2] == nearest2) ||
                        (tri[0] == nearest1 && tri[1] == nearest2 && tri[2] == static_cast<int>(i)) ||
                        (tri[0] == nearest2 && tri[1] == static_cast<int>(i) && tri[2] == nearest1) ||
                        (tri[0] == nearest2 && tri[1] == nearest1 && tri[2] == static_cast<int>(i))) {
                        exists = true;
                        break;
                    }
                }
                if (!exists) {
                    triangles_.push_back({static_cast<int>(i), nearest1, nearest2});
                }
            }
        }
    }
    
    std::cout << "Built " << triangles_.size() << " triangles for VBAP" << std::endl;
}

void VBAPRenderer::UpdateSourcePosition(float x, float y, float z) {
    ComputeVBAP(x, y, z);
}

void VBAPRenderer::ComputeVBAP(float x, float y, float z) {
    // Reset gains
    std::fill(rawGains_.begin(), rawGains_.end(), 0.0f);
    
    // Normalize source direction
    float len = std::sqrt(x * x + y * y + z * z);
    if (len < 0.0001f) {
        return; // Source at origin, no gains
    }
    
    float nx = x / len;
    float ny = y / len;
    float nz = z / len;
    
    // Find best triangle
    int bestTriangle = FindBestTriangle(nx, ny, nz);
    
    if (bestTriangle >= 0) {
        ComputeTriangleGains(bestTriangle, nx, ny, nz);
    }
}

int VBAPRenderer::FindBestTriangle(float x, float y, float z) {
    int bestTriangle = -1;
    float maxDot = -1.0f;
    
    for (size_t i = 0; i < triangles_.size(); ++i) {
        const auto& tri = triangles_[i];
        if (tri.size() < 3) continue;
        
        // Get speaker positions
        const Speaker& s1 = speakers_[tri[0]];
        const Speaker& s2 = speakers_[tri[1]];
        const Speaker& s3 = speakers_[tri[2]];
        
        // Compute triangle normal (simplified for 2D case)
        float v1x = s2.x - s1.x;
        float v1y = s2.y - s1.y;
        float v1z = s2.z - s1.z;
        
        float v2x = s3.x - s1.x;
        float v2y = s3.y - s1.y;
        float v2z = s3.z - s1.z;
        
        // Cross product for normal
        float nx = v1y * v2z - v1z * v2y;
        float ny = v1z * v2x - v1x * v2z;
        float nz = v1x * v2y - v1y * v2x;
        
        Normalize(nx, ny, nz);
        
        // Dot product with source direction
        float dot = Dot(x, y, z, nx, ny, nz);
        
        if (dot > maxDot) {
            maxDot = dot;
            bestTriangle = i;
        }
    }
    
    return bestTriangle;
}

void VBAPRenderer::ComputeTriangleGains(int triangleIdx, float x, float y, float z) {
    if (triangleIdx < 0 || triangleIdx >= static_cast<int>(triangles_.size())) {
        return;
    }
    
    const auto& tri = triangles_[triangleIdx];
    if (tri.size() < 3) return;
    
    const Speaker& s1 = speakers_[tri[0]];
    const Speaker& s2 = speakers_[tri[1]];
    const Speaker& s3 = speakers_[tri[2]];
    
    // Build matrix L (loudspeaker base vectors)
    float L[3][3] = {
        {s1.x, s1.y, s1.z},
        {s2.x, s2.y, s2.z},
        {s3.x, s3.y, s3.z}
    };
    
    // Normalize columns
    for (int i = 0; i < 3; ++i) {
        float len = std::sqrt(L[i][0] * L[i][0] + L[i][1] * L[i][1] + L[i][2] * L[i][2]);
        if (len > 0.0001f) {
            L[i][0] /= len;
            L[i][1] /= len;
            L[i][2] /= len;
        }
    }
    
    // Compute inverse matrix (simplified 3x3 inversion)
    float det = L[0][0] * (L[1][1] * L[2][2] - L[2][1] * L[1][2]) -
                L[0][1] * (L[1][0] * L[2][2] - L[2][0] * L[1][2]) +
                L[0][2] * (L[1][0] * L[2][1] - L[2][0] * L[1][1]);
    
    if (std::abs(det) < 0.0001f) {
        return; // Singular matrix
    }
    
    float invL[3][3];
    invL[0][0] = (L[1][1] * L[2][2] - L[2][1] * L[1][2]) / det;
    invL[0][1] = -(L[0][1] * L[2][2] - L[2][1] * L[0][2]) / det;
    invL[0][2] = (L[0][1] * L[1][2] - L[1][1] * L[0][2]) / det;
    invL[1][0] = -(L[1][0] * L[2][2] - L[2][0] * L[1][2]) / det;
    invL[1][1] = (L[0][0] * L[2][2] - L[2][0] * L[0][2]) / det;
    invL[1][2] = -(L[0][0] * L[1][2] - L[1][0] * L[0][2]) / det;
    invL[2][0] = (L[1][0] * L[2][1] - L[2][0] * L[1][1]) / det;
    invL[2][1] = -(L[0][0] * L[2][1] - L[2][0] * L[0][1]) / det;
    invL[2][2] = (L[0][0] * L[1][1] - L[1][0] * L[0][1]) / det;
    
    // Compute gains: g = invL * p
    float g1 = invL[0][0] * x + invL[0][1] * y + invL[0][2] * z;
    float g2 = invL[1][0] * x + invL[1][1] * y + invL[1][2] * z;
    float g3 = invL[2][0] * x + invL[2][1] * y + invL[2][2] * z;
    
    // Ensure non-negative gains
    g1 = std::max(0.0f, g1);
    g2 = std::max(0.0f, g2);
    g3 = std::max(0.0f, g3);
    
    // Normalize for power preservation
    float sum = g1 + g2 + g3;
    if (sum > 0.0001f) {
        float norm = std::sqrt(g1 * g1 + g2 * g2 + g3 * g3);
        g1 /= norm;
        g2 /= norm;
        g3 /= norm;
    }
    
    // Store gains
    rawGains_[tri[0]] = g1;
    rawGains_[tri[1]] = g2;
    rawGains_[tri[2]] = g3;
}

void VBAPRenderer::UpdateSmoothing(float dt) {
    float alpha = dt / (smoothingTime_ + dt);
    
    for (size_t i = 0; i < smoothedGains_.size(); ++i) {
        smoothedGains_[i] = alpha * rawGains_[i] + (1.0f - alpha) * smoothedGains_[i];
    }
}

void VBAPRenderer::Normalize(float& x, float& y, float& z) {
    float len = std::sqrt(x * x + y * y + z * z);
    if (len > 0.0001f) {
        x /= len;
        y /= len;
        z /= len;
    }
}

float VBAPRenderer::Dot(float x1, float y1, float z1, float x2, float y2, float z2) {
    return x1 * x2 + y1 * y2 + z1 * z2;
}
