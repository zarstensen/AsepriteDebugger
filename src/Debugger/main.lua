---
--- Entry point for debugger extension.
---

-- json is local here, as we do not want to pollute tests with this package, in case we are in a test configuration.
local json = dofile('json/json.lua')

-- configuration file should store the debug adapter endpoint, the debugger log file, and optionally test mode and test script.
-- can be accessed through all scripts with the ASEDEB_CONFIG global.
local config_file = io.open(app.fs.joinPath(app.fs.userConfigPath, "extensions/!AsepriteDebugger/config.json"), "r")
ASEDEB_CONFIG = json.decode(config_file:read("a"))
config_file:close()

if ASEDEB_CONFIG.log_file then
    -- overload print function, as it otherwise prints to aseprites built in console.
    io.output(ASEDEB_CONFIG.log_file)

    function print(...)

        for _, arg in pairs({...}) do
            io.write(tostring(arg), "\t")
        end

        io.write('\n')
        io.flush()

    end
end

if not ASEDEB_CONFIG.test_mode then
    Debugger = dofile('Debugger.lua')

    Debugger.init(ASEDEB_CONFIG.endpoint)
else
    -- we want to run a different entry point if we are in test mode.
    dofile('tests/test_main.lua')
end
