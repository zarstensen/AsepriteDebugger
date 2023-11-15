local JsonWS = require 'JsonWS'

---@class Response Class responsible for sending response messages to a specific request.
---@field request table request associated with the Response instance.
---@field pipe_ws LuaWebSocket websocket to send response to.
local P = {}

---@param request table
---@param pipe_ws LuaWebSocket
---@return Response response
function P.new(request, pipe_ws)
    local obj = {}
    
    P.__index = P
    setmetatable(obj, P)

    obj.request = request
    obj.pipe_ws = pipe_ws

    return obj
end

--- Construct and send a successfull response table, containing the passed body, to the connected debug adapter.
---@param body table
function P:send(body)
    local response = {
        type = 'response',
        seq = 0,
        success = true,
        body = body,
        request_seq = self.request.seq,
        command = self.request.command,
    }

    JsonWS.sendJson(self.pipe_ws, response)
end

--- Sends an error response to the connected debug adapter.
---@param error_message string detailed error message displayed to the user.
---@param short_message string | nil short machine readable error message. Defaults to error_message.
function P:sendError(id, short_message, error_message)
    short_message = short_message or error_message

    local response = {
        type = 'response',
        seq = 0,
        success = false,
        message = short_message,
        body = {
            error = {
                id = id,
                format = error_message,
                showUser = true
            }
        }
        
    }

    if self.request then
        response.request_seq = self.request.seq
        response.command = self.request.command
    else
        response.request_seq = 1
        response.command = 'initialize'
    end

    JsonWS.sendJson(self.pipe_ws, response)
end

return P
