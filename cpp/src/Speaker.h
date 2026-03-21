#pragma once

struct Speaker {
    int id;
    float x, y, z;
    
    Speaker() : id(0), x(0.0f), y(0.0f), z(0.0f) {}
    Speaker(int id, float x, float y, float z) : id(id), x(x), y(y), z(z) {}
};
