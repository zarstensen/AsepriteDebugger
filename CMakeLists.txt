cmake_minimum_required(VERSION 3.27)

project(LuaWebSocket 
    VERSION 1.0
    DESCRIPTION "Lua requirable DLL, which wraps websocketpp in a simple to use websocket class."
    LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_SCAN_FOR_MODULES ON)
list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_SOURCE_DIR}/modules/Catch2/extras)
message(${CMAKE_MODULE_PATH})

# asio librart

add_library(asio INTERFACE)
target_include_directories(asio INTERFACE modules/asio/asio/include)

# websocketpp library
    
add_library(websocketpp INTERFACE)
target_include_directories(websocketpp INTERFACE modules/websocketpp)
target_compile_definitions(websocketpp INTERFACE _WEBSOCKET_CPP11_STL_ _WEBSOCKETPP_CPP11_INTERNAL_ ASIO_STANDALONE)

target_link_libraries(websocketpp INTERFACE asio)

# lua library (see: https://www.lua.org/manual/5.4/readme.html for build instructions)

add_library(lua STATIC 
    modules/lua/lapi.c
    modules/lua/lcode.c
    modules/lua/lctype.c
    modules/lua/ldebug.c
    modules/lua/ldo.c
    modules/lua/ldump.c
    modules/lua/lfunc.c
    modules/lua/lgc.c
    modules/lua/llex.c
    modules/lua/lmem.c
    modules/lua/lobject.c
    modules/lua/lopcodes.c
    modules/lua/lparser.c
    modules/lua/lstate.c
    modules/lua/lstring.c
    modules/lua/ltable.c
    modules/lua/ltm.c
    modules/lua/lundump.c
    modules/lua/lvm.c
    modules/lua/lzio.c
    modules/lua/lauxlib.c
    modules/lua/lbaselib.c
    modules/lua/lcorolib.c
    modules/lua/ldblib.c
    modules/lua/liolib.c
    modules/lua/lmathlib.c
    modules/lua/loadlib.c
    modules/lua/loslib.c
    modules/lua/lstrlib.c
    modules/lua/ltablib.c
    modules/lua/lutf8lib.c
    modules/lua/linit.c 
)

target_include_directories(lua PUBLIC modules/lua)

# LuaWebSocket library

add_library(${PROJECT_NAME} SHARED
    src/LuaWebSocket/LuaWebSocket.cpp
    src/LuaWebSocket/WebSocket.cpp
    src/LuaWebSocket/WebSocket.h
    src/LuaWebSocket/LUAWS.h
)
target_include_directories(${PROJECT_NAME} PUBLIC src/LuaWebSocket)
target_link_libraries(${PROJECT_NAME} PUBLIC lua websocketpp)
target_compile_definitions(${PROJECT_NAME} PRIVATE LUAWS_EXPORTS)

# LuaWebSocket tests


if(${CMAKE_PROJECT_NAME} STREQUAL ${PROJECT_NAME})
    include(CTest)

    if(${BUILD_TESTING})

        add_subdirectory(modules/Catch2)

        include(Catch REQUIRED)
        
        add_executable(LuaWebSocketTests
        tests/LuaWebSocketTests/test.cpp
        )

        target_link_libraries(LuaWebSocketTests PRIVATE LuaWebSocket Catch2::Catch2WithMain)
        
        catch_discover_tests(LuaWebSocketTests)
    endif()
endif()
