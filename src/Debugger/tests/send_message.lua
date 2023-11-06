require '../LuaWebSocket'
require 'JsonWS'

print("OMG")

local ws = LuaWebSocket()
ws:connect(ASEDEB.config.endpoint)

local msg = JsonWS.receiveJson(ws)

ASEDEB.testAssert(
    msg.type == "test_message",
    "Received invalid message."
)

ws:close()
ASEDEB.stopTest()
