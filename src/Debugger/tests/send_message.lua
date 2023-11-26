local JsonWS = require 'JsonWS'

local ws = LuaWebSocket()
ws:connect(ASEDEB.config.endpoint)

local msg = JsonWS.receiveJson(ws)

ASEDEB.testAssert(
    msg.type == "test_message",
    "Received invalid message."
)

ASEDEB.stopTest()

ASEDEB.waitForServerStop()

ws:close()
