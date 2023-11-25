local StackTraceHandler = require 'StackTraceHandler'

--- Handler responsible for detecting, and reporting errors to the client.
---@class ErrorHandler
local P = {
    has_error = false,
    error_message = nil,
    error_level = 0,
}

---@param handlers @param handles table<fun(request: table, response: table, args: table), boolean>
function P.register(handlers)
    handlers[P.exceptionInfo] = true
end

---@param args table
---@param response Response
function P.exceptionInfo(args, response)
    response:send({
        exceptionId = P.error_message
    })
end

---@param event string
---@param line number
function P.onDebugHook(event, line)
    if P.has_error then
        -- remove the reportError and error call from the call stack.
        for i=1,P.error_level + 2 do
            StackTraceHandler.popStackFrame()
        end
        
        ASEDEB.Debugger.stop('exception', P.error_message, { allThreadsStopped = true })
    end
end

function P.onContinue()
    -- communication with the debugger is lost on errors,
    -- so inform the client the debug session has stopped.
    if P.has_error then
        ASEDEB.Debugger.deinit()
    end
end

--- register the passed error message for the next onDebugHook event,
--- where the debugger will send an exception stopped event.
---@param message string
---@param pop_stackframes number
function P.reportError(message, pop_stackframes)
    P.error_message = message
    P.error_level = pop_stackframes or 0
    -- important we set this last, as this triggers the exception to be sent on the next debug event.
    P.has_error = true
end

-- provide a custom error function overload, which reports the error to the debugger,
-- before propagating the error up the call stack.
local orig_error = error
function error(message, level)

    -- do not report error if pcall or xpcall exists in the call stack,
    -- since these will catch the error.

    local in_pcall = false

    for i, frame in ipairs(StackTraceHandler.stacktrace) do
        if frame.name == 'pcall' or frame.name == 'xpcall' then
            in_pcall = true
            break
        end
    end

    if not in_pcall then
        P.reportError(message, level)
    end

    orig_error(message, level)
end

return P
