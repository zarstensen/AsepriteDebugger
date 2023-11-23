---@class StepHandler
---@field stack_depth_segments number[]
--- List of numbers, representing the current depth when summed.
--- Whenever a return event is hit, the last element of this list should be popped.
---@field mode string | nil
--- current stepping mode, can be one of the MODE_ variables, or nil, if no stepping is desired.
local P = {
    MODE_STEP_IN = 'MODE_STEP_IN',
    MODE_STEP_OUT = 'MODE_STEP_OUT',

    stack_depth_segments = { },
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
    P.target_depth = P.getStackDepth() - 1
    response:send({})
    ASEDEB.Debugger.resume()
end


---@param args table
---@param response Response
function P.next(args, response)
    P.mode = P.MODE_STEP_OUT
    P.target_depth = P.getStackDepth()
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
    P.updateStackDepthSegments(event)

    if event == 'line' then
        local stack_depth = P.getStackDepth()
        if P.mode == P.MODE_STEP_IN or (P.mode == P.MODE_STEP_OUT and stack_depth <= P.target_depth) then
            ASEDEB.Debugger.stop('step')
        end
    end
end


function P.onStop()
    P.mode = nil
end


--- update the stack depth segments list according to the passed debug hook event.
---@param event string
function P.updateStackDepthSegments(event)
    -- call and tail call needs to be handled seperately,
    -- as all consecutive tail calls are returned, when a return event is fired.
    if event == 'call' then
        table.insert(P.stack_depth_segments, 1)
    elseif event == 'tail call' then
        P.stack_depth_segments[#P.stack_depth_segments] = P.stack_depth_segments[#P.stack_depth_segments] + 1
    elseif event == 'return' then
        table.remove(P.stack_depth_segments, #P.stack_depth_segments)
    end
end


---@return number
function P.getStackDepth()
    local total_depth = 0

    for _, depth in ipairs(P.stack_depth_segments) do
        total_depth = total_depth + depth
    end

    return total_depth
end

return P
