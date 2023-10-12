---
--- This file is used to generate the actual entry point for the debugger extension.
--- Any !<VAR> will be replaced with a valid lua expression once the entry point is generated.
--- This is done as it is not possible to pass arguments to extensions when starting aseprite,
--- so instead we generate source code, which contains relevant arguments.
---

print(debug.getinfo(1, "S").source)

local json = require 'json.json'

local config_file = io.open(app.fs.joinPath(app.fs.userConfigPath, "extensions/!AsepriteDebugger/config.json"), "r")
ASEDEB_CONFIG = json.decode(config_file:read("a"))
config_file:close()

-- overload print function, as it otherwise prints to aseprites built in console.
io.output(ASEDEB_CONFIG.log_file)

function print(...)

    for _, arg in pairs({...}) do
        io.write(tostring(arg), "\t")
    end

    io.write('\n')
    io.flush()

end

if ASEDEB_CONFIG.test_mode then
    dofile("tests/test_main.lua")
    return
end

require 'Debugger'

Debugger.init(ASEDEB_CONFIG.endpoint)
