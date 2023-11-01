local Protocol = ASEDEB.Protocol
local json = ASEDEB.json

print("Protocol: ", Protocol, ASEDEB.Protocol)

---@class PipeWebSocket
local P = {}

if _REQUIREDNAME == nil then
    PipeWebSocket = P
else
    _G[_REQUIREDNAME] = P
end

function P.new(pipe_websocket_path)
    local obj = {}
    
    P.__index = P
    setmetatable(obj, P)
    
    obj._pipe_in = nil
    obj._pipe_out = nil
    obj._pipe_websocket_path = pipe_websocket_path

    return obj
end

function P:connect(server_ip, websocket_uri)
    self._pipe_in = io.popen(string.format("\"\"%s\" --server \"%s\"\"", self._pipe_websocket_path, websocket_uri), 'rb')
    
    local port = tonumber(Protocol.receive(self._pipe_in))

    self._pipe_out = io.popen(string.format("\"\"%s\" --client \"%s\" \"%s\"", self._pipe_websocket_path, server_ip, port), 'w')
end

function P:close()
    Protocol.exit(self._pipe_out)
end

function P:send(message)
    Protocol.send(message, self._pipe_out)
end

function P:sendJson(json_message)
    self:send(json.encode(json_message))
end

function P:receive()
    return Protocol.receive(self._pipe_in)
end

function P:receiveJson()
    return json.decode(self:receive())
end

return P
