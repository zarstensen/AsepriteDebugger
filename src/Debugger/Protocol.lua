---@class Protocol
local P = {
    EXIT_HEADER = "!<EXIT>",
    LEN_HEADER = "!<LEN>",
    MSG_HEADER = "!<MSG>"
}

if _REQUIREDNAME == nil then
    Protocol = P
else
    _G[_REQUIREDNAME] = P
end

---@param file_handle table
---@return string | nil header
local function getHeader(file_handle)
    local header = file_handle:read(2)

    if header ~= "!<" then
        return nil
    end

    repeat
        local res = file_handle:read(1)

        if res == fail then
            return nil
        end

        header = header .. res
    until header:sub(#header, #header) == '>'

    return header
end

---@param msg string
---@param file_handle table
function P.send(msg, file_handle)
    file_handle:write(P.LEN_HEADER)

    local binary_len = {}

    for i = 0, 3 do
        binary_len[i + 1] = (#msg >> (i * 8)) & 0xFF
    end

    file_handle:write(string.char(table.unpack(binary_len)))

    file_handle:write(P.LEN_HEADER)
    file_handle:write(msg)

    file_handle:flush()
end

---@param file_handle table
---@return string | nil msg nil on exit message
function P.receive(file_handle)

    local header = getHeader(file_handle)

    print("HEADER: ", header)

    if header == P.EXIT_HEADER then
        return nil
    elseif header ~= P.LEN_HEADER then
        error(string.format("First header must be '%s' or '%s', was '%s' instead.", P.EXIT_HEADER, P.LEN_HEADER, header))
    end

    local res = file_handle:read(4)

    if res == nil then
        error("File handle was closed mid receive.")
    end

    local msg_len = 0

    for i = 0, 3 do
        print(res:byte(i + 1))
        msg_len = msg_len | (res:byte(i + 1) << (i * 8))
    end

    print("LEN B: ", res)
    print("MSG LEN: ", msg_len)

    header = getHeader(file_handle)

    print("NEXT HEADER: ", header)

    if header == nil then
        error("File handle was closed mid receive.")
    elseif header ~= P.MSG_HEADER then
        error(string.format("Header following '%s' must be '%s', was '%s' instead.", P.LEN_HEADER, P.MSG_HEADER, header))
    end

    res = file_handle:read(msg_len)

    print("MESSAGE: ", res)

    if res == nil then
        error("File handle was closed mid receive.")
    end

    return res
end

---@param file_handle table
function P.exit(file_handle)
    file_handle:write(P.EXIT_HEADER)
    file_handle:flush()
end

return P