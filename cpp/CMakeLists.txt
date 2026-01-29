cmake_minimum_required(VERSION 3.10)
project(SoundARray)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find packages (PkgConfig used on Linux/macOS for portaudio)
if(NOT WIN32)
  find_package(PkgConfig)
endif()

# Include directories (oscpack and our src; PortAudio handled below)
include_directories(${CMAKE_SOURCE_DIR}/src)
include_directories(${CMAKE_SOURCE_DIR}/third_party/oscpack)

# OSC Pack sources (required)
file(GLOB OSCPACK_SOURCES
    third_party/oscpack/osc/*.cpp
    third_party/oscpack/ip/*.cpp
)
if(NOT OSCPACK_SOURCES)
    message(FATAL_ERROR "oscpack not found. Run: git clone https://github.com/RossBencina/oscpack.git third_party/oscpack")
endif()

# PortAudio: prefer building as subproject (works on Windows/Linux/macOS)
set(PORTAUDIO_SUBPROJECT OFF)
if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/CMakeLists.txt")
    set(PORTAUDIO_SUBPROJECT ON)
    set(PA_BUILD_SHARED_LIBS OFF CACHE BOOL "PortAudio static" FORCE)
    set(PA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(PA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    add_subdirectory(third_party/portaudio)
endif()

# Source files
set(SOURCES
    src/main.cpp
    src/ConfigLoader.cpp
    src/VBAPRenderer.cpp
    src/OSCReceiver.cpp
    src/AudioEngine.cpp
    src/RendererFactory.cpp
)

# Add executable
add_executable(SoundARray ${SOURCES} ${OSCPACK_SOURCES})

# Link PortAudio
if(PORTAUDIO_SUBPROJECT)
    target_link_libraries(SoundARray portaudio)
else()
    include_directories(${CMAKE_SOURCE_DIR}/third_party/portaudio/include)
    if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a")
        target_link_libraries(SoundARray
            ${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a
        )
    elseif(WIN32)
        # Windows: prebuilt or custom path (e.g. build/Release/portaudio.lib)
        if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/build/Release/portaudio.lib")
            target_link_libraries(SoundARray
                ${CMAKE_SOURCE_DIR}/third_party/portaudio/build/Release/portaudio.lib
            )
        else()
            message(FATAL_ERROR "PortAudio not found for Windows. Clone portaudio into third_party/portaudio and use add_subdirectory, or build and put portaudio.lib in third_party/portaudio/build/Release/")
        endif()
    else()
        find_library(PORTAUDIO_LIBRARY portaudio)
        if(PORTAUDIO_LIBRARY)
            target_link_libraries(SoundARray ${PORTAUDIO_LIBRARY})
        else()
            message(FATAL_ERROR "PortAudio not found. Clone into third_party/portaudio: git clone https://github.com/PortAudio/portaudio.git third_party/portaudio")
        endif()
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

# Copy speakers.yaml to build directory (project root or cpp/)
if(EXISTS "${CMAKE_SOURCE_DIR}/../speakers.yaml")
  configure_file(${CMAKE_SOURCE_DIR}/../speakers.yaml ${CMAKE_BINARY_DIR}/speakers.yaml COPYONLY)
elseif(EXISTS "${CMAKE_SOURCE_DIR}/speakers.yaml")
  configure_file(${CMAKE_SOURCE_DIR}/speakers.yaml ${CMAKE_BINARY_DIR}/speakers.yaml COPYONLY)
endif()
