require 'JsonWS'

---@class Debugger
---@field handles table<fun(request: table, response: table, args: table), boolean>
local P = {
    breakpoints = { },
    handles = { },
    _curr_seq = 1,
}

-- Set global table containing debugger equal to the current lua file name.
if _REQUIREDNAME == nil then
    Debugger = P
else
    _G[_REQUIREDNAME] = P
end

---@class Response Class responsible for sending response messages to a specific request.
---@field request table request associated with the Response instance.
local Response = {}

---@param request table
---@return Response response
function Response.new(request)
    local obj = {}
    
    Response.__index = Response
    setmetatable(obj, Response)

    obj.request = request

    return obj
end

--- Construct and send a successfull response table, containing the passed body, to the connected debug adapter.
---@param body table
function Response:send(body)
    local response = {
        type = 'response',
        seq = 0,
        success = true,
        body = body,
        request_seq = self.request.seq,
        command = self.request.command,
    }

    JsonWS.sendJson(P.pipe_ws, response)
end

--- Sends an error response to the connected debug adapter.
---@param error_message string detailed error message displayed to the user.
---@param short_message string | nil short machine readable error message. Defaults to error_message.
function Response:sendError(id, short_message, error_message)
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

    JsonWS.sendJson(P.pipe_ws, response)
end

--- Sets up the debug hook and connects to the debug adapter listening for websockets at the passed endpoint (or app.params.debugger_endpoint)
---@param endpoint string | nil app.params.debugger_endpoint if nil (passed as --script-param debugger_endpoint=<ENDPOINT>).
function P.connect(endpoint)
    
    -- connect to debug adapter
    if app.params then
        endpoint = endpoint or ASEDEB.config.endpoint
    end

    P.pipe_ws = LuaWebSocket()
    P.pipe_ws:connect(endpoint)

    P.handles[P.initialize] = true

    -- setup lua debugger
    debug.sethook(P._debugHook, "clr")
end

function P.connected()
    return P.pipe_ws ~= nil and P.pipe_ws:isConnected()
end

--- Begin initialize process with debug adapter.
function P.init()
    print("Begin initialize message loop")
    P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
end

--- Stop debugging and disconnect from the debug adapter.
function P.deinit()
    debug.sethook(Debugger._debugHook)
    P.pipe_ws:close()
end

-- request handles

---@param args table
---@param response Response
function P.initialize(args, response)
    -- TODO: implement all marked as false.
    response:send({
        supportsConfigurationDoneRequest = true,
        supportsFunctionBreakpoints = false,
        supportsConditionalBreakpoints = false,
        supportsHitConditionalBreakpoints = false,
        supportsEvaluateForHovers = false,
        supportsSetVariable = false,
        supportsExceptionInfo = false,
        supportsDelayedStackTraceLoading=false,
        supportsLogPoints = false,
        supportsSetExpression = false,
        supportsDataBreakpoints = false,
        supportsBreakpointLocationsRequest = false,
    })

    -- initialization has happened before this request, as it primarily consists of setting up request handles, and connecting to the debug adapter,
    -- which is not possible to do during an initialize request, as there would be no handler to handle the request, and no debug adapter connected to recieve the request from.
    P.event("initialized")
end

-- message helpers

--- Dispatches the supplied message to a relevant request handler.
--- If none is found, a not implemented error response is sent back.
---@param message table
function P.handleMessage(message)
    
    local response = Response.new(message)

    if not message then
        response.sendError("Nil Message", "Received nil message")
    end

    if message.type ~= "request" or not P.handles[P[message.command]] then
        response:sendError("Not Implemented", string.format("The %s request is not implemented in the debugger.", message.command))
        return
    end

    P[message.command](message.arguments, response, message)
end

--- Send an event message to the connected debug adapter.
---@param event_type string
---@param body table | nil
function P.event(event_type, body)
    local event = {
        type = 'event',
        seq = P._curr_seq,
        event = event_type,
        body = body
    }

    P._curr_seq = P._curr_seq + 1

    JsonWS.sendJson(P.pipe_ws, event)
end

---comment
---@param msg string
function P.log(msg)
    P.event('output', { output = msg, category = 'console' })
end

-- debug specific

function P._debugHook(event, line)
    -- handle new requests
    -- P.pipe_ws:sendJson(P.newMsg('request_request', { }))
    
    -- while true do
    --     local res = P.pipe_ws:receiveJson()
    -- 
    --     if res.type == 'end_of_requests' then
    --         break
    --     end
    -- 
    --     P.handleMessage(res)
    -- end

    -- check for breakpoints
    -- TODO: implement
end


return P
