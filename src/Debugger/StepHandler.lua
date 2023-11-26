local StackTraceHandler = require 'StackTraceHandler'

---@class StepHandler
---@field mode string | nil
--- current stepping mode, can be one of the MODE_ variables, or nil, if no stepping is desired.
local P = {
    MODE_STEP_IN = 'MODE_STEP_IN',
    MODE_STEP_OUT = 'MODE_STEP_OUT',
    mode = nil,
    target_depth = nil,
}


---@param handlers table<fun(request: table, response: table, args: table), boolean>
function P.register(handlers)
    handlers[P.stepIn] = true
    handlers[P.stepOut] = true
    handlers[P.next] = true
    handlers[P.pause] = true
end


---@param args table
---@param response Response
function P.stepIn(args, response)
    P.mode = P.MODE_STEP_IN
    response:send({})
    ASEDEB.Debugger.resume()
end


---@param args table
---@param response Response
function P.stepOut(args, response)
    P.mode = P.MODE_STEP_OUT
    P.target_depth = #StackTraceHandler.stacktrace - 1
    response:send({})
    ASEDEB.Debugger.resume()
end

---@param args table
---@param response Response
function P.next(args, response)
    P.mode = P.MODE_STEP_OUT
    P.target_depth = #StackTraceHandler.stacktrace
    response:send({})
    ASEDEB.Debugger.resume()
end


---@param args table
---@param response Response
function P.pause(args, response)
    response:send({})
    ASEDEB.Debugger.stop('pause')
end


---@param event string
---@param line number | nil
function P.onDebugHook(event, line)
    if event == 'line' then
        local stack_depth = #StackTraceHandler.stacktrace
        if P.mode == P.MODE_STEP_IN or (P.mode == P.MODE_STEP_OUT and stack_depth <= P.target_depth) then
            ASEDEB.Debugger.stop('step')
        end
    end
end

function P.onStop()
    P.mode = nil
end

return P
