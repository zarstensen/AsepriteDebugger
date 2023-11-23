ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

function func()
    local var_1
    
    local var_2
end

function tailCallFunc(d)
    if d then
        return
    end

    return tailCallFunc(true)
end

function main()
    func()
    func()
    tailCallFunc(false)
end

main()

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()
