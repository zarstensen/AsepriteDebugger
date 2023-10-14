local Debugger = ASEDEB.Debugger

Debugger.init()

Debugger.onConnect(function()
    ASEDEB.testAssert(debug.gethook() == Debugger._debugHook, "Debugger hook was not set!")
    ASEDEB.testAssert(Debugger.handles[Debugger.initialize], "Initialize handle was not registered!")
    ASEDEB.stopTest()
end)
