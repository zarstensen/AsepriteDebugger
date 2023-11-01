--- 
--- Entry point for debugger extension in test configuration.
--- 

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

    ASEDEB.test_pipe_ws:sendJson({ type = "assert", message = trace_message })
end

--- Signal to the test runner that all test checks have finished and aseprite can be terminated.
--- If this is not called at some point, the test is considered a failure.
function ASEDEB.stopTest()
    ASEDEB.test_pipe_ws:sendJson({ type = "test_end" })
end


-- use this pipe websocket to communicate failures and test ends.
ASEDEB.test_pipe_ws = PipeWebSocket.new(ASEDEB.config.pipe_ws_path)
ASEDEB.test_pipe_ws:connect("127.0.0.1", ASEDEB.config.test_endpoint)

dofile(ASEDEB.config.test_script)
