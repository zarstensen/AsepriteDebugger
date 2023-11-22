local SourceMapper = require 'SourceMapper'

--- Handler for keeping track of the current stacktrace, informing the client about updates, and providing a stacktrace when requested.
---@class StackTraceHandler
---@field stacktrace table[] current stacktrace, stored as a list of stackframes,
--- where the top stackframe is the first element in the list.
local P = {
    stacktrace = { },
}

---@param handles table<fun(request: table, response: table, args: table), boolean>
function P.register(handles)
    handles[P.stackTrace] = true
end

--- Simply convert the current stacktrace stored at Debugger.stacktrace to a valid debug adapter stacktrace response.
---@param args table
---@param response Response
function P.stackTrace(args, response)
    local stackframes = { }

    if args.levels and args.levels <= 0 then
        args.levels = nil
    end

    local start_frame = args.startFrame or 0
    local frame_count = args.levels or 1000

    local tail_call_count = 0

    for i=1,math.min(start_frame + frame_count, #P.stacktrace) do
        local stackframe_id = i - 1
        local deb_stackframe = P.stacktrace[#P.stacktrace - stackframe_id]
        
        if i >= start_frame + 1 and i <= start_frame + frame_count then
            local stackframe = {
                name = deb_stackframe.name,
                source = deb_stackframe.source,
                line = deb_stackframe.line,
                column = deb_stackframe.column
            }
            
            stackframe.id = stackframe_id - tail_call_count
            table.insert(stackframes, stackframe)
        end
            
        if deb_stackframe.is_tail_call then
            tail_call_count = tail_call_count + 1
        end

    end

    response:send({
        stackFrames = stackframes,
        totalFrames = #P.stacktrace
    })
end

---@param event string
---@param line number | nil
function P.onDebugHook(event, line)
    -- update stacktrace
    
    local stack_trace_update_event = {
        type = event,
        line = line,
    }

    if event == 'call' or event == 'tail call' then
        local stack_info = debug.getinfo(2, 'nlS')
        local new_stack_frame = {
            name = stack_info.name,
            source = {
                path = SourceMapper.mapInstalled(stack_info.short_src)
            },
            line = stack_info.currentline,
            -- column has to be specified here, but the debugger does not currently support detecting where in the line we are stopped,
            -- so we just say its always at the start of the line.
            column = 1
        }

        new_stack_frame.is_tail_call = event == 'tail call'

        if not new_stack_frame.source.path then
            -- source was unable to map to a source location.
            new_stack_frame.source.path = stack_info.short_src
        end

        if not new_stack_frame.name then
            if event == 'call' then
                new_stack_frame.name = '(main)'
            elseif event == 'tail call' then
                new_stack_frame.name = P.stacktrace[#P.stacktrace].name
            end
        end

        stack_trace_update_event.name = new_stack_frame.name
        stack_trace_update_event.source = new_stack_frame.source.path
        stack_trace_update_event.line = new_stack_frame.line

        table.insert(P.stacktrace, #P.stacktrace + 1, new_stack_frame)
    elseif event == 'line' and #P.stacktrace > 0 then
        P.stacktrace[#P.stacktrace].line = line
    elseif event == 'return' then
        local was_tail_call = true
        while #P.stacktrace > 0 and was_tail_call do
            was_tail_call =  P.stacktrace[#P.stacktrace].is_tail_call
            table.remove(P.stacktrace, #P.stacktrace)
        end
    end

    -- we will be unable to communicate with the client if we hit an error,
    -- so we supply an additional stackTraceUpdate event, which the client can handle,
    -- so it can keep track of the current stacktrace and not rely on the StackTraceRequest.
    ASEDEB.Debugger.event('stackTraceUpdate', stack_trace_update_event)
end

return P
