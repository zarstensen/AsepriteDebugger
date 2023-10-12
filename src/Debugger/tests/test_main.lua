local json = require('json.json')

--- Checks that the given condition is true, 
--- and sends an assert json object back to the mock debug adapter with the passed message if not.
---
--- If the debug adapter is not accessible through the Debugger websocket (debugger has not been initialized or has closed),
--- it will instead be printed to console.
--- Therefore this should ideally be called after / during the Debugger.onConnect callback function.
---
---@param condition any
---@param message any
function testAssert(condition, message)
    if condition then
        return;
    end

    -- generate stack trace for error message

    local _, trace_message = xpcall(function() error(message) end, debug.traceback)

    if Debugger and Debugger.ws then
        Debugger.ws:sendText(json.encode({ type = "assert", message = trace_message }))
    else
        print(trace_message)
    end
end

-- wrap all test scripts in an xpcall, to capture any failures during test execution.
local status, msg = xpcall(dofile, debug.traceback, ASEDEB_CONFIG.test_script)

testAssert(status, msg)
