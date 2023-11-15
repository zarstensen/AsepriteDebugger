local JsonWS = require 'JsonWS'

local ws = LuaWebSocket()

ws:connect(ASEDEB.config.endpoint)

JsonWS.sendJson(ws, { type = "test_message" })

ws:close()
ASEDEB.stopTest()
