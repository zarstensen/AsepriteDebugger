local JsonWS = require 'JsonWS'
local Response = require 'Response'
local BreakpointHandler = require 'BreakpointHandler'
local VariableHandler = require 'VariableHandler'
local StackTraceHandler = require 'StackTraceHandler'
local StepHandler = require 'StepHandler'
local ErrorHandler = require 'ErrorHandler'

---@class Debugger
---@field handles table<fun(request: table, response: table, args: table), boolean>
---@field handlers table[] list of handler classes, which implement a set of response handlers.
--- Each handler class must implement an register method, which takes handles as its argument,
--- and registers all of its requests handles.
--- Each handler class can optionally implement an onStop and onContinue function, which take no parameters,
--- and are called whenever a stop event occurs or an continue / step request is received.
--- Each handler class can optionally implement an onDebugHook function, which is called any time the debuggers debug hook is fired.
--- the current event and optionally line will be passed to this function.
--- onDebugHook is called before any new requests from the client is handled.
local P = {
    ERR_NIL = 1,
    ERR_NOT_IMPLEMENTED = 2,
    ERR_EVALUATION_FAILED = 3,
    ERR_INVALID_SRC_FILE = 4,

    THREAD_ID = 1,

    -- debug.getInfo -[1]-> onStop -[2]-> debugHook -[3]-> relevant code.
    HANDLER_DEPTH_OFFSET = 3,

    handles = { },
    handlers = { StackTraceHandler, ErrorHandler, BreakpointHandler, VariableHandler, StepHandler },
    
    curr_stack_depth = 0,
    _curr_seq = 1,
    _stopped = true,
    _launched = false,
}

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
    P.handles[P.launch] = true
    P.handles[P.configurationDone] = true
    P.handles[P.threads] = true
    P.handles[P.continue] = true

    for _, handler in ipairs(P.handlers) do
        handler.register(P.handles)
    end

    -- setup lua debugger

    debug.sethook(P._debugHook, "clr")
end

function P.connected()
    return P.pipe_ws ~= nil and P.pipe_ws:isConnected()
end

--- Begin initialize process with debug adapter.
function P.init()
    while P._stopped or not P._launched do
        P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
    end
end

--- Stop debugging and disconnect from the debug adapter after sending terminated event.
function P.deinit()
    if(P.pipe_ws:isConnected()) then
        P.event('terminated')
        debug.sethook()
        P.pipe_ws:close()
    end
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
        supportsExceptionInfoRequest = true,
        supportsDelayedStackTraceLoading = true,
        supportsLogPoints = false,
        supportsSetExpression = false,
        supportsDataBreakpoints = false,
        supportsBreakpointLocationsRequest = false,
    })

    -- initialization has happened before this request, as it primarily consists of setting up request handles, and connecting to the debug adapter,
    -- which is not possible to do during an initialize request, as there would be no handler to handle the request, and no debug adapter connected to recieve the request from.
    P.event("initialized")
end



---@param args table
---@param response Response
function P.configurationDone(args, response)
    P._stopped = false
    response:send({})
end

---@param args table
---@param response Response
function P.launch(args, response)
    P._launched = true
    response:send({})
end

--- Debugger only works with one thread, since lua has no multithreading (except maybe for asepite websockets?),
--- so we simply return a default main thread here.
---@param args table
---@param response Response
function P.threads(args, response)
    response:send({ threads = { { id = P.THREAD_ID, name = 'Main Thread' } }})
end

---@param args table
---@param response Response
function P.continue(args, response)
    P._stopped = false
    response:send({ allThreadsContinued = true })
end



-- message helpers

--- Dispatches the supplied message to a relevant request handler.
--- If none is found, a not implemented error response is sent back.
---@param message table
function P.handleMessage(message)
    local response = Response.new(message, P.pipe_ws)

    if not message then
        response:sendError(P.ERR_NIL, "Nil Value", "Received nil message")
        return
    end

    if message.type ~= "request" then
        response:sendError(P.ERR_NOT_IMPLEMENTED, "Not Implemented", string.format("The %s message is not implemented in the debugger, as it is not a request type.", message.command))
        return
    end

    -- find the handler class which implements the request handle for the received message.
    -- the debugger class itself is also included in this search.

    local handler_class

    for _, handler in ipairs({P, table.unpack(P.handlers)}) do
        if P.handles[handler[message.command]] then
            handler_class = handler
            break
        end
    end

    if not handler_class then
        response:sendError(P.ERR_NOT_IMPLEMENTED, "Not Implemented", string.format("The %s message is not implemented in the debugger or any of its handlers.", message.command))
        return
    end

    handler_class[message.command](message.arguments, response, message)
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

--- Sends an output event to the connected debug adapter, with the passed message.
---@param msg string
function P.log(msg)
    P.event('output', { output = msg, category = 'console' })
end

--- Sends an stop event to the connected debug adapter, and registers in the debugger that it is currently stopped.
---@param reason string
---@param description string | nil
---@param additional_info table | nil
function P.stop(reason, description, additional_info)
    P._stopped = true
    additional_info = additional_info or { }

    local body = { reason = reason, description = description, threadId = P.THREAD_ID }

    for k, v in pairs(additional_info) do
        body[k] = v
    end

    P.event('stopped', body)
end

--- Resumes debugged program, if in a suspended state.
function P.resume()
    P._stopped = false
end

---@return boolean
function P.isStopped()
    return P._stopped
end

-- debug specific

function P._debugHook(event, line)
    -- handle any new client requests.
    for _, handler in ipairs(P.handlers) do
        if type(handler.onDebugHook) == 'function' then
            handler.onDebugHook(event, line)
        end
    end

    while P.pipe_ws:hasMessage() do
        P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
    end

    if P._stopped then
        for _, handler in ipairs(P.handlers) do
            if type(handler.onStop) == 'function' then
                handler.onStop()
            end
        end
        
        -- constantly listen for new messages in order to perform a blocking operation,
        -- instead of just checking if new messages have arrived.
        while P._stopped do
            P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
        end

        for _, handler in ipairs(P.handlers) do
            if type(handler.onContinue) == 'function' then
                handler.onContinue()
            end
        end
    end
end

return P
