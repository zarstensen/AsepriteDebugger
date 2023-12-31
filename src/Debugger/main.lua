---
--- Entry point for debugger extension.
---


-- global aseprite debugger namespace.
ASEDEB = {}
ASEDEB.json = require 'json.json'

ASEDEB.ext_path = app.fs.joinPath(app.fs.userConfigPath, "extensions/!AsepriteDebugger")

table.insert(package.searchers, function(module)

    local new_module = module:match(".+/(.+)")

    if not new_module then
        return nil
    end

    for _, searcher in ipairs(package.searchers) do
        local res = searcher(new_module)

        if type(res) == 'function' then
            return res
        end
    end

    return nil
end)


-- package.cpath seems to be preserved between aseprite runs, so we want to make sure to remove the custom search path.
-- this also prevents this workaround from affecting other scripts and extensions.
local tmp_cpath = package.cpath

-- use the loadall library extension as the shared library extension for the current platform.
local shared_lib_ext = package.cpath:match(".+loadall%p(%a+)")

package.cpath = tmp_cpath .. ";" .. ASEDEB.ext_path .. "/?." .. shared_lib_ext

require 'LuaWebSocket'

package.cpath = tmp_cpath

-- configuration file should store the debug adapter endpoint, the debugger log file, and optionally test mode and test script.
-- can be accessed through all scripts with the ASEDEB_CONFIG global.
local config_file = io.open(app.fs.joinPath(ASEDEB.ext_path, 'config.json'), "r")
ASEDEB.config = ASEDEB.json.decode(config_file:read("a"))
config_file:close()

if ASEDEB.config.log_file then
    io.output(ASEDEB.config.log_file)
end

 -- overload print function, as it otherwise prints to aseprites built in console.
function print(...)
    local args = {...}

    for i=1,select("#", ...) do

        if ASEDEB.config.log_file then
            io.write(tostring(args[i]), "\t")
        end

        if(ASEDEB.Debugger and ASEDEB.Debugger.connected() and not ASEDEB.config.no_websocket_logging) then
            ASEDEB.Debugger.log(string.format("%s\t", tostring(args[i])))
        end
    end

    if(ASEDEB.Debugger and ASEDEB.Debugger.connected() and not ASEDEB.config.no_websocket_logging) then
        ASEDEB.Debugger.log('\n')
    end

    if ASEDEB.config.log_file then
        io.write('\n')
        io.flush()
    end
end

ASEDEB.Debugger = require 'Debugger'

if not ASEDEB.config.test_mode then
    ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
    ASEDEB.Debugger.init()
else
    dofile('tests/test_main.lua')
end

function exit(plugin)
    ASEDEB.Debugger.deinit()
end
