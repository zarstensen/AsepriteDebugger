--- 
--- Entry point for debugger extension in test configuration.
--- 

local json = ASEDEB.json

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

    ASEDEB.test_ws:sendText(json.encode({ type = "assert", message = trace_message }))
end

--- Signal to the test runner that all test checks have finished and aseprite can be terminated.
--- If this is not called at some point, the test is considered a failure.
function ASEDEB.stopTest()
    ASEDEB.test_ws:sendText(json.encode({ type = "test_end" }))
end

local function testWsOnReceive(message_type)
    if message_type == WebSocketMessageType.OPEN then
        -- we want to run the tests when we can communicate any failures to the test runner,
        -- so we wait for an open message for the test websocket to ensure this.
        local status, msg = xpcall(dofile, debug.traceback, ASEDEB.config.test_script)
        ASEDEB.testAssert(status, msg)
    end
end

-- use this websocket to communicate failures and test ends.
ASEDEB.test_ws = WebSocket{
    url = ASEDEB.config.test_endpoint,
    deflate = false,
    onreceive = testWsOnReceive
}

ASEDEB.test_ws:connect()
