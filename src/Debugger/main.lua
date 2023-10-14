---
--- Entry point for debugger extension.
---

-- global aseprite debugger namespace.
ASEDEB = {}

ASEDEB.json = dofile('json/json.lua')

-- configuration file should store the debug adapter endpoint, the debugger log file, and optionally test mode and test script.
-- can be accessed through all scripts with the ASEDEB_CONFIG global.
local config_file = io.open(app.fs.joinPath(app.fs.userConfigPath, "extensions/!AsepriteDebugger/config.json"), "r")
ASEDEB.config = ASEDEB.json.decode(config_file:read("a"))
config_file:close()

if ASEDEB.config.log_file then
    -- overload print function, as it otherwise prints to aseprites built in console.
    io.output(ASEDEB.config.log_file)

    function print(...)

        for _, arg in pairs({...}) do
            io.write(tostring(arg), "\t")
        end

        io.write('\n')
        io.flush()

    end
end

-- load debugger package here, to make sure its source path matches up with the path used when giving script permissions.
ASEDEB.Debugger = dofile('Debugger.lua')

if not ASEDEB.config.test_mode then
    ASEDEB.Debugger.init(ASEDEB.config.endpoint)
else
    dofile('tests/test_main.lua')
end
