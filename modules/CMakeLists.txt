
# asio library

add_library(asio INTERFACE)
target_include_directories(asio INTERFACE asio/asio/include)

add_library(asio::asio ALIAS asio)

# websocketpp library
    
add_library(websocketpp INTERFACE)
target_include_directories(websocketpp INTERFACE websocketpp)
target_compile_definitions(websocketpp INTERFACE _WEBSOCKET_CPP11_STL_ _WEBSOCKETPP_CPP11_INTERNAL_ ASIO_STANDALONE)

target_link_libraries(websocketpp INTERFACE asio::asio)

add_library(websocketpp::websocketpp ALIAS websocketpp)

# lua library (see: https://www.lua.org/manual/5.4/readme.html for build instructions)

add_library(lua STATIC 
    lua/lapi.c
    lua/lcode.c
    lua/lctype.c
    lua/ldebug.c
    lua/ldo.c
    lua/ldump.c
    lua/lfunc.c
    lua/lgc.c
    lua/llex.c
    lua/lmem.c
    lua/lobject.c
    lua/lopcodes.c
    lua/lparser.c
    lua/lstate.c
    lua/lstring.c
    lua/ltable.c
    lua/ltm.c
    lua/lundump.c
    lua/lvm.c
    lua/lzio.c
    lua/lauxlib.c
    lua/lbaselib.c
    lua/lcorolib.c
    lua/ldblib.c
    lua/liolib.c
    lua/lmathlib.c
    lua/loadlib.c
    lua/loslib.c
    lua/lstrlib.c
    lua/ltablib.c
    lua/lutf8lib.c
    lua/linit.c 
)

target_include_directories(lua PUBLIC lua)

add_library(lua::lua ALIAS lua)

# Cathc2 library

if(${CMAKE_PROJECT_NAME} STREQUAL ${PROJECT_NAME})
    include(CTest)

    if(${BUILD_TESTING})
        list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_SOURCE_DIR}/Catch2/extras)
        include(Catch REQUIRED)

        add_subdirectory(Catch2)
    endif()
endif()
