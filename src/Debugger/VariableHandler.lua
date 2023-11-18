require 'Response'

--- Handler class for all things related to variables and code evaluation.
---@class VariableHandler
---
---@field scope_info table<number, table> table mapping a variablesReference value to a scope info table,
--- which contains relevant information for retreiving the scopes variables.
--- to simplify implementation, structured variables (tables, lists, aseprite objects with fields) are also seen as scopes.
---
---@field scope_variable_retreivers table<string, fun(scope_info: table): table[]>
--- table which maps a scope type, to a function which will be able to retreive the variables of this scope type,
--- if given a scope_info table associated witht he scope.
--- each variable retreiver should return a list of debug adapter variables.
---
---@field default_global_fields table<string, boolean>
--- set structure which stores all of the global variables which have been set up until this file is required.
--- is used for filtering global variables between the GLOBAL_DEFAULT_SCOPE and GLOBAL_SCOPE
local P = {
    LOCAL_SCOPE = 'Locals',
    GLOBAL_DEFAULT_SCOPE = 'Globals Default',
    GLOBAL_SCOPE = 'Globals',
    ARGUMENT_SCOPE = 'Arguments',
    UPVALUE_SCOPE = 'Upvalues',
    TEMPORARY_SCOPE = 'Temporaries',
    -- special scope types for retreiving children of structured variables,
    -- should not be returned in the scopes request.
    -- string values of these are never presented to the user.
    TABLE_LIKE_VARIABLE = 'Table Like Variable',
    GETTERS_VARIABLE = 'Getters Variable',

    -- debug.getInfo -[1]-> variableRetreiver -[2]-> variablesRequest -[3]-> handleMessage -[4]-> debugHook -[5]-> relevant code.
    DEPTH_OFFSET = 5,

    scope_info = {},
    scope_variable_retreivers = { },
    curr_scope_id = 1,

    default_global_fields = { },
}

---@param handles table<fun(request: table, response: table, args: table), boolean>
function P.register(handles)
    handles[P.scopes] = true
    handles[P.variables] = true
    handles[P.evaluate] = true
end


function P.onContinue()
    -- variables and scopes should be reretreived on every stop,
    -- so their information is cleared on every continue.
    P.curr_scope_id = 1
    P.scope_info = {}
end

---@param args table
---@param response Response
function P.scopes(args, response)
    response:send({
        scopes = {
            P.createScope(args.frameId, P.LOCAL_SCOPE, 'locals'),
            P.createScope(args.frameId, P.ARGUMENT_SCOPE, 'arguments'),
            P.createScope(args.frameId, P.UPVALUE_SCOPE),
            P.createScope(args.frameId, P.GLOBAL_SCOPE),
            P.createScope(args.frameId, P.GLOBAL_DEFAULT_SCOPE),
            P.createScope(args.frameId, P.TEMPORARY_SCOPE),
        }
    })
end

--- Create a debug adapter scope type with an unique id, from the passed parameters
---@param frameid number
---@param name string
---@param hint string
---@param expensive boolean
---@return table
function P.createScope(frameid, name, hint, expensive)
    local scope = {
        name = name,
        presentationHint = hint,
        expensive = expensive or false,
        variablesReference = P.curr_scope_id
    }

    P.scope_info[P.curr_scope_id] = {
        type = name,
        depth = frameid
    }

    P.curr_scope_id = P.curr_scope_id + 1

    return scope
end

---@param args table
---@param response Response
function P.variables(args, response)
    local scope_info = P.scope_info[args.variablesReference]

    -- functions which are able to retreive the passed scope types variabies are stored in this variable,
    -- for quick retreival here.
    local variable_retreiver = P.scope_variable_retreivers[scope_info.type]

    local response_variables = {}

    if variable_retreiver then
        local variables = variable_retreiver(scope_info)
        
        for _, var in pairs(variables) do

            local variable_entry = {
                name = var.name,
                value = tostring(var.value),
                type = type(var.value),
                evaluateName = var.name,
                variablesReference = 0
            }

            -- special handling is required for structured variables, since we need to figure out how we get their child variables.
            -- __getters is a metamethod aseprite implements for some of its userdata values, which returns all of its gettable members.
            if type(var.value) == 'table' or type(var.value) == 'userdata' and (getmetatable(var.value).__pairs or getmetatable(var.value).__ipairs or getmetatable(var.value).__getters) then
                variable_entry.variablesReference = P.curr_scope_id
                
                local scope_type

                if type(var.value) == 'table' or getmetatable(var.value).__pairs or getmetatable(var.value).__ipairs then
                    scope_type = P.TABLE_LIKE_VARIABLE
                elseif getmetatable(var.value).__getters then
                    scope_type = P.GETTERS_VARIABLE
                end

                P.scope_info[P.curr_scope_id] = { 
                    type = scope_type,
                    value = var.value
                }

                P.curr_scope_id = P.curr_scope_id + 1
            end

            table.insert(response_variables, variable_entry)
        end
    end

    response:send({
        variables = response_variables
    })
end

