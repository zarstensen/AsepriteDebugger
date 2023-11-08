cmake_minimum_required(VERSION 3.25)

project(LuaWebSocket 
    VERSION 1.0
    DESCRIPTION "Lua requirable DLL, which wraps websocketpp in a simple to use websocket class."
    LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_SHARED_LIBRARY_PREFIX "")

add_subdirectory(modules)

# LuaWebSocket library

add_library(${PROJECT_NAME} SHARED
    src/LuaWebSocket/LuaWebSocket.cpp
    src/LuaWebSocket/WebSocket.cpp
    src/LuaWebSocket/WebSocket.h
    src/LuaWebSocket/LUAWS.h
)
target_include_directories(${PROJECT_NAME} PUBLIC src/LuaWebSocket)
target_link_libraries(${PROJECT_NAME} PUBLIC lua::lua websocketpp::websocketpp)
target_compile_definitions(${PROJECT_NAME} PRIVATE LUAWS_EXPORTS)

install(TARGETS ${PROJECT_NAME} DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/install
    RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/install/bin)

# LuaWebSocket tests

if(${BUILD_TESTING})
    include(CTest)

    find_package(OpenSSL REQUIRED)

    add_executable(LuaWebSocketTests
        tests/LuaWebSocketTests/test.cpp
    )
    
    target_link_libraries(LuaWebSocketTests PRIVATE OpenSSL::SSL LuaWebSocket Catch2::Catch2WithMain)
    
    catch_discover_tests(LuaWebSocketTests)
endif()
