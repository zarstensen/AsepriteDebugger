require 'json.json'

---@class JsonWS
local P = {}

--- Send the passed table as a json string.
---@param ws LuaWebSocket
---@param msg table
function P.sendJson(ws, msg)
    ws:send(json.encode(msg))
end

--- Retreive the next message and parse it into a lua table,
--- assuming the message is formatted as json.
---@param ws LuaWebSocket
---@return table | nil
function P.receiveJson(ws)
    local msg = ws:receive()

    if msg == nil then
        return nil
    end

    return json.decode(msg)
end

return P
