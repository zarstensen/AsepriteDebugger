local json = dofile('json/json.lua')

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
    local response = P.newMsg('response', {
        success = true,
        body = body,
        request_seq = self.request.seq,
        command = self.request.command,
    })

    P.ws:sendText(json.encode(response))
end

--- Sends an error response to the connected debug adapter.
---@param error_message string detailed error message displayed to the user.
---@param short_message string | nil short machine readable error message. Defaults to error_message.
function Response:sendError(error_message, short_message)
    local response = P.newMsg('response', {
        type = 'response',
        success = false,
        message = short_message or error_message,
        body = { error = error_message },
        request_seq = self.request.seq,
        command = self.request.command,
    })

    P.ws:sendText(json.encode(response))
end

--- Sets up the debug hook and connects to the debug adapter listening for websockets at the passed endpoint (or app.params.debugger_endpoint)
---@param endpoint string | nil app.params.debugger_endpoint if nil (passed as --script-param debugger_endpoint=<ENDPOINT>).
function P.init(endpoint)
    -- setup lua debugger
    debug.sethook(P._debugHook, "clr")
    
    -- connect to debug adapter
    if app.params then
        endpoint = endpoint or ASEDEB_CONFIG.endpoint
    end

    P.ws = WebSocket{
        url = endpoint,
        deflate = false,
        onreceive = P._onWebsocketRecieve
    }

    P.ws:connect()

    print(P.ws)

    P.handles[P.initialize] = true

end

--- calls the callback function when the debugger has connected to a debug adapter.
---@param callback fun(): nil
function P.onConnect(callback)
    P._on_connect_callback = callback
end

--- Stop debugging and disconnect from the debug adapter.
function P.deinit()
    debug.sethook(Debugger._debugHook)
    P.ws:close()
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
--- If none is found, an not implemented error response is sent back.
---@param message table
function P.handleMessage(message)
    
    local response = Response.new(message)

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
    local event = P.newMsg('event', {
        event = event_type,
        body = body
    })

    P.ws:sendText(json.encode(event))
end

--- Construct a new message of the specified type.
--- Auto increments the seq field.
---@param type string
---@param body table
---@return table
function P.newMsg(type, body)
    local msg = {
        type = type,
        seq = P._curr_seq
    }

    P._curr_seq = P._curr_seq + 1

    for k, v in pairs(body) do
        msg[k] = v
    end

    return msg
end

-- debug specific

function P._debugHook(event, line)
end

function P._onWebsocketRecieve(message_type, message)
    -- as the websocket recieves happen on a different thread or something,
    -- the xpcall in the entry point will not capture any errors which happen in the following code.
    -- therefore we wrap the body of this function inside another function which we call in protected mode,
    -- to capture and print any errors.
    local function fn()
        if message_type == WebSocketMessageType.OPEN and P._on_connect_callback then
            P._on_connect_callback()
        end

        if message_type == WebSocketMessageType.TEXT then
            local message = json.decode(message)
            P.handleMessage(message)
            -- TODO: handle callbacks
        end
    end

    local result, msg = xpcall(fn, debug.traceback)

    if not result then
        print(msg)
    end

end

return P
