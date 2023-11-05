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

-- determine shared library extension by examining existing values in the cpath and using their extensions.
local shared_lib_ext = package.cpath:match("%p[\\|/]?%p(%a+)")

package.cpath = package.cpath .. ";" .. ASEDEB.ext_path .. "/?." .. shared_lib_ext

print("before require")
require 'LuaWebSocket'
print("after require")

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

if not ASEDEB.config.test_mode then
    require 'Debugger'
    Debugger.init(ASEDEB.config.endpoint)
else
    dofile('tests/test_main.lua')
end
