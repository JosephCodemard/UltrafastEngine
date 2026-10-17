# C++ example for the UltrafastEngine C API (Visual Studio 2022 "Open Folder", or any CMake toolchain).
cmake_minimum_required(VERSION 3.16)
project(ultrafast_cpp_example CXX)
set(CMAKE_CXX_STANDARD 17)

# Folder containing the NativeAOT publish output (ultrafast.dll + ultrafast.lib on Windows,
# libultrafast.so / libultrafast.dylib elsewhere):
#   dotnet publish src/UltrafastEngine.Native -c Release -r win-x64 -o artifacts/native/win-x64
set(ULTRAFAST_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../artifacts/native/win-x64" CACHE PATH "UltrafastEngine native library folder")

add_executable(run_system run_system.cpp)
target_include_directories(run_system PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../../src/UltrafastEngine.Native/include")
if(WIN32)
  target_link_libraries(run_system PRIVATE "${ULTRAFAST_DIR}/ultrafast.lib")
  add_custom_command(TARGET run_system POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different "${ULTRAFAST_DIR}/ultrafast.dll" $<TARGET_FILE_DIR:run_system>)
else()
  find_library(ULTRAFAST_LIB NAMES ultrafast PATHS "${ULTRAFAST_DIR}" NO_DEFAULT_PATH REQUIRED)
  target_link_libraries(run_system PRIVATE "${ULTRAFAST_LIB}")
  set_target_properties(run_system PROPERTIES BUILD_RPATH "${ULTRAFAST_DIR}")
endif()
