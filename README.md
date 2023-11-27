<div align="center">
  <h1 align="center">Aseprite Debugger</h1>
  <img src=assets/AsepriteDebuggerIcon.png alt="Icon" width="200" height="200"/>
</div>

[![build](https://img.shields.io/github/actions/workflow/status/zarstensen/AsepriteDebugger/tests.yml?label=tests
)](https://github.com/zarstensen/AsepriteDebugger/actions/workflows/tests.yml)

This project is a lua debugger for aseprite, which allows a client using the [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/) to debug an Aseprite script or extension.

- [Implementing a Debug Adapter](#implementing-a-debug-adapter)
  - [Debugger Installation](#debugger-installation)
    - [Lua Source](#lua-source)
    - [LuaWebSocket](#luawebsocket)
    - [Config file](#config-file)
  - [Souce Installation](#souce-installation)
  - [Debugger Communication](#debugger-communication)
  - [Example](#example)
- [Building LuaWebSocket](#building-luawebsocket)
- [Built With](#built-with)
- [Links](#links)
- [License](#license)

## Implementing a Debug Adapter

Implementing a Debug Adapter for the Aseprite Debugger is simple, as the debugger was intentionally designed to use the Debug Adapter Protocol for communication, so the primary job of the Debug Adapter is simply piping requests, responses and events from the client to the debugger and vice versa.

### Debugger Installation

The Debug Adapter still needs to install the debugger in Aseprite as an extension. To do this, various files needs to be copied to a subfolder in the extensions folder at the [aseprite user configuration directory](https://www.aseprite.org/api/app_fs#appfsuserconfigpath).

Below is a list of these various files, and how to install them.

#### Lua Source

All of the contents of the 'src/Debugger' folder should be copied over to the subfolder.

#### LuaWebSocket

A built version of 'LuaWebSocket' should also be copied over to the same subfolder. This binary must match the bitness of the Aseprite Application.
LuaWebSocket can be built using cmake and the [CMakeLists.txt](CMakeLists.txt) at the root of the repository.

#### Config file

Finally, a JSON file, named 'config.json' also needs to be copied over to the subfolder. The JSON file should contain the following fields.

```json
{
    "endpoint": "...",
    "install_dir": "...",
    "source_dir": "...",
}
```

| Config Name | Type   | Comment                                                                                |
| ----------- | ------ | -------------------------------------------------------------------------------------- |
| endpoint    | string | websocket endpoint the debugger will attempt to connect to on startup.                 |
| install_dir | string | location of installed debuggable source code. (ex. USER_CONFIG_DIR/scripts/script.lua) |
| source_dir  | string | location of debuggable source code. (ex. VSCODE_WORKSPACE/script.lua)                  |

When the Debug Adapter receives a terminated event, this subfolder needs to be uninstalled, as Aseprite cannot run properly otherwise.

### Souce Installation

The debuggable source code should also be installed in aseprite before it is launched. The location does not matter, as long as it is a valid aseprite script or extension folder, the important thing in this step is making sure the folder the source code is installed to, is the same as specified in 'config.json'.

### Debugger Communication

The debugger will attempt to connect to a websocket server which listents for connections on the endpoint specified in the 'config.json' file.
The messagese sent and received are all Debug Adapter Protocol messages, so these should simply be forwarded to the client.

The one exception is the StackTraceUpdate event, which is sent everytime the stacktrace is updated by the debugger.
This event represents either a push, pop or update event on a list, which contains a stacktrace of the debug session.
See [StackTraceHandler.lua](src/Debugger/StackTraceHandler.lua), for how to implement handling of event.

The event is primarily used for cases where communications with the debugger has been lost, as this allows the debug adapter to keep its own stacktrace, which can be used as a placeholder for the actual stacktrace in a StackTraceRequest.

### Example

See the [Aseprite Debugger for Visual Studio Code](https://github.com/zarstensen/AsepriteDebuggerVsc) extension, which implements a debug adapter for the Aseprite Debugger.

## Building LuaWebSocket

Requirements

- Any C++ 20 compliant compiler (AsepriteDebuggerVSC uses Visual Studio 2022 and MSVC)
- CMake

See the build-luaws-x64 and build-luaws-win32 tasks in the [package.json](https://github.com/zarstensen/AsepriteDebuggerVsc/blob/main/package.json) file in AsepriteDebuggerVSC for how to build.

## Built With

- [asio](https://think-async.com/Asio/)
- [Catch2](https://github.com/catchorg/Catch2)
- [Lua](http://www.lua.org/)
- [websocketpp](https://www.zaphoyd.com/projects/websocketpp/)

## Links

[VSCode Extension](https://marketplace.visualstudio.com/items?itemName=zarstensen.aseprite-debugger)

## License

See [LICENSE](LICENSE)