--- Retreive field and list elements from tables, or objects which implement __pairs and / or __ipairs in their metatable.
---@param scope_info table
---@return table
function P.getTableLikeFields(scope_info)
    local children = { }

    -- retreive key value pairs

    if type(scope_info.value) == 'table' or getmetatable(scope_info.value).__pairs then
        for k, v in pairs(scope_info.value) do

            -- ignore index keys, they will be included further down.

            if type(k) ~= 'number' or k > #scope_info.value then
                table.insert(children, {
                    name = tostring(k),
                    value = v
                })
            end
        end
        -- pairs does not return a consistent order, so we just sort keys alphabetically instead.
    
        table.sort(children, function(a, b) return a.name < b.name end)
    end


    -- retreive list elements

    if type(scope_info.value) == 'table' or getmetatable(scope_info.value).__ipairs then
        for i, v in ipairs(scope_info.value) do
            table.insert(children, {
                name = string.format(string.format("[%%0%ii]", math.ceil(math.log(#scope_info.value + 1, 10))), i),
                value = v
            })
        end
    end

    return children
end

--- Retreive fields from userdata objects which implements __getters in their metatable.
---@param scope_info table
---@return table
function P.getGetterFields(scope_info)
    local children = {}

    for field, getter in pairs(getmetatable(scope_info.value).__getters) do
        print(debug.getinfo(getter, 'u').nparams)
        table.insert(children, {
            name = field,
            -- pass value for non static getters, as it would act as self / this in the method.
            value = getter(scope_info.value)
        })
    end

    table.sort(children, function(a, b) return a.name < b.name end)

    return children
end

--- Retreive function args and varargs.
---@param scope_info table
---@return table
function P.getArgumentVariables(scope_info)
    local arguments = { }

    for param=1, debug.getinfo(scope_info.depth + P.DEPTH_OFFSET, 'uf').nparams do
        local name, value = debug.getlocal(scope_info.depth + P.DEPTH_OFFSET, param)
        table.insert(arguments, {
            name = name,
            value = value
        })
    end

    local var_indx = -1

    while true do
        local name, value = debug.getlocal(scope_info.depth + P.DEPTH_OFFSET, var_indx)

        if not name then
            break
        end

        table.insert(arguments, {
            name = name,
            value = value
        })
        var_indx = var_indx - 1
    end

    return arguments
end

--- Retreive all local, non temporary variables.
--- Temporaries are filtered as it seems aseprite creates a lot of them for various code(?).
--- These can be retreived in the TEMPORARY_SCOPE.
---@param scope_info table
---@return table
function P.getLocalVariables(scope_info)
    local variables = { }

    -- since both arguments and locals are retreived with getlocal,
    -- we also need to offset the index with the amount of arguments to the current function, so we dont include arguments in the local scope.
    local var_index = debug.getinfo(scope_info.depth + P.DEPTH_OFFSET, 'u').nparams + 1

    while true do
        print(var_index)
        local var_name, var_value = debug.getlocal(scope_info.depth + P.DEPTH_OFFSET, var_index)
        var_index = var_index + 1

        if not var_name then
            break
        end

        -- ignore temporaries, these are reserved for the temporaries scope.
        if var_name ~= '(temporary)' then
            table.insert(variables, {
                name = var_name,
                value = var_value
            })
        end
    end

    return variables
end

--- Retreive all global variables not stored in the default_global_fields table.
---@param scope_info table
---@return table
function P.getGlobalVariables(scope_info)
    local globals = {}
    
    for field, value in pairs(_G) do
        if not P.default_global_fields[field] then
            table.insert(globals, {
                name = field,
                value = value
            })
        end
    end
    
    table.sort(globals, function(a, b) return a.name < b.name end)

    return globals
end


--- Retreive all global variables stored in the default_global_fields table.
---@param scope_info table
---@return table
function P.getDefaultGlobalVariables(scope_info)
    local globals = {}
    
    for field, value in pairs(_G) do
        if P.default_global_fields[field] then
            table.insert(globals, {
                name = field,
                value = value
            })
        end
    end
    
    table.sort(globals, function(a, b) return a.name < b.name end)

    return globals
end

---@param scope_info table
---@return table
function P.getUpvalueVariables(scope_info)
    local upvalues = {}

    local deb_info = debug.getinfo(scope_info.depth + P.DEPTH_OFFSET, 'uf')

    for up=1, deb_info.nups do
        local name, value = debug.getupvalue(deb_info.func, up)
        table.insert(upvalues, {
            name = name,
            value = value
        })
    end

    return upvalues
end

--- Retreive all local temporary variables.
---@param scope_info table
---@return table
function P.getTemporaryVariables(scope_info)
    local variables = { }

    local var_index = debug.getinfo(scope_info.depth + P.DEPTH_OFFSET, 'u').nparams + 1

    while true do
        print(var_index)
        local var_name, var_value = debug.getlocal(scope_info.depth + P.DEPTH_OFFSET, var_index)
        var_index = var_index + 1

        if not var_name then
            break
        end

        if var_name == '(temporary)' then
            table.insert(variables, {
                name = var_name,
                value = var_value
            })
        end
    end

    return variables
end

-- tie variable retreivers to their scope type.
P.scope_variable_retreivers[P.ARGUMENT_SCOPE] = P.getArgumentVariables
P.scope_variable_retreivers[P.UPVALUE_SCOPE] = P.getUpvalueVariables
P.scope_variable_retreivers[P.LOCAL_SCOPE] = P.getLocalVariables
P.scope_variable_retreivers[P.GLOBAL_SCOPE] = P.getGlobalVariables
P.scope_variable_retreivers[P.GLOBAL_DEFAULT_SCOPE] = P.getDefaultGlobalVariables
P.scope_variable_retreivers[P.TEMPORARY_SCOPE] = P.getTemporaryVariables
P.scope_variable_retreivers[P.TABLE_LIKE_VARIABLE] = P.getTableLikeFields
P.scope_variable_retreivers[P.GETTERS_VARIABLE] = P.getGetterFields

-- register default global fields.
for field, _ in pairs(_G) do
    P.default_global_fields[field] = true
end

return P
