cmake_minimum_required(VERSION 3.10)
project(SoundARray)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find packages
find_package(PkgConfig REQUIRED)

# Include directories
include_directories(${CMAKE_SOURCE_DIR}/src)
include_directories(${CMAKE_SOURCE_DIR}/third_party/oscpack)
include_directories(${CMAKE_SOURCE_DIR}/third_party/portaudio/include)

# Source files
set(SOURCES
    src/main.cpp
    src/ConfigLoader.cpp
    src/VBAPRenderer.cpp
    src/OSCReceiver.cpp
    src/AudioEngine.cpp
    src/RendererFactory.cpp
)

# OSC Pack sources (simplified - adjust paths as needed)
file(GLOB OSCPACK_SOURCES
    third_party/oscpack/osc/*.cpp
    third_party/oscpack/ip/*.cpp
)

# Add executable
add_executable(SoundARray ${SOURCES} ${OSCPACK_SOURCES})

# Link libraries
# PortAudio library path (adjust based on installation)
if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a")
    target_link_libraries(SoundARray
        ${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a
    )
else()
    # Try to find PortAudio via pkg-config (Linux/macOS)
    find_library(PORTAUDIO_LIBRARY portaudio)
    if(PORTAUDIO_LIBRARY)
        target_link_libraries(SoundARray ${PORTAUDIO_LIBRARY})
    else()
        message(WARNING "PortAudio library not found. Please install PortAudio or adjust library path.")
    endif()
endif()

# Platform-specific settings
if(WIN32)
    target_link_libraries(SoundARray
        winmm
        ws2_32
        ole32
    )
elseif(APPLE)
    find_library(COREAUDIO_LIBRARY CoreAudio)
    find_library(COREFOUNDATION_LIBRARY CoreFoundation)
    target_link_libraries(SoundARray ${COREAUDIO_LIBRARY} ${COREFOUNDATION_LIBRARY})
elseif(UNIX)
    target_link_libraries(SoundARray asound pthread)
endif()

# Copy speakers.yaml to build directory
configure_file(${CMAKE_SOURCE_DIR}/speakers.yaml ${CMAKE_BINARY_DIR}/speakers.yaml COPYONLY)
