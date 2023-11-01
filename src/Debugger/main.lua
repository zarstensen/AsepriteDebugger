---
--- Entry point for debugger extension.
---

-- global aseprite debugger namespace.
ASEDEB = {}

ASEDEB.ext_path = app.fs.joinPath(app.fs.userConfigPath, "extensions/!AsepriteDebugger")

ASEDEB.json = dofile(app.fs.joinPath(ASEDEB.ext_path, 'json/json.lua'))

-- configuration file should store the debug adapter endpoint, the debugger log file, and optionally test mode and test script.
-- can be accessed through all scripts with the ASEDEB_CONFIG global.
local config_file = io.open(app.fs.joinPath(ASEDEB.ext_path, 'config.json'), "r")
ASEDEB.config = ASEDEB.json.decode(config_file:read("a"))
config_file:close()

if ASEDEB.config.log_file then
    -- overload print function, as it otherwise prints to aseprites built in console.
    io.output(ASEDEB.config.log_file)

    function print(...)
        
        local args = {...}

        for i=1,select("#", ...) do
            io.write(tostring(args[i]), "\t")
        end

        io.write('\n')
        io.flush()

    end
end

-- load packagea here, to make sure their source path matches up with the path used when giving script permissions.
-- order is important here as some files depend on each other.
ASEDEB.Protocol = dofile(app.fs.joinPath(ASEDEB.ext_path, 'Protocol.lua'))
ASEDEB.PipeWebSocket = dofile(app.fs.joinPath(ASEDEB.ext_path, 'PipeWebSocket.lua'))
ASEDEB.Debugger = dofile(app.fs.joinPath(ASEDEB.ext_path, 'Debugger.lua'))

if not ASEDEB.config.test_mode then
    ASEDEB.Debugger.init(ASEDEB.config.endpoint)
else
    dofile('tests/test_main.lua')
end
