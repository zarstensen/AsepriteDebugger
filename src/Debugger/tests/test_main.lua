--- 
--- Entry point for debugger extension in test configuration.
--- 

local JsonWS = require 'JsonWS'

--- Checks that the given condition is true, 
--- and sends an assert json object back to the mock debug adapter with the passed message if not.
---
---@param condition any
---@param message any
function ASEDEB.testAssert(condition, message)
    if condition then
        return;
    end

    -- generate stack trace for error message

    local _, trace_message = xpcall(function() error(message) end, debug.traceback)

    print(trace_message)

    JsonWS.sendJson(ASEDEB.test_pipe_ws, { type = "assert", message = trace_message })
end

function ASEDEB.waitForServerStop()
    while true do
        local recv = JsonWS.receiveJson(ASEDEB.test_pipe_ws)

        if recv == nil or recv.type == "test_end" then
            break
        end

    end
end

--- Signal to the test runner that all test checks have finished and aseprite can be terminated.
--- If this is not called at some point, the test is considered a failure.
function ASEDEB.stopTest()
    JsonWS.sendJson(ASEDEB.test_pipe_ws, { type = "test_end" })
end


-- use this pipe websocket to communicate failures and test ends.
print("CON")
ASEDEB.test_pipe_ws = LuaWebSocket()
print("CONNECT")
ASEDEB.test_pipe_ws:connect(ASEDEB.config.test_endpoint)
print("ENDCON")

local res, msg = xpcall(function() dofile(ASEDEB.config.test_script) end, debug.traceback)

ASEDEB.testAssert(res, msg)
