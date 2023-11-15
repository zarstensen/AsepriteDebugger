

ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

-- commented line

local code_line_1

function function_1()
    local code_line_2
end

--[[ multi line comment
non code line
]]

local code_line_3

function_1()

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.deinit()

--
-- final comment line
--
