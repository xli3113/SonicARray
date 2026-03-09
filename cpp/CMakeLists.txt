cmake_minimum_required(VERSION 3.10)
project(SoundARray)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Output backend: JACK (Linux default) or PortAudio
if(UNIX AND NOT APPLE)
  set(USE_JACK_DEFAULT ON)
else()
  set(USE_JACK_DEFAULT OFF)
endif()
option(USE_JACK "Use JACK output instead of PortAudio" ${USE_JACK_DEFAULT})

# Find packages (PkgConfig for JACK)
if(USE_JACK)
    find_package(PkgConfig REQUIRED)
endif()

# OSC Pack sources (required)
file(GLOB OSCPACK_OSC_SOURCES third_party/oscpack/osc/*.cpp)
set(OSCPACK_IP_SOURCES
    third_party/oscpack/ip/IpEndpointName.cpp
)
if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/oscpack/ip/UdpSocket.cpp")
    list(APPEND OSCPACK_IP_SOURCES third_party/oscpack/ip/UdpSocket.cpp)
endif()
if(WIN32)
    file(GLOB OSCPACK_IP_PLATFORM third_party/oscpack/ip/win32/*.cpp)
else()
    file(GLOB OSCPACK_IP_PLATFORM third_party/oscpack/ip/posix/*.cpp)
endif()
set(OSCPACK_SOURCES ${OSCPACK_OSC_SOURCES} ${OSCPACK_IP_SOURCES} ${OSCPACK_IP_PLATFORM})
if(NOT OSCPACK_SOURCES)
    message(FATAL_ERROR "oscpack not found. Run: git clone https://github.com/RossBencina/oscpack.git third_party/oscpack")
endif()

# JACK (USE_JACK=ON, Linux default)
if(USE_JACK)
    pkg_check_modules(JACK REQUIRED jack)
    add_definitions(-DUSE_JACK)
    set(SOURCES_EXTRA src/JackAudioOutput.cpp)
# PortAudio (USE_JACK=OFF)
else()
    set(PORTAUDIO_SUBPROJECT OFF)
    if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/CMakeLists.txt")
        set(PORTAUDIO_SUBPROJECT ON)
        set(PA_BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
        set(PA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
        set(PA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
        add_subdirectory(third_party/portaudio)
    endif()
    set(SOURCES_EXTRA src/PortAudioOutput.cpp)
endif()

# Source files
set(SOURCES
    src/main.cpp
    src/ConfigLoader.cpp
    src/VBAPRenderer.cpp
    src/OSCReceiver.cpp
    src/AudioEngine.cpp
    src/RendererFactory.cpp
    ${SOURCES_EXTRA}
)

# Add executable
add_executable(SoundARray ${SOURCES} ${OSCPACK_SOURCES})

target_include_directories(SoundARray PRIVATE
    ${CMAKE_SOURCE_DIR}/src
    ${CMAKE_SOURCE_DIR}/third_party/oscpack
)

# Link audio backend
if(USE_JACK)
    target_include_directories(SoundARray PRIVATE ${JACK_INCLUDE_DIRS})
    target_link_libraries(SoundARray PRIVATE ${JACK_LIBRARIES})
    target_compile_options(SoundARray PRIVATE ${JACK_CFLAGS_OTHER})
else()
    if(NOT PORTAUDIO_SUBPROJECT)
        find_package(portaudio CONFIG QUIET)
    endif()
    if(portaudio_FOUND)
        target_link_libraries(SoundARray PRIVATE portaudio::portaudio)
    elseif(PORTAUDIO_SUBPROJECT)
        target_link_libraries(SoundARray PRIVATE portaudio)
    else()
        target_include_directories(SoundARray PRIVATE ${CMAKE_SOURCE_DIR}/third_party/portaudio/include)
        if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a")
            target_link_libraries(SoundARray PRIVATE
                ${CMAKE_SOURCE_DIR}/third_party/portaudio/lib/.libs/libportaudio.a
            )
        elseif(WIN32)
            if(EXISTS "${CMAKE_SOURCE_DIR}/third_party/portaudio/build/Release/portaudio.lib")
                target_link_libraries(SoundARray PRIVATE
                    ${CMAKE_SOURCE_DIR}/third_party/portaudio/build/Release/portaudio.lib
                )
            else()
                message(FATAL_ERROR "PortAudio not found. Use: add_subdirectory(third_party/portaudio), or vcpkg install portaudio")
            endif()
        else()
            find_library(PORTAUDIO_LIBRARY portaudio)
            if(PORTAUDIO_LIBRARY)
                target_link_libraries(SoundARray PRIVATE ${PORTAUDIO_LIBRARY})
            else()
                message(FATAL_ERROR "PortAudio not found. Use -DUSE_JACK=ON (Linux) or clone third_party/portaudio")
            endif()
        endif()
    endif()
endif()

# Platform-specific settings
if(WIN32)
    target_link_libraries(SoundARray PRIVATE
        winmm
        ws2_32
        ole32
    )
elseif(APPLE)
    find_library(COREAUDIO_LIBRARY CoreAudio)
    find_library(COREFOUNDATION_LIBRARY CoreFoundation)
    target_link_libraries(SoundARray PRIVATE ${COREAUDIO_LIBRARY} ${COREFOUNDATION_LIBRARY})
elseif(UNIX)
    target_link_libraries(SoundARray PRIVATE asound pthread)
endif()

# Copy speakers.yaml to executable directory (Debug/Release)
set(SPEAKER_YAML "")
if(EXISTS "${CMAKE_SOURCE_DIR}/../speakers.yaml")
    set(SPEAKER_YAML "${CMAKE_SOURCE_DIR}/../speakers.yaml")
elseif(EXISTS "${CMAKE_SOURCE_DIR}/speakers.yaml")
    set(SPEAKER_YAML "${CMAKE_SOURCE_DIR}/speakers.yaml")
endif()
if(SPEAKER_YAML)
    add_custom_command(TARGET SoundARray POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
                "${SPEAKER_YAML}"
                "$<TARGET_FILE_DIR:SoundARray>/speakers.yaml"
        COMMENT "Copy speakers.yaml to output dir"
    )
endif()




if(EXISTS "${CMAKE_SOURCE_DIR}/src/offline_tests.cpp")
    add_executable(offline_tests src/offline_tests.cpp src/VBAPRenderer.cpp)
    target_include_directories(offline_tests PRIVATE ${CMAKE_SOURCE_DIR}/src)
endif()
if(EXISTS "${CMAKE_SOURCE_DIR}/src/offline_tests_vbap_diagnose.cpp")
    add_executable(offline_tests_vbap_diagnose src/offline_tests_vbap_diagnose.cpp src/VBAPRenderer.cpp)
    target_include_directories(offline_tests_vbap_diagnose PRIVATE ${CMAKE_SOURCE_DIR}/src)
endif()
if(EXISTS "${CMAKE_SOURCE_DIR}/src/offline_tests_smoothing_step.cpp")
    add_executable(offline_tests_smoothing_step src/offline_tests_smoothing_step.cpp src/VBAPRenderer.cpp)
    target_include_directories(offline_tests_smoothing_step PRIVATE ${CMAKE_SOURCE_DIR}/src)
endif()
if(EXISTS "${CMAKE_SOURCE_DIR}/src/offline_tests_simd_fuzz.cpp")
    add_executable(offline_tests_simd_fuzz src/offline_tests_simd_fuzz.cpp)
    target_include_directories(offline_tests_simd_fuzz PRIVATE ${CMAKE_SOURCE_DIR}/src)
endif()
if(EXISTS "${CMAKE_SOURCE_DIR}/src/offline_tests_real_layout.cpp")
    add_executable(offline_tests_real_layout src/offline_tests_real_layout.cpp src/VBAPRenderer.cpp)
    target_include_directories(offline_tests_real_layout PRIVATE ${CMAKE_SOURCE_DIR}/src)
endif()
