cmake_minimum_required(VERSION 3.16)
project(netkeyer_midi_shim C CXX)

include(FetchContent)

# readerwriterqueue is required by libremidi's PipeWire backend.
# Fetch it first and expose its root as a global include directory so that
# libremidi's sources can find readerwriterqueue.h when they are compiled.
FetchContent_Declare(readerwriterqueue
    GIT_REPOSITORY https://github.com/cameron314/readerwriterqueue.git
    GIT_TAG        v1.0.6
    GIT_SHALLOW    TRUE)
FetchContent_MakeAvailable(readerwriterqueue)
include_directories(${readerwriterqueue_SOURCE_DIR})

FetchContent_Declare(libremidi
    GIT_REPOSITORY https://github.com/celtera/libremidi.git
    GIT_TAG        v5.4.3
    GIT_SHALLOW    TRUE)

set(BUILD_SHARED_LIBS OFF)   # build libremidi as static
set(LIBREMIDI_EXAMPLES OFF)
set(LIBREMIDI_TESTS OFF)
set(LIBREMIDI_NO_BOOST ON)
FetchContent_MakeAvailable(libremidi)

add_library(netkeyer_midi_shim SHARED netkeyer_midi_shim.c)
target_link_libraries(netkeyer_midi_shim PRIVATE libremidi)
target_compile_definitions(netkeyer_midi_shim PRIVATE NKM_EXPORTS)
set_target_properties(netkeyer_midi_shim PROPERTIES
    C_STANDARD 11
    POSITION_INDEPENDENT_CODE ON)

# Platform-specific link flags to hide non-exported symbols
if(UNIX AND NOT APPLE)
    target_link_options(netkeyer_midi_shim PRIVATE
        -Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/exports.map)
endif()
