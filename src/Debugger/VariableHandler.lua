
---@class VariableHandler
local P = {
    current_scopes = {},
    curr_scope_id = 0,

    LOCAL_SCOPE = 'Local',
    GLOBAL_SCOPE = 'Global',
    GLOBAL_USER_SCOPE = 'User Global',
    ARGUMENT_SCOPE = 'Argument',
    UPVALUE_SCOPE = 'Upvalue',
}

function P.register(handles)
    handles[P.scopes] = true
end

function P.scopes(args, response)

    response:send({
        scopes = {
            P.createScope(args.frameid, P.LOCAL_SCOPE, 'locals'),
            P.createScope(args.frameid, P.GLOBAL_SCOPE, nil, true),
            P.createScope(args.frameid, P.GLOBAL_USER_SCOPE),
            P.createScope(args.frameid, P.ARGUMENT_SCOPE),
            P.createScope(args.frameid, P.UPVALUE_SCOPE),
        }
    })
end

function P.createScope(frameid, name, hint, expensive)
    local scope = {
        name = name,
        presentationHint = hint,
        isExpensive = expensive or false,
        variablesReference = P.curr_scope_id
    }

    P.current_scopes[P.curr_scope_id] = {
        type = name,
        depth = frameid
    }

    P.curr_scope_id = P.curr_scope_id + 1

    return scope
end

return P
