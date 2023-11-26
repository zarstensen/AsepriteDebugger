ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

function errorFunc(d)

    if d >= 10 then
        error("ERROR")
    end

    return errorFunc(d + 1)
end

function func()
    local var_1
    
    local var_2
end

function tailCallFunc(d)
    if d >= 10 then
        return
    end

    return tailCallFunc(d + 1)
end

function main()
    func()
    func()
    tailCallFunc(0)
    pcall(errorFunc, 0)
end

main()

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()
