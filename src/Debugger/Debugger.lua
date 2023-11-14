require 'JsonWS'

---@class Debugger
---@field handles table<fun(request: table, response: table, args: table), boolean>
---@field _stacktrace table[] current stacktrace if stopped, stored as a list of stackframes,
--- where the top stackframe is the last element in the list.
local P = {
    ERR_NIL = 1,
    ERR_NOT_IMPLEMENTED = 2,

    THREAD_ID = 1,

    breakpoints = { },
    handles = { },
    _curr_breakpoint_id = 0,
    _curr_seq = 1,
    _stopped = true,
    _launched = false,
    
    _stacktrace = {},
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
    P.handles[P.launch] = true
    P.handles[P.setBreakpoints] = true
    P.handles[P.configurationDone] = true
    P.handles[P.threads] = true
    P.handles[P.stackTrace] = true
    P.handles[P.scopes] = true
    P.handles[P.continue] = true

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

--- Stop debugging and disconnect from the debug adapter.
function P.deinit()
    debug.sethook()
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
        supportsDelayedStackTraceLoading = false,
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
function P.setBreakpoints(args, response)
    -- all paths need to point to the actual installed location in the aseprite user config folder,
    -- as we use these files to check for breakpoint validity.
    local response_body = { breakpoints = { } }
    local mapped_source = P.mapSource(args.source.path)

    if mapped_source == nil then
        response:send(response_body)
        return
    end

    P.breakpoints[mapped_source] = { }

    for _, breakpoint in ipairs(args.breakpoints) do
        
        local valid_line = P.validBreakpointLine(breakpoint.line, args.source.path)

        if valid_line then
            P._curr_breakpoint_id = P._curr_breakpoint_id + 1
            P.breakpoints[mapped_source][valid_line] = P._curr_breakpoint_id

            table.insert(response_body.breakpoints, {
                verified = true,
                id = P._curr_breakpoint_id,
                line = valid_line,
                source = args.source
            })
        end
    end

    response:send(response_body)
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

--- Simply convert the current stacktrace stored at P._stacktrace to a valid debug adapter stacktrace response.
---@param args table
---@param response Response
function P.stackTrace(args, response)
    local stack_frames = { }

    for i=args.startFrame + 1,args.levels + 1 do
        if #P._stacktrace < i then
            break;
        end

        table.insert(stack_frames, P._stacktrace[i])
    end

    response:send({
        stackFrames = stack_frames,
        totalFrames = #stack_frames
    })
end

function P.scopes(args, response)
    response:send({ scopes = { } })
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
    local response = Response.new(message)

    if not message then
        response:sendError(P.ERR_NIL, "Nil Value", "Received nil message")
        return
    end

    if message.type ~= "request" or not P.handles[P[message.command]] then
        response:sendError(P.ERR_NOT_IMPLEMENTED, "Not Implemented", string.format("The %s request is not implemented in the debugger.", message.command))
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

--- Sends an output event to the connected debug adapter, with the passed message.
---@param msg string
function P.log(msg)
    P.event('output', { output = msg, category = 'console' })
end

--- Sends an stop event to the connected debug adapter, and registers in the debugger that it is currently stopped.
---@param reason string
---@param description string | nil
function P.stop(reason, description)
    P._stopped = true
    P.event('stopped', { reason = reason, description = description, threadId = P.THREAD_ID, })
end

--- Maps the passed source file, to its actual installed location in the aseprite user cofig directory.
--- If the file is not part of the installed source code, nil is returned instead.
---@param src any
---@return string | nil
function P.mapSource(src)
    local source_dir = app.fs.normalizePath(ASEDEB.config.source_dir)
    local install_dir = app.fs.normalizePath(ASEDEB.config.install_dir)
    
    local _, end_src_dir_index = src:find(source_dir, 1, true)

    if end_src_dir_index == nil then
        return nil
    end

    return app.fs.normalizePath(app.fs.joinPath(install_dir, src:sub(end_src_dir_index + 1)))
end

--- Maps the passed installed source file to its source code location.
---@param isntall_src any
---@return string | nil
function P.mapInstalled(isntall_src)
    local source_dir = app.fs.normalizePath(ASEDEB.config.source_dir)
    local install_dir = app.fs.normalizePath(ASEDEB.config.install_dir)
    
    local _, end_install_dir_index = isntall_src:find(install_dir, 1, true)

    if end_install_dir_index == nil then
        return nil
    end

    return app.fs.normalizePath(app.fs.joinPath(source_dir, isntall_src:sub(end_install_dir_index + 1)))
end

--- Returns a line in the passed file, which contains lua code.
--- The line is guaranteed to be the closest valid breakpoint line, after or at the passed line.
---@param line number
---@param file string
function P.validBreakpointLine(line, file)
    local file_handle, err = io.open(file, 'r')

    local file_line

    local is_inside_comment = false

    local multi_line_comment_start = "^%s*%-%-%[%[.*$"
    local multi_line_comment_end = "^%.*%]%].*$"
    
    -- read lines up until target line, keeping track of multiline comments.
    for i=1,line do
        file_line = file_handle:read("l")
                
        if file_line:match(multi_line_comment_start) then
            is_inside_comment = true
        end

        if is_inside_comment and file_line:match(multi_line_comment_end) then
            is_inside_comment = false
        end

    end

    ---@type number | nil
    local valid_line = line

    while true do
        if file_line == nil then
            valid_line = nil
            break
        end

        if file_line:match(multi_line_comment_start) then
            is_inside_comment = true
        end

        if is_inside_comment and file_line:match(multi_line_comment_end) then
            is_inside_comment = false

            -- remove end of multiline comment, so next step can check if this line contains any other code.
            file_line = file_line:gsub(".*%]%]", "")
        end

        -- match for non code lines.
        if not is_inside_comment and not file_line:match("^%s*%-%-.*$") and not file_line:match("^%s*$") then
            break
        end

        valid_line = valid_line + 1
        file_line = file_handle:read("l")
    end

    file_handle:close()

    return valid_line
end

-- debug specific

function P._debugHook(event, line)

    while P.pipe_ws:hasMessage() do
        P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
    end

    -- check for breakpoints

    -- check if file has breakpoints

    local file_info = debug.getinfo(2, "nS")
    
    local src_key = app.fs.normalizePath(file_info.source:sub(2))

    if file_info.source:sub(1, 1) ~= '@' or not P.breakpoints[src_key] then
        return
    end

    -- check if current line is a breakpoint

    if P.breakpoints[src_key][line] then
        P.stop('breakpoint')
        
        -- generate stacktrace

        local stack_depth = 0
        local stack_info = debug.getinfo(stack_depth + 2, "lnS")

        while stack_info do

            local stack_frame = {
                name = stack_info.name,
                -- id is simply the depth of the frame, as there should be no two stackframes with equal depth.
                id = stack_depth,
                source = {
                    path = P.mapInstalled(stack_info.short_src)
                },
                line = stack_info.currentline,
                -- column has to be specified here, but the debugger does not currently support detecting where in the line we are stopped,
                -- so we just say its always at the start of the line.
                column = 1
            }

            if not stack_frame.source.path then
                -- source was unable to map to a source location.
                stack_frame.source.path = stack_info.short_src
            end

            if not stack_frame.name then
                stack_frame.name = 'main'
            end
            
            table.insert(P._stacktrace, #P._stacktrace + 1, stack_frame)
            
            stack_depth = stack_depth + 1

            stack_info = debug.getinfo(stack_depth + 2, "lnS")
        end

        -- constantly listen for new messages in order to perform a blocking operation,
        -- instead of just checking if new messages have arrived.
        while P._stopped do
            P.handleMessage(JsonWS.receiveJson(P.pipe_ws))
        end

        P._stacktrace = {}
    end
end


return P
